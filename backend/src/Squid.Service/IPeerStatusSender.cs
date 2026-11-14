namespace Squid.Service;

/// <summary>
/// What the wire answered for one delivery: <see cref="Accepted"/> on a 2xx, and
/// <see cref="StatusCode"/> when an HTTP answer arrived at all (null = unreachable, timeout or a
/// transport error). This is the richer half of the edge seam <c>Squid.Api</c>'s
/// <see cref="Squid.Api.Federation.IPeerSender"/> carries: a plain bool cannot tell AC-02's
/// <c>4xx permanente</c> — permanent refusal, straight to the DLQ — from a 500 worth retrying, so
/// the pipeline prefers this port when the registered sender also implements it (both
/// <see cref="HttpPeerSender"/> and a test's stand-in do) and falls back to the bool otherwise.
/// </summary>
public sealed record PeerSendAnswer(bool Accepted, int? StatusCode);

/// <summary>The status-reporting twin of the send seam, declared beside its production adapter (the <c>IFederatedActorClient</c> precedent).</summary>
public interface IPeerStatusSender
{
    /// <summary>Same POST as the bool port, answering with the status code the peer returned. Never throws (FR-040).</summary>
    Task<PeerSendAnswer> SendWithStatusAsync(
        string inboxUri, string signature, string? digest, string date, string body,
        CancellationToken cancellationToken);
}
