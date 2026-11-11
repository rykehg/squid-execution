using Microsoft.Extensions.Configuration;

namespace Squid.Api.Posts;

/// <summary>
/// The configuration of the link unfurl (SPEC-0009 section G, GAP-26): <c>Unfurl:TimeoutMs</c>
/// bounds the WHOLE synchronous fetch — every hop of the redirect chain shares one budget — and
/// <c>Unfurl:MaxBytes</c> caps how much of the response body may be read. Defaults are the 3 s
/// and 1 MB the spec fixes; both are configurable so a test can run them small.
/// </summary>
public sealed record UnfurlOptions(int TimeoutMs, long MaxBytes)
{
    /// <summary>The spec's synchronous fetch budget: 3000 ms.</summary>
    public const int DefaultTimeoutMs = 3000;

    /// <summary>The spec's response cap: 1 MB (1 048 576 bytes).</summary>
    public const long DefaultMaxBytes = 1024 * 1024;

    /// <summary>Reads the options from configuration, falling back to the defaults above.</summary>
    public static UnfurlOptions From(IConfiguration configuration) => new(
        Positive(configuration["Unfurl:TimeoutMs"], DefaultTimeoutMs),
        Positive(configuration["Unfurl:MaxBytes"], DefaultMaxBytes));

    private static int Positive(string? raw, int fallback) =>
        int.TryParse(raw, out var value) && value > 0 ? value : fallback;

    private static long Positive(string? raw, long fallback) =>
        long.TryParse(raw, out var value) && value > 0 ? value : fallback;
}
