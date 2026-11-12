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
/// One row of <c>message</c> (SPEC-0010 section B, data-model section 2.5.1): the sender, the
/// sanitized <c>Content</c> exactly as stored and <c>Seq</c> — the position the thread's state
/// handed out under its version guard. <c>ReplyToId</c> and the reaction counts are deliberately
/// absent: they arrive with TASK-0037/TASK-0038, not before.
/// </summary>
public sealed record MessageState(
    string Id,
    string ThreadId,
    string AuthorActorId,
    string Content,
    long Seq,
    DateTimeOffset CreatedAt);

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

    /// <summary>
    /// Whether <paramref name="actorId"/> sits in <paramref name="threadId"/>'s participant rows —
    /// the single probe every route of the thread authorizes with (FR-031): a thread the actor is
    /// not in and a thread that does not exist have the SAME answer here, so a caller can be told
    /// 404 without the answer ever revealing which case it was (SC-005).
    /// </summary>
    Task<bool> IsParticipantAsync(string threadId, string actorId);

    /// <summary>
    /// Appends one message (SPEC-0010 section B): the thread's next <c>seq</c> is taken under the
    /// version guard AND the row lands in the SAME transaction (GAP-29), so a number is never
    /// committed without its message — a reader polling <c>?since=</c> can miss neither. Null means
    /// no such thread: nothing was written. A racing append waits for the lock instead of failing,
    /// which is why every sender of a race still gets an answer, with its own number.
    /// </summary>
    Task<MessageState?> AppendMessageAsync(
        string threadId, string messageId, string authorActorId, string content, DateTimeOffset createdAt);

    /// <summary>
    /// The exact delta of contract §9.3: every message of <paramref name="threadId"/> with
    /// <c>seq &gt; since</c>, no duplicates (one row per number) and ordered by <c>seq</c> — the
    /// index <c>(thread_id, seq)</c> of 0003 is what it reads through.
    /// </summary>
    Task<IReadOnlyList<MessageState>> ListMessagesAsync(string threadId, long since);
}
