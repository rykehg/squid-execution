using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Squid.Tests.ServiceHost;

/// <summary>
/// Local OTLP receiver for the tests (SPEC-0002 puts the real collector out of scope until the
/// compose delivery): a loopback Kestrel endpoint that counts export requests and keeps the last
/// body, so a test can prove a span arrived. OTLP speaks gRPC over HTTP/2, which Kestrel serves
/// on a cleartext port only when the protocol is pinned to <c>Http2</c> — with the default
/// <c>Http1AndHttp2</c> it demands TLS and answers HTTP_1_1_REQUIRED (measured by probe).
/// </summary>
internal sealed class OtlpReceiver : IAsyncDisposable
{
    private const string ExportPath = "/opentelemetry.proto.collector.trace.v1.TraceService/Export";

    private readonly WebApplication _app;
    private readonly List<byte[]> _bodies;

    private OtlpReceiver(WebApplication app, List<byte[]> bodies, Uri endpoint)
    {
        _app = app;
        _bodies = bodies;
        Endpoint = endpoint;
    }

    /// <summary>Base URL to hand to <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.</summary>
    public Uri Endpoint { get; }

    /// <summary>Number of OTLP export requests received so far.</summary>
    public int RequestCount
    {
        get
        {
            lock (_bodies) return _bodies.Count;
        }
    }

    /// <summary>UTF-8 text of the most recent export request body (the protobuf batch).</summary>
    public string LastBodyText
    {
        get
        {
            lock (_bodies) return _bodies.Count == 0 ? string.Empty : Encoding.UTF8.GetString(_bodies[^1]);
        }
    }

    /// <summary>Starts the receiver on a free loopback port (HTTP/2 cleartext, as OTLP/gRPC needs).</summary>
    public static OtlpReceiver Start()
    {
        var bodies = new List<byte[]>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        var app = builder.Build();

        app.Map(ExportPath, async context =>
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            lock (bodies) bodies.Add(body.ToArray());

            // A200 with grpc-status 0 and an empty message is a valid gRPC success response;
            // the exporter accepts it without parsing our payload (probe-verified).
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/grpc";
            context.Response.Headers["grpc-status"] = "0";
            await context.Response.CompleteAsync();
        });

        app.StartAsync().GetAwaiter().GetResult();
        var port = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Select(address => new Uri(address).Port)
            .First();
        return new OtlpReceiver(app, bodies, new Uri($"http://127.0.0.1:{port}"));
    }

    /// <summary>Waits until more than <paramref name="previousCount"/> requests have arrived.</summary>
    public async Task<bool> WaitForRequestAsync(int previousCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (RequestCount <= previousCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        return RequestCount > previousCount;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        catch (IOException)
        {
            // A receiver that is already gone is not a test failure.
        }
    }
}
