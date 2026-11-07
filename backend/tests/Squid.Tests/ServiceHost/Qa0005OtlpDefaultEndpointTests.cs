using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Squid.Service;

namespace Squid.Tests.ServiceHost;

/// <summary>
/// QA-0005 / AC-02 of TASK-0006, end to end: DEV's unit case proves <c>OtlpEndpoint.Resolve</c>
/// returns the OTLP default, and this one proves the HOST really uses that value. With
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> unset, a <c>GET /health</c> must leave as an OTLP export on
/// <c>http://localhost:4317</c> — the receiver below binds exactly that port, so an export that
/// leaves via any other address (or nowhere) fails here instead of passing through the pure
/// function. SPEC-0002 puts a real collector out of dev scope, so 4317 is free; if another process
/// owns it the precondition assert names that, never a silent pass. Shares the ServiceHost
/// collection because localhost clustering fixes the silo ports for every host.
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0005OtlpDefaultEndpointTests
{
    private const int OtlpDefaultPort = 4317;

    [Fact] // AC-02 (QA end-to-end complement)
    public async Task Without_the_environment_variable_the_host_exports_to_localhost_4317()
    {
        var previous = Environment.GetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable);
        Environment.SetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable, null);
        try
        {
            var receiver = await DefaultPortReceiver.StartAsync(OtlpDefaultPort);
            try
            {
                await using var host = await StartHostAsync();

                var response = await host.Http.GetAsync("/health");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                // The batch delay is 5 s; flushing makes the proof deterministic instead of waiting.
                host.App.Services.GetRequiredService<TracerProvider>().ForceFlush();

                Assert.True(
                    await receiver.WaitForRequestAsync(TimeSpan.FromSeconds(30)),
                    $"no OTLP export arrived on http://localhost:{OtlpDefaultPort} with "
                    + $"{OtlpEndpoint.EnvironmentVariable} unset");
                Assert.Contains("GET /health", receiver.LastBodyText, StringComparison.Ordinal);
            }
            finally
            {
                await receiver.DisposeAsync();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable, previous);
        }
    }

    /// <summary>OTLP/gRPC receiver bound to the OTLP default port, counting export requests.</summary>
    private sealed class DefaultPortReceiver : IAsyncDisposable
    {
        private const string ExportPath = "/opentelemetry.proto.collector.trace.v1.TraceService/Export";

        private readonly WebApplication _app;
        private readonly List<byte[]> _bodies;

        private DefaultPortReceiver(WebApplication app, List<byte[]> bodies)
        {
            _app = app;
            _bodies = bodies;
        }

        /// <summary>UTF-8 text of the most recent export request body (the protobuf batch).</summary>
        public string LastBodyText
        {
            get
            {
                lock (_bodies) return _bodies.Count == 0 ? string.Empty : Encoding.UTF8.GetString(_bodies[^1]);
            }
        }

        /// <summary>Starts the receiver on the fixed OTLP default port (HTTP/2 cleartext).</summary>
        public static async Task<DefaultPortReceiver> StartAsync(int port)
        {
            var bodies = new List<byte[]>();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options =>
            {
                // "localhost" resolves to both loopbacks, so both are pinned to HTTP/2 cleartext —
                // the protocol OTLP/gRPC needs on a port without TLS (measured by DEV's probe).
                options.Listen(IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http2);
                if (Socket.OSSupportsIPv6)
                {
                    options.Listen(IPAddress.IPv6Loopback, port, listen => listen.Protocols = HttpProtocols.Http2);
                }
            });
            var app = builder.Build();

            app.Map(ExportPath, async context =>
            {
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body);
                lock (bodies) bodies.Add(body.ToArray());

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/grpc";
                context.Response.Headers["grpc-status"] = "0";
                await context.Response.CompleteAsync();
            });

            try
            {
                await app.StartAsync();
            }
            catch (Exception ex)
            {
                await app.DisposeAsync();
                Assert.Fail(
                    $"precondition: port {port} must be free for the AC-02 default-endpoint proof "
                    + $"(SPEC-0002 keeps a real collector out of dev): {ex.Message}");
                throw; // unreachable: Assert.Fail throws
            }

            return new DefaultPortReceiver(app, bodies);
        }

        /// <summary>Waits until at least one export request has arrived.</summary>
        public async Task<bool> WaitForRequestAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (RequestCount == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            return RequestCount > 0;
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

        private int RequestCount
        {
            get
            {
                lock (_bodies) return _bodies.Count;
            }
        }
    }

    private static async Task<RunningHost> StartHostAsync()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"squid-qa0005-{Guid.NewGuid():N}.db");
        var app = SquidServiceHost.Build(["--Storage:Path=" + storagePath]);
        app.Urls.Add("http://127.0.0.1:0"); // dynamic port: tests never fight over a fixed one
        await app.StartAsync();

        var baseUrl = app.Urls.Single();
        return new RunningHost(app, new HttpClient { BaseAddress = new Uri(baseUrl) }, storagePath);
    }

    private sealed record RunningHost(WebApplication App, HttpClient Http, string StoragePath) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            try
            {
                File.Delete(StoragePath);
            }
            catch (IOException)
            {
                // a leftover temp file is not a product failure; the OS cleans it up
            }
        }
    }
}
