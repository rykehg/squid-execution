using Squid.Domain.Containers;
using Squid.Domain.Objects;

namespace Squid.Api.Federation;

/// <summary>
/// The outbound half of SPEC-0011 section F (contract §4, FR-037/FR-038): the fan-out a local
/// publication and a local removal enqueue, the follow the local route initiates (GAP-41), and
/// the single send with its R-05 re-evaluation. Declared here, on the edge (ADR-35), implemented
/// in <c>Squid.Service</c> — the only layer that holds the stores, the JSON-LD adapter and the
/// actor signer at once. Enqueueing is a local write that finishes BEFORE the publish answer
/// leaves (AC-05); sending is never on the request path (FR-036).
/// </summary>
public interface IOutboundPipeline
{
    /// <summary>
    /// The fan-out of a publication (AC-01/AC-02): recipients by visibility — remote followers of
    /// the author, the container's own Active members (its followers collection, contract §4),
    /// nobody at all for <c>private</c> — one <c>Pending</c> row each, carrying the <c>Create</c>
    /// with <c>to</c>/<c>cc</c> of §2.1 and the named destination <c>audience</c> + <c>squid:slug</c>
    /// (AC-01, GAP-48).
    /// </summary>
    Task EnqueueCreateAsync(string origin, PostObject post, Container container);

    /// <summary>
    /// The propagation of a removal (AC-06, R-08): a signed <c>Delete</c> enqueued for every
    /// recipient that was to receive the <c>Create</c> and whose delivery was not already
    /// policy-cancelled — a recipient nothing ever reached owes nothing.
    /// </summary>
    Task EnqueueDeleteAsync(string origin, PostObject post, Container container);

    /// <summary>
    /// The local follow route (AC-03, GAP-41): a <c>Follow</c> toward the remote target enqueued
    /// for the peer and the outbound edge recorded as pending — settled when the Accept arrives.
    /// </summary>
    Task EnqueueFollowAsync(string origin, string handle, string targetActorUri);

    /// <summary>
    /// One delivery goes out (AC-04): R-05 recomputes the decision with the policy in force NOW
    /// — denied means <see cref="PeerDeliveryState.Cancelled"/> with the audit reason and the
    /// sender is never touched, no error and no retry (FR-038); allowed means the draft-cavage
    /// signature of §5 and the POST to the recipient's inbox.
    /// </summary>
    Task<SendVerdict> SendAsync(PeerDelivery delivery, CancellationToken cancellationToken = default);
}

/// <summary>What <c>SendAsync</c> did with one row: the state it reached, plus the reason that state carries.</summary>
public sealed record SendVerdict(PeerDeliveryState State, string? Reason);
