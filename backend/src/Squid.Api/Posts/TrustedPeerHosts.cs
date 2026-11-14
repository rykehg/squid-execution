using System.Net;
using Microsoft.Extensions.Configuration;

namespace Squid.Api.Posts;

/// <summary>
/// The trusted-peer allowlist of SPEC-0011 section C (GAP-49): <c>Federation:TrustedPeerHosts</c>
/// is an EXACT list of hostnames/IPs — scalar with commas (<c>a, b</c>) or the indexed children
/// form (<c>:0</c>, <c>:1</c>) — and is EMPTY by default, so the <see cref="SsrfGuard"/> perimeter
/// answers exactly as before until an operator names a host. Only the three federation clients
/// consult it (actor resolution, media download, peer delivery); the unfurl never asks, so a
/// named list cannot open it. A named host is trusted as it resolves — putting a value here is
/// the human gate the spec demands for production (SPEC-0011 section C).
/// </summary>
public sealed class TrustedPeerHosts
{
    /// <summary>The default: no host named, every federation target judged by the perimeter alone.</summary>
    public static readonly TrustedPeerHosts Empty = new([]);

    private readonly string[] _hosts;

    private TrustedPeerHosts(string[] hosts) => _hosts = hosts;

    /// <summary>Reads <c>Federation:TrustedPeerHosts</c>; absent or empty answers <see cref="Empty"/>.</summary>
    public static TrustedPeerHosts From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Federation:TrustedPeerHosts");
        var raw = string.Join(',', new[] { section.Value }
            .Concat(section.GetChildren().Select(child => child.Value))
            .OfType<string>());
        return new TrustedPeerHosts(raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(StripBrackets)
            .ToArray());
    }

    /// <summary>
    /// True when <paramref name="host"/> IS one entry: hostnames compare case-insensitively (the
    /// DNS spelling), IP literals compare as addresses; no wildcard, no suffix, no port — the
    /// same port does not make another host trusted (SPEC-0011 section C, AC-02).
    /// </summary>
    public bool Contains(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var bare = StripBrackets(host);

        if (IPAddress.TryParse(bare, out var address))
        {
            foreach (var entry in _hosts)
            {
                if (IPAddress.TryParse(StripBrackets(entry), out var other) && other.Equals(address)) return true;
            }

            return false;
        }

        foreach (var entry in _hosts)
        {
            if (string.Equals(entry, bare, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>The bare host behind an IPv6 URI spelling — <c>[::1]</c> and <c>::1</c> are one entry.</summary>
    private static string StripBrackets(string host) =>
        host.Length > 1 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
}
