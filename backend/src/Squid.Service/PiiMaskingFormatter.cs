using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Formatting;

namespace Squid.Service;

/// <summary>
/// The last gate in front of the console sink (SPEC-0002 GAP-10): formats the event with the
/// inner formatter — structured JSON — and then masks PII in the resulting line. Email addresses,
/// phone numbers and Squid key material (<c>rk_</c>/<c>rc_</c>) are replaced inside JSON string
/// tokens only, so numbers, structure and timestamps stay parseable. This catches what the
/// enricher cannot see: a PII value written straight into the message template.
/// </summary>
public sealed class PiiMaskingFormatter(ITextFormatter inner) : ITextFormatter
{
    private static readonly Regex JsonStringToken = new("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);
    private static readonly Regex Email = new(@"[\w.+-]+@[\w-]+\.[\w.-]{2,}", RegexOptions.Compiled);
    private static readonly Regex InternationalPhone = new(@"\+\d[\d\s().-]{7,}\d", RegexOptions.Compiled);
    private static readonly Regex ParenthesisedPhone = new(@"\(\d{2,3}\)[\s.-]?\d{4,5}[\s.-]?\d{4}", RegexOptions.Compiled);
    private static readonly Regex SeparatedPhone = new(@"\b\d{2,3}[\s-]\d{4,5}[\s.-]?\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex SquidKey = new(@"\b(?:rk|rc)_[A-Za-z0-9+/=_-]{16,}\b", RegexOptions.Compiled);
    private static readonly Regex BarePhone = new(@"\b\d{8,15}\b", RegexOptions.Compiled);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var buffer = new StringWriter();
        inner.Format(logEvent, buffer);
        output.Write(Mask(buffer.ToString()));
    }

    /// <summary>Masks PII inside every JSON string token of an already formatted line.</summary>
    public static string Mask(string jsonLine) =>
        JsonStringToken.Replace(jsonLine, Match => MaskToken(Match.Value));

    private static string MaskToken(string token)
    {
        var inner = token[1..^1];
        inner = Email.Replace(inner, "***@***");
        inner = InternationalPhone.Replace(inner, "***");
        inner = ParenthesisedPhone.Replace(inner, "***");
        inner = SeparatedPhone.Replace(inner, "***");
        inner = SquidKey.Replace(inner, "***");
        inner = BarePhone.Replace(inner, "***");
        return "\"" + inner + "\"";
    }
}
