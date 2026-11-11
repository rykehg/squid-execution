using System.Net;
using System.Text.RegularExpressions;

namespace Squid.Api.Posts;

/// <summary>The three OpenGraph values a link card is built from (SPEC-0009 section G).</summary>
public sealed record OpenGraphData(string Title, string Description, string? Image);

/// <summary>
/// The OpenGraph reader of the unfurl: the three <c>og:*</c> properties out of the fetched
/// HTML, attribute order and quoting agnostic, first occurrence winning, HTML entities decoded
/// back (the page wrote <c>&amp;amp;</c>, the card shows <c>&amp;</c>).
/// </summary>
public static class OpenGraph
{
    private static readonly Regex MetaTag = new(
        @"<meta\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Attribute = new(
        @"(?<key>property|name|content)\s*=\s*(?<quote>""|')(?<value>.*?)\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Extracts og:title, og:description and og:image; missing properties come out empty/null.</summary>
    public static OpenGraphData Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        string? title = null, description = null, image = null;
        foreach (Match tag in MetaTag.Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in Attribute.Matches(tag.Value))
            {
                var key = attribute.Groups["key"].Value;
                if (!attributes.ContainsKey(key)) attributes[key] = attribute.Groups["value"].Value;
            }

            if (!attributes.TryGetValue("property", out var property)
                && !attributes.TryGetValue("name", out property))
            {
                continue;
            }

            if (!attributes.TryGetValue("content", out var content)) continue;

            switch (property.ToLowerInvariant())
            {
                case "og:title" when title is null:
                    title = WebUtility.HtmlDecode(content);
                    break;
                case "og:description" when description is null:
                    description = WebUtility.HtmlDecode(content);
                    break;
                case "og:image" when image is null:
                    image = WebUtility.HtmlDecode(content);
                    break;
            }
        }

        return new OpenGraphData(title ?? string.Empty, description ?? string.Empty, image);
    }
}
