namespace Squid.Api.Federation;

/// <summary>
/// The wire seam of an outbound delivery (SPEC-0011 section F, mirror of <c>IRemoteMediaClient</c>:
/// the edge declares it so a test stands in for every socket). The pipeline does the policy
/// re-evaluation, the draft-cavage signing of contract §5 and the state transition; this port only
/// POSTs the signed request and answers whether the peer accepted it. Declared with plain strings
/// instead of the protocol's own types so <c>Squid.Api</c> keeps referencing no adapter (ADR-11).
/// </summary>
public interface IPeerSender
{
    /// <summary>POSTs <paramref name="body"/> to <paramref name="inboxUri"/> with the three signed headers; true on a 2xx. Never throws — a hostile peer is an answer (FR-040).</summary>
    Task<bool> SendAsync(
        string inboxUri, string signature, string? digest, string date, string body,
        CancellationToken cancellationToken);
}
