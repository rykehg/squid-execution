using Serilog.Core;
using Serilog.Events;

namespace Squid.Service;

/// <summary>
/// Masks PII on every event before it reaches a sink (doc 05 §4.7 #12, SPEC-0002): a property
/// whose name carries identity, contact or secret material has its whole value replaced by
/// <c>[MASKED]</c>, so neither the structured property nor the rendered message — which renders
/// from the same properties — can show it. Nested structures are rewritten the same way; values
/// without a sensitive name are caught by <see cref="PiiMaskingFormatter"/> at output time.
/// </summary>
public sealed class PiiSanitizingEnricher : ILogEventEnricher
{
    private const string Masked = "[MASKED]";

    /// <summary>Lower-case name fragments that mark a property as sensitive.</summary>
    private static readonly string[] SensitiveFragments =
    [
        "key", "token", "secret", "password", "passwd", "pwd", "credential", "payload",
        "authorization", "email", "phone", "telefone", "mobile", "chave", "cookie",
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToList())
        {
            var value = IsSensitive(property.Key) ? new ScalarValue(Masked) : Rewrite(property.Value);
            if (!ReferenceEquals(value, property.Value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, value));
            }
        }
    }

    private static bool IsSensitive(string name)
    {
        var lower = name.ToLowerInvariant();
        return SensitiveFragments.Any(fragment => lower.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>Returns the value with every sensitive name inside it masked; the same instance
    /// when nothing changed, so untouched properties are never rewritten.</summary>
    private static LogEventPropertyValue Rewrite(LogEventPropertyValue value) => value switch
    {
        StructureValue structure => new StructureValue(
            structure.Properties.Select(property => new LogEventProperty(
                property.Name,
                IsSensitive(property.Name) ? new ScalarValue(Masked) : Rewrite(property.Value))),
            structure.TypeTag),
        SequenceValue sequence => new SequenceValue(sequence.Elements.Select(Rewrite)),
        DictionaryValue dictionary => new DictionaryValue(
            dictionary.Elements.Select(entry => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                entry.Key,
                IsSensitive(entry.Key.Value?.ToString() ?? string.Empty)
                    ? new ScalarValue(Masked)
                    : Rewrite(entry.Value)))),
        _ => value,
    };
}
