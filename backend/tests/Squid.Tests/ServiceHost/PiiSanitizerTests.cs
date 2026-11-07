using System.Text.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;
using Squid.Service;

namespace Squid.Tests.ServiceHost;

/// <summary>
/// Unit tests for the two PII layers of TASK-0006: <see cref="PiiSanitizingEnricher"/> masks
/// sensitive property names — nested ones included — before anything is rendered, and
/// <see cref="PiiMaskingFormatter"/> masks email/phone/key literals in the formatted JSON line
/// without breaking the JSON. End-to-end output is asserted in ObservabilityTests (AC-04).
/// </summary>
public sealed class PiiSanitizerTests
{
    [Fact]
    public void Literal_pii_in_the_message_is_masked_in_the_json_line()
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.Information("falha no envio para jo.silva@example.com com telefone (11) 91234-5678");

        var line = Format(sink.SingleEvent());

        Assert.DoesNotContain("jo.silva@example.com", line, StringComparison.Ordinal);
        Assert.DoesNotContain("91234-5678", line, StringComparison.Ordinal);
        Assert.Contains("***@***", line, StringComparison.Ordinal);
        Assert.Contains("***", line, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(line);
        Assert.Equal("Information", json.RootElement.GetProperty("Level").GetString());
    }

    [Fact]
    public void Sensitive_property_names_are_masked_even_inside_a_destructured_object()
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.Information("usuario {@User}", new { Email = "jo.silva@example.com", Name = "jo" });

        var line = Format(sink.SingleEvent());

        Assert.DoesNotContain("jo.silva@example.com", line, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(line);
        var user = json.RootElement.GetProperty("Properties").GetProperty("User");
        Assert.Equal("[MASKED]", user.GetProperty("Email").GetString());
        Assert.Equal("jo", user.GetProperty("Name").GetString()); // the innocent value survives
    }

    [Fact]
    public void Masking_keeps_json_structure_numbers_and_timestamps_intact()
    {
        var line = PiiMaskingFormatter.Mask(
            "{\"Count\":123456789,"
            + "\"Timestamp\":\"2026-11-06T13:40:57.2009086-03:00\","
            + "\"Message\":\"contato jo@x.com\"}");

        using var json = JsonDocument.Parse(line);
        Assert.Equal(123456789, json.RootElement.GetProperty("Count").GetInt64());
        Assert.Equal("2026-11-06T13:40:57.2009086-03:00", json.RootElement.GetProperty("Timestamp").GetString());
        Assert.DoesNotContain("jo@x.com", json.RootElement.GetProperty("Message").GetString(), StringComparison.Ordinal);
    }

    /// <summary>Runs the enricher and the formatter the way the host pipeline does.</summary>
    private static string Format(LogEvent logEvent)
    {
        new PiiSanitizingEnricher().Enrich(logEvent, null!); // the enricher never needs the factory
        var writer = new StringWriter();
        new PiiMaskingFormatter(new JsonFormatter(renderMessage: true)).Format(logEvent, writer);
        return writer.ToString();
    }

    private sealed class CaptureSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public void Emit(LogEvent logEvent) => _events.Add(logEvent);

        public LogEvent SingleEvent() => Assert.Single(_events);
    }
}
