using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Squid.Service;

namespace Squid.Tests.ServiceHost;

/// <summary>
/// QA-0005 / AC-04 second half (TASK-0006): PII written straight into the message TEMPLATE — the
/// path only <see cref="PiiMaskingFormatter"/> can catch, because <see cref="PiiSanitizingEnricher"/>
/// masks property names, not rendered text. DEV's AC-04 case proves property masking through the
/// host; this one proves the host wiring really attaches the formatter: detach it from
/// <c>SquidServiceHost</c> (mutation QA-M5) and the raw address below reaches the console line,
/// failing this test while every other case stays green. Shares the ServiceHost collection because
/// localhost clustering fixes the silo ports for every host.
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0005HostLiteralPiiTests
{
    [Fact] // AC-04 (QA complement: literal PII in the message template, through the host pipeline)
    public async Task Literal_pii_in_a_template_message_never_reaches_the_console_output()
    {
        var output = new StringWriter();
        var console = Console.Out;
        Console.SetOut(output); // the Serilog console sink writes to the current Console.Out
        try
        {
            await using var host = await StartHostAsync();
            var logger = host.App.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PiiLiteralProbe");
            logger.LogInformation(
                "envio para jo.silva@example.com com telefone (11) 91234-5678 chave "
                + "rk_Ab3DeFgHiJkLmNoPqRsTuVwXyZ123456");

            var everything = await WaitForOutputAsync(output, candidate =>
                candidate.Contains("envio para", StringComparison.Ordinal) && candidate.StartsWith('{'));
            var line = everything.Split('\n').First(candidate =>
                candidate.Contains("envio para", StringComparison.Ordinal) && candidate.StartsWith('{'));

            // No raw PII in any line of the output, ours included.
            Assert.DoesNotContain("jo.silva@example.com", everything, StringComparison.Ordinal);
            Assert.DoesNotContain("91234-5678", everything, StringComparison.Ordinal);
            Assert.DoesNotContain("Ab3DeFgHiJk", everything, StringComparison.Ordinal);

            // The PII was masked, not dropped: the message still renders, as structured JSON.
            Assert.Contains("***@***", line, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(line);
            Assert.Contains(
                "envio para",
                json.RootElement.GetProperty("RenderedMessage").GetString(),
                StringComparison.Ordinal);
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

    private static async Task<RunningHost> StartHostAsync()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"squid-qa0005pii-{Guid.NewGuid():N}.db");
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
