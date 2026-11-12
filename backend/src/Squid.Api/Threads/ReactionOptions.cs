using Microsoft.Extensions.Configuration;

namespace Squid.Api.Threads;

/// <summary>
/// The instance emoji whitelist of SPEC-0010 section F (GAP-34, resolved by a human: config
/// <c>Reactions:AllowedEmojis</c> with defaults) beside the acceptance itself. Reading it from
/// <c>IConfiguration</c> on every reaction is what makes "changing the config changes the accepted
/// set" true without a restart (AC-02): an emoji outside the set is refused with
/// <c>422 invalid_emoji</c> before anything is written (data-model section 2.14 — rejecting the
/// emoji outside the whitelist is the spam rule). The set is read from the section's array entries
/// (<c>Reactions:AllowedEmojis:0=...</c>), from the comma form one environment variable can carry,
/// or falls back to <see cref="DefaultEmojis"/> when the instance configured nothing — and the
/// Like emoji <see cref="Like"/> (👍) is ALWAYS in the resulting list, the guarantee GAP-34 fixes
/// ("👍 sempre incluído"), so a config can widen or narrow the set but never drop the default
/// reaction.
/// </summary>
public sealed record ReactionOptions(IReadOnlyList<string> AllowedEmojis)
{
    /// <summary>The emoji data-model section 2.14 calls <c>Like</c> — always accepted, always first.</summary>
    public const string Like = "👍";

    /// <summary>
    /// What an instance with no configuration accepts: the default Like plus a small set of
    /// ordinary replies (GAP-34 "com defaults"). Widening or narrowing happens through config only.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultEmojis =
        ["👍", "❤️", "😂", "🎉", "😮", "😢", "😡"];

    /// <summary>Reads the whitelist from configuration; empty config means <see cref="DefaultEmojis"/>.</summary>
    public static ReactionOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("Reactions:AllowedEmojis");
        var allowed = section.GetChildren()
            .Select(child => child.Value?.Trim())
            .OfType<string>() // the null half of a Value is not an emoji of anybody's list
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (allowed.Count == 0)
        {
            // The scalar (comma) form: the single variable an environment can set without array keys.
            allowed = (configuration["Reactions:AllowedEmojis"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (allowed.Count == 0)
        {
            allowed = DefaultEmojis.ToList();
        }

        if (!allowed.Contains(Like, StringComparer.Ordinal))
        {
            allowed.Insert(0, Like); // GAP-34: the Like never leaves the list, whatever the config says
        }

        return new ReactionOptions(allowed);
    }

    /// <summary>Whether <paramref name="emoji"/> is on this instance's list (trim it first — the routes do).</summary>
    public bool Allows(string? emoji) =>
        !string.IsNullOrEmpty(emoji) && AllowedEmojis.Contains(emoji, StringComparer.Ordinal);
}
