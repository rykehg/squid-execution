using System.Net;

namespace Squid.Api.Posts;

/// <summary>
/// Every A/AAAA answer behind a host name (SPEC-0009 section G, GAP-26): the SSRF guard
/// validates ALL of them before a fetch, so one bad record among good ones refuses the hop.
/// The seam exists so the gate's tests substitute it — no test may resolve a real name (AC-05).
/// </summary>
public interface IUnfurlResolver
{
    /// <summary>The addresses of <paramref name="host"/>, or an empty list when nothing answers.</summary>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}
