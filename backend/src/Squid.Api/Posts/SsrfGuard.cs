using System.Net;
using System.Net.Sockets;

namespace Squid.Api.Posts;

/// <summary>
/// The SSRF perimeter of the link unfurl (SPEC-0009 section G, GAP-26): a fetched address may
/// not be loopback, private (RFC 1918 / unique-local), link-local or CGNAT (RFC 6598), in any
/// of its forms — an IPv4-mapped or IPv4-compatible IPv6 spelling is judged as the IPv4 it
/// carries, and an address family the rule does not know is refused rather than let through.
/// </summary>
public static class SsrfGuard
{
    /// <summary>True when <paramref name="address"/> sits inside the perimeter and must never be fetched.</summary>
    public static bool Blocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // ::ffff:127.0.0.1 and the deprecated ::127.0.0.1 spelling are loopback wearing IPv6.
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        else if (address.AddressFamily == AddressFamily.InterNetworkV6 && IsIPv4Compatible(address))
        {
            address = new IPAddress(address.GetAddressBytes().AsSpan(12, 4));
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return IPAddress.IsLoopback(address)            // 127.0.0.0/8
                || bytes[0] == 169 && bytes[1] == 254       // 169.254.0.0/16
                || bytes[0] is >= 224 and <= 239            // 224.0.0.0/4 multicast
                || bytes[0] == 0                            // 0.0.0.0/8 — "this network" reaches localhost in practice
                || bytes[0] == 10                           // 10.0.0.0/8
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31   // 172.16.0.0/12
                || bytes[0] == 192 && bytes[1] == 168       // 192.168.0.0/16
                || bytes[0] == 100 && (bytes[1] & 0xC0) == 64;      // 100.64.0.0/10 (CGNAT)
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.Equals(IPAddress.IPv6Loopback)   // ::1
                || address.Equals(IPAddress.IPv6Any)        // ::
                || address.IsIPv6LinkLocal                  // fe80::/10
                || address.IsIPv6SiteLocal                  // fec0::/10 (deprecated site-local)
                || address.IsIPv6Multicast                  // ff00::/8
                || (bytes[0] & 0xFE) == 0xFC;               // fc00::/7 unique-local (fc00..fdff)
        }

        return true; // a family the perimeter does not know is refused, never fetched
    }

    /// <summary>True when every answer of a resolver round is inside the perimeter.</summary>
    public static bool Allowed(IReadOnlyList<IPAddress> addresses) =>
        addresses.Count > 0 && addresses.All(address => !Blocked(address));

    /// <summary>The deprecated IPv4-compatible form <c>::a.b.c.d</c> (first 96 bits zero).</summary>
    private static bool IsIPv4Compatible(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0
            && bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0
            && bytes[8] == 0 && bytes[9] == 0 && bytes[10] == 0 && bytes[11] == 0;
    }
}
