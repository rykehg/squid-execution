using System.Text;

namespace Squid.Domain.Objects;

/// <summary>
/// GAP-24's server-side sanitization of SPEC-0009 section A (<c>Content</c> sanitizado no
/// servidor): the five HTML metacharacters are escaped and the C0 controls are dropped before
/// the text is stored, so the server never depends on the front escaping on its way out. Only
/// the metacharacters move — accents, emoji and every other character of the text travel as
/// written, because a full-ASCII encoder would turn each accented word of the instance's own
/// language into a numeric entity. Tab, LF and CR survive: they are the text's own whitespace,
/// not controls to strip, and DEL (U+007F) goes with the C0 family.
/// </summary>
public static class PostSanitizer
{
    /// <summary>Escapes <paramref name="content"/> and removes every C0 control but tab/LF/CR.</summary>
    public static string Sanitize(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var kept = new StringBuilder(content.Length);
        foreach (var character in content)
        {
            switch (character)
            {
                case '\t':
                case '\n':
                case '\r':
                    kept.Append(character);
                    break;

                case < ' ':
                case '\x7F':
                    break; // C0 control (or DEL): dropped, never stored

                case '&':
                    kept.Append("&amp;");
                    break;

                case '<':
                    kept.Append("&lt;");
                    break;

                case '>':
                    kept.Append("&gt;");
                    break;

                case '"':
                    kept.Append("&quot;");
                    break;

                case '\'':
                    kept.Append("&#39;");
                    break;

                default:
                    kept.Append(character);
                    break;
            }
        }

        return kept.ToString();
    }
}
