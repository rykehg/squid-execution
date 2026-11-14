namespace Squid.Api.Federation;

/// <summary>One federated follow edge: the remote actor on one end, and whether the local side is settled.</summary>
/// <param name="ActorUri">The other end's actor URI — a remote actor following us, or a remote actor we follow.</param>
/// <param name="Established">
/// <c>true</c> for a follower (their Follow is in force the moment we answer Accept) and for an
/// outgoing follow the peer already accepted; <c>false</c> is the pending state of a Follow this
/// instance has sent and the peer has not answered yet (TASK-0045's route writes it).
/// </param>
/// <param name="CreatedAt">When the edge was first written.</param>
public sealed record FollowEdge(string ActorUri, bool Established, DateTimeOffset CreatedAt);

/// <summary>
/// The follow relation of SPEC-0011 section E (AC-05 of TASK-0044): follower rows inbound,
/// following rows outbound, both addressed by the LOCAL handle they hang on. ADR-35 — the edge
/// declares it, <c>Squid.Service</c> implements it as keyed documents over the same SQLite file
/// (pattern <c>SqliteActorStatusStore</c>): no table and no migration inside this task, so the
/// <c>follow</c> table of <c>0008_federation.sql</c> stays TASK-0046's to create and this interface
/// is the one TASK-0045 adopts when it starts sending Follows.
/// </summary>
public interface IFollowStore
{
    /// <summary>The actors following <paramref name="handle"/>, in the order the rows arrived.</summary>
    Task<IReadOnlyList<FollowEdge>> FollowersAsync(string handle);

    /// <summary>The actors <paramref name="handle"/> follows and the peer accepted — pending edges never federate.</summary>
    Task<IReadOnlyList<FollowEdge>> FollowingAsync(string handle);

    /// <summary>Records an inbound follow (idempotent: the same pair is one row, however often it arrives).</summary>
    Task AddFollowerAsync(string handle, string actorUri, DateTimeOffset now);

    /// <summary>Removes an inbound follow (Undo); false when there was none to remove.</summary>
    Task<bool> RemoveFollowerAsync(string handle, string actorUri);

    /// <summary>Records an outbound follow awaiting its Accept — the row TASK-0045's route writes.</summary>
    Task AddFollowingAsync(string handle, string actorUri, DateTimeOffset now);

    /// <summary>Settles an outbound follow when its Accept arrives; false when no such follow was ever sent.</summary>
    Task<bool> EstablishAsync(string handle, string actorUri);

    /// <summary>Removes an outbound follow (our own Undo); false when there was none to remove.</summary>
    Task<bool> RemoveFollowingAsync(string handle, string actorUri);
}
