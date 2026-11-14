namespace Squid.Api.Federation;

/// <summary>Which outbound activity a queue row carries — the outbound half of GAP-43's set (SPEC-0011 section F).</summary>
public enum PeerActivity
{
    Create,
    Follow,
    Delete,
}

/// <summary>The delivery machine of data-model section 3.4: <c>Cancelled</c> only ever means policy (R-05).</summary>
public enum PeerDeliveryState
{
    Pending,
    Sent,
    Failed,
    Cancelled,
}

/// <summary>
/// One row of the outbound queue (data-model §2.9, SPEC-0011 section F): the pair
/// (<see cref="ActivityId"/>, <see cref="RecipientActorId"/>) IS the identity — a repeated
/// enqueue of the same delivery lands nowhere, which is how a re-run of the fan-out stays
/// idempotent. <see cref="Payload"/> is the JSON-LD frozen at enqueue (R-13: raw, signed only
/// when it goes out) and <see cref="PolicyVersionAtEnqueue"/> is what the send-time
/// re-evaluation compares the policy in force against (R-05, FR-038).
/// </summary>
/// <param name="ActivityId">The canonical activity URL this row delivers (<c>{origin}/activities/{token}</c>).</param>
/// <param name="RecipientActorId">The remote actor (or collection) the activity is addressed to.</param>
/// <param name="Activity">Which of the three outbound activities the payload is.</param>
/// <param name="ObjectId">The object the activity carries — the post for Create/Delete, the target actor for Follow.</param>
/// <param name="Payload">The activity document exactly as it will be POSTed.</param>
/// <param name="PolicyVersionAtEnqueue">Container policy version when the row was enqueued; 0 for Follow, which has no container.</param>
/// <param name="Attempts">How many sends were attempted — the motor of TASK-0046 grows it.</param>
/// <param name="NextAttemptAt">When the next attempt may run; born at enqueue, owned by TASK-0046 afterwards.</param>
/// <param name="State">Pending | Sent | Failed | Cancelled (§3.4).</param>
/// <param name="Reason">The audit word a terminal (or failed) row carries; null while healthy.</param>
public sealed record PeerDelivery(
    string ActivityId,
    string RecipientActorId,
    PeerActivity Activity,
    string ObjectId,
    string Payload,
    int PolicyVersionAtEnqueue,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    PeerDeliveryState State,
    string? Reason);

/// <summary>
/// The outbound queue of SPEC-0011 section F (ADR-35: declared on the edge, implemented in
/// <c>Squid.Service</c> over <c>0008_federation.sql</c>). Deliveries are born here and stay
/// observable — the local publish registers them as <c>Pending</c> and never waits on a peer
/// (FR-036); the motor that drains them is TASK-0046's.
/// </summary>
public interface IPeerDeliveryStore
{
    /// <summary>Registers one delivery; the same (activity, recipient) is one row, however often the fan-out runs.</summary>
    Task AddAsync(PeerDelivery delivery);

    /// <summary>One delivery by its pair, or null when it was never enqueued.</summary>
    Task<PeerDelivery?> FindAsync(string activityId, string recipientActorId);

    /// <summary>Every delivery of <paramref name="objectId"/> carrying <paramref name="activity"/> — how the Delete fan-out finds the Create's recipients (R-08).</summary>
    Task<IReadOnlyList<PeerDelivery>> ForObjectAsync(string objectId, PeerActivity activity);

    /// <summary>Every delivery addressed to one recipient, all activity kinds — the observable slice a test (or an operator) reads.</summary>
    Task<IReadOnlyList<PeerDelivery>> ForRecipientAsync(string recipientActorId);

    /// <summary>Persists state, reason, attempts and next attempt of an existing row; false when the pair is gone.</summary>
    Task<bool> SaveAsync(PeerDelivery delivery);
}
