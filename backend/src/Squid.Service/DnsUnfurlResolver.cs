using System.Net;
using Squid.Api.Posts;

namespace Squid.Service;

/// <summary>
/// The production <see cref="IUnfurlResolver"/>: <c>Dns.GetHostAddressesAsync</c> returns every
/// A/AAAA answer of the name at once, which is exactly the set the SSRF guard validates before
/// a fetch (SPEC-0009 section G, GAP-26). The composition root registers it; the gate's tests
/// swap the seam so no test resolves a real name (AC-05).
/// </summary>
public sealed class DnsUnfurlResolver : IUnfurlResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}
