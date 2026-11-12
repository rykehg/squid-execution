namespace Squid.Api.Threads;

/// <summary>
/// One thread row of SPEC-0010 section A (data-model section 2.7 + emenda GAP-30) beside the
/// store's own state version — the <c>version</c> column of <c>0002_threads.sql</c>, the ETag
/// every write guards with. <c>MessageSeq</c> lives here under that guard (GAP-29: the total
/// order of FR-030 is serialized in SQLite, never in a grain).
/// </summary>
public sealed record ThreadState(
    string Id,
    string Kind,
    string CreatedBy,
    long MessageSeq,
    DateTimeOffset CreatedAt,
    int Version);

/// <summary>
/// One row of <c>thread_participant</c>: <c>Role</c> is <c>admin</c> or <c>member</c> — GAP-30's
/// amendment puts the admin seat on the creator of a Group, one role per participant.
/// </summary>
public sealed record ThreadParticipant(string ThreadId, string ActorId, string Role, DateTimeOffset JoinedAt);

/// <summary>
/// One row of <c>GET /api/threads</c>: the thread, every participant (data-model section 2.7's
/// <c>ParticipantActorIds</c>) and the reader's own <c>lastReadSeq</c> — born 0 per participant
/// (GAP-38; the monotonic marker itself is TASK-0036, the field exists from here).
/// </summary>
public sealed record ThreadListItem(
    ThreadState Thread,
    IReadOnlyList<string> ParticipantActorIds,
    long LastReadSeq);

/// <summary>
/// The thread store of SPEC-0010 section A (ADR-35: the edge declares it, <c>Squid.Service</c>
/// implements it over SQLite). Writes are insert-only or compare-and-swap on the version column:
/// a divergent version is refused, never overwritten — the optimistic-concurrency rule TASK-0003
/// proved, now carrying the message sequence of GAP-29.
/// </summary>
public interface IThreadStore
{
    /// <summary>
    /// Inserts the thread at state version 1 with all its participant rows — one transaction, so
    /// a thread is never visible half-joined. Insert only: an id that already exists is refused,
    /// never rewritten.
    /// </summary>
    Task CreateAsync(ThreadState thread, IReadOnlyList<ThreadParticipant> participants);

    /// <summary>
    /// The threads where <paramref name="actorId"/> is a participant, in creation order, each
    /// with all its participants and the actor's own <c>lastReadSeq</c> (0 while the marker
    /// belongs to TASK-0036). Threads the actor is not in never appear here (FR-031).
    /// </summary>
    Task<IReadOnlyList<ThreadListItem>> ListForAsync(string actorId);

    /// <summary>
    /// The next message sequence (GAP-29): <c>message_seq</c> and <c>version</c> move together in
    /// one atomic update that only lands while <paramref name="expectedVersion"/> still matches
    /// the stored one — two concurrent senders never receive the same number, and a stale
    /// expectation moves nothing. Null means stale version or absent thread; nothing was written.
    /// </summary>
    Task<long?> NextMessageSeqAsync(string id, int expectedVersion);
}
