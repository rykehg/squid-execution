using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Squid.Service;

namespace Squid.Tests.ServiceHost;

/// <summary>
/// AC-01..AC-04 of TASK-0006 against the host <see cref="SquidServiceHost"/> builds: a request
/// leaves as an OTLP span, the endpoint resolves per GAP-09, an unreachable collector only
/// warns, and PII never reaches the console output. The class shares the ServiceHost collection
/// on purpose: localhost clustering fixes the silo ports, so two hosts must never overlap.
/// </summary>
[Collection("ServiceHost")]
public sealed class ObservabilityTests
{
    [Fact] // AC-01
    public async Task A_health_request_reaches_the_otlp_receiver_as_an_exported_span()
    {
        await using var receiver = OtlpReceiver.Start();
        var previous = Environment.GetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable);
        Environment.SetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable, receiver.Endpoint.ToString());
        try
        {
            await using var host = await StartHostAsync();
            var before = receiver.RequestCount;

            var response = await host.Http.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // The batch delay is 5 s; flushing makes the proof deterministic instead of waiting.
            host.App.Services.GetRequiredService<TracerProvider>().ForceFlush();

            Assert.True(
                await receiver.WaitForRequestAsync(before, TimeSpan.FromSeconds(30)),
                "no OTLP export arrived at the local receiver");
            // The counter moved before/after and the batch carries the span of that request.
            Assert.Contains("GET /health", receiver.LastBodyText, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable, previous);
        }
    }

    [Fact] // AC-02
    public void The_otlp_endpoint_defaults_to_the_otlp_port_without_the_environment_variable()
    {
        Assert.Equal("http://localhost:4317", OtlpEndpoint.Resolve(null));
        Assert.Equal("http://localhost:4317", OtlpEndpoint.Resolve("   "));
        Assert.Equal("http://collector.internal:4317", OtlpEndpoint.Resolve("http://collector.internal:4317"));
    }

    [Fact] // AC-03
    public async Task An_unreachable_collector_keeps_the_host_healthy_and_warns_in_the_log()
    {
        var closedPort = FindClosedPort();
        var previous = Environment.GetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable);
        Environment.SetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable, $"http://127.0.0.1:{closedPort}");

        var output = new StringWriter();
        var console = Console.Out;
        Console.SetOut(output); // the Serilog console sink writes to the current Console.Out
        try
        {
            await using var host = await StartHostAsync();
            Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/health")).StatusCode);

            host.App.Services.GetRequiredService<TracerProvider>().ForceFlush();
            var endpoint = $"127.0.0.1:{closedPort}";
            var text = await WaitForOutputAsync(output, line =>
                line.Contains(endpoint, StringComparison.Ordinal)
                && line.Contains("OTLP export failed", StringComparison.Ordinal));

            // The export error is a Warning in the log and it names our endpoint, not another
            // host's default one; nothing was swallowed silently.
            Assert.Contains(endpoint, text, StringComparison.Ordinal);
            Assert.Contains("OTLP export failed", text, StringComparison.Ordinal);
            Assert.Contains("\"Level\":\"Warning\"", text, StringComparison.Ordinal);

            // And the failure never reached the client: the host still answers 200 afterwards.
            Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/health")).StatusCode);
        }
        finally
        {
            Console.SetOut(console);
            Environment.SetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable, previous);
        }
    }

    [Fact] // AC-04
    public async Task Structured_logs_reach_the_output_with_pii_masked()
    {
        var output = new StringWriter();
        var console = Console.Out;
        Console.SetOut(output);
        try
        {
            await using var host = await StartHostAsync();
            var logger = host.App.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PiiProbe");
            logger.LogInformation(
                "contato {Email} telefone {Phone} chave {Key}",
                "jo.silva@example.com",
                "(11) 91234-5678",
                "rk_Ab3DeFgHiJkLmNoPqRsTuVwXyZ123456");

            var text = await WaitForOutputAsync(output, candidate =>
                candidate.Contains("contato", StringComparison.Ordinal) && candidate.StartsWith('{'));
            var everything = output.ToString();
            var line = text.Split('\n').First(candidate =>
                candidate.Contains("contato", StringComparison.Ordinal) && candidate.StartsWith('{'));

            // No raw PII in any line of the output, ours included.
            Assert.DoesNotContain("jo.silva@example.com", everything, StringComparison.Ordinal);
            Assert.DoesNotContain("91234-5678", everything, StringComparison.Ordinal);
            Assert.DoesNotContain("Ab3DeFgHiJk", everything, StringComparison.Ordinal);

            // The line itself is structured key-value JSON with the values masked, not dropped.
            using var json = JsonDocument.Parse(line);
            Assert.Equal("Information", json.RootElement.GetProperty("Level").GetString());
            var properties = json.RootElement.GetProperty("Properties");
            Assert.Equal("[MASKED]", properties.GetProperty("Email").GetString());
            Assert.Equal("[MASKED]", properties.GetProperty("Phone").GetString());
            Assert.Equal("[MASKED]", properties.GetProperty("Key").GetString());
            Assert.Contains("contato", json.RootElement.GetProperty("RenderedMessage").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    /// <summary>Waits until any line of the captured output matches, returning the whole output.</summary>
    private static async Task<string> WaitForOutputAsync(StringWriter output, Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var text = output.ToString();
            if (text.Split('\n').Any(line => predicate(line)) || predicate(text)) return text;
            await Task.Delay(100);
        }

        return output.ToString();
    }

    /// <summary>A loopback port that was open a moment ago and is now closed (nothing listens).</summary>
    private static int FindClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<RunningHost> StartHostAsync()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"squid-obs-{Guid.NewGuid():N}.db");
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
