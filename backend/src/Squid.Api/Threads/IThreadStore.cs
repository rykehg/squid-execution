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
/// The vocabulary of a <c>thread_participant.role</c> row (GAP-30): one seat per participant, and
/// the only word the store's succession reads — shared so <c>admin</c> is never spelled twice.
/// </summary>
public static class ThreadRoles
{
    public const string Admin = "admin";

    public const string Member = "member";
}

/// <summary>
/// A thread as the participant routes read it (SPEC-0010 section C): the row of data-model
/// section 2.7 — kind and <c>CreatedBy</c> carry the Direct/Group rule and the creator's protected
/// seat — beside every participant row with its role and <c>joined_at</c>, in join order.
/// </summary>
public sealed record ThreadSnapshot(
    ThreadState Thread,
    IReadOnlyList<ThreadParticipant> Participants);

/// <summary>What a removal answered: the row left, the seat was not there, or nobody may leave.</summary>
public enum ParticipantRemoval
{
    Removed,

    /// <summary>No such seat (or no thread): the caller answers the byte-identical 404.</summary>
    NoSuchParticipant,

    /// <summary>The thread would be left with nobody — the <c>409 last_participant</c> of GAP-38.</summary>
    LastParticipant,
}

/// <summary>
/// One row of <c>GET /api/threads</c>: the thread, every participant (data-model section 2.7's
/// <c>ParticipantActorIds</c>) and the READER's own <c>lastReadSeq</c> — the monotonic marker of
/// FR-033/GAP-38, one value per participant (a seat with no marker yet is born 0), never another
/// participant's (SPEC-0010 section D: read state is per participant, visible only to participants).
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
/// What one append attempt answered (SPEC-0010 section D): the row landed, the §9.6 refusal of a
/// sender that never accepted the non-E2EE notice, or the nothing an absent thread answers with —
/// three outcomes because the route owes three different statuses (200 / 428 / 404).
/// </summary>
public enum AppendOutcome
{
    /// <summary>Number taken and row committed in the same transaction (GAP-29).</summary>
    Appended,

    /// <summary>No <c>thread_no_e2ee_ack</c> row for (thread, sender) — the caller answers <c>428 precondition_required</c>.</summary>
    AckRequired,

    /// <summary>No such thread: nothing was written, the caller answers the byte-identical 404.</summary>
    NoSuchThread,
}

/// <summary>One append attempt: <see cref="Message"/> is the stored row only when <see cref="Outcome"/> is <see cref="AppendOutcome.Appended"/>.</summary>
public sealed record AppendResult(AppendOutcome Outcome, MessageState? Message);

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
    /// with all its participants and the actor's own <c>lastReadSeq</c> read from the marker of
    /// 0004 (born 0 until the actor marks one). Threads the actor is not in never appear here
    /// (FR-031).
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
    /// One thread with every participant row (SPEC-0010 section C): the read the participant and
    /// admin routes decide from — kind, the protected <c>CreatedBy</c> seat and each role. Null
    /// means no such thread, the same nothing the FR-031 probe answers with.
    /// </summary>
    Task<ThreadSnapshot?> FindAsync(string threadId);

    /// <summary>
    /// Adds one participant row as <paramref name="participant"/>'s own role (emenda GAP-31):
    /// insert only, and an actor already in the thread is left untouched rather than refused —
    /// one row per (thread, actor) is the primary key of 0002.
    /// </summary>
    Task AddParticipantAsync(ThreadParticipant participant);

    /// <summary>
    /// Removes one participant (SPEC-0010 section C) and carries GAP-32's succession in the SAME
    /// transaction: when the removed seat is the creator, the admin with the smallest
    /// <c>joined_at</c> assumes the creator role, and with no admin left the oldest member is
    /// promoted to admin and becomes creator — a Group with at least one participant always keeps
    /// at least one admin. The last seat never leaves (<see cref="ParticipantRemoval.LastParticipant"/>)
    /// and every refusal writes nothing; history rows are never touched.
    /// </summary>
    Task<ParticipantRemoval> RemoveParticipantAsync(string threadId, string actorId);

    /// <summary>
    /// Writes <paramref name="role"/> onto one seat (emenda GAP-31: <c>POST/DELETE .../admin</c>) —
    /// false when the seat is not there and nothing was written, so the caller can answer the 404.
    /// </summary>
    Task<bool> SetRoleAsync(string threadId, string actorId, string role);

    /// <summary>
    /// Records the acceptance of the non-E2EE notice (SPEC-0010 section D, FR-032, GAP-37): one
    /// row per (thread, actor), so the route is idempotent — repeating it keeps the FIRST ack and
    /// changes nothing. The notice text the actor accepted travels in the row itself (section D:
    /// "o aviso em si é texto registrado no registro do aceite"); showing it in the UI is P-08.
    /// </summary>
    Task AckNoE2eeAsync(string threadId, string actorId, string notice, DateTimeOffset ackedAt);

    /// <summary>
    /// Marks the actor's read marker up to <paramref name="seq"/> (SPEC-0010 section D, FR-033):
    /// the marker only ever MOVES FORWARD — a smaller value is written nowhere and the stored
    /// value comes back unchanged (GAP-38) —, and the value now in force is returned so the
    /// answer tells the truth even when the request was a no-op. The route bounds
    /// <paramref name="seq"/> against the thread total before calling (400 above it).
    /// </summary>
    Task<long> MarkReadAsync(string threadId, string actorId, long seq);

    /// <summary>
    /// The actor's stored read marker for one thread (FR-033), 0 when the actor never marked one.
    /// The caller has already probed participation, so this value only ever leaves through a
    /// participant's own read (SPEC-0010 section D: visible only to participants).
    /// </summary>
    Task<long> LastReadSeqAsync(string threadId, string actorId);

    /// <summary>
    /// Appends one message (SPEC-0010 section B): the thread's next <c>seq</c> is taken under the
    /// version guard AND the row lands in the SAME transaction (GAP-29), so a number is never
    /// committed without its message — a reader polling <c>?since=</c> can miss neither. The
    /// §9.6 gate lives HERE, inside the same transaction and before any number is taken: a sender
    /// with no ack row for (thread, actor) is answered <see cref="AppendOutcome.AckRequired"/>
    /// with nothing written, which is what makes the 428 hold for EVERY path that sends. Absent
    /// thread is decided first (<see cref="AppendOutcome.NoSuchThread"/>), so a foreign thread
    /// still shares the byte-identical 404 whether or not the caller ever acked (SC-005). A
    /// racing append waits for the lock instead of failing, which is why every sender of a race
    /// still gets an answer, with its own number.
    /// </summary>
    Task<AppendResult> AppendMessageAsync(
        string threadId, string messageId, string authorActorId, string content, DateTimeOffset createdAt);

    /// <summary>
    /// The exact delta of contract §9.3: every message of <paramref name="threadId"/> with
    /// <c>seq &gt; since</c>, no duplicates (one row per number) and ordered by <c>seq</c> — the
    /// index <c>(thread_id, seq)</c> of 0003 is what it reads through.
    /// </summary>
    Task<IReadOnlyList<MessageState>> ListMessagesAsync(string threadId, long since);
}
