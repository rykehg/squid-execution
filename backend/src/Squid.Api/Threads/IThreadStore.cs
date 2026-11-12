using Squid.Domain.Objects;

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
/// handed out under its version guard. <c>ReplyToId</c> is FR-057's reference (section E): null
/// when the message answers nothing, and always another message OF THE SAME thread — the append
/// refuses any other target before a row exists. Reaction counts are deliberately NOT a field
/// here: they are the derived projection of <see cref="ReactionCount"/> the read assembles from
/// the rows of 0006 (data-model section 2.14: projection, never source). Attachments join with
/// TASK-0039 (section G): the §2.6 list frozen at publication from the <c>contentHashes</c> of
/// the body — the <c>attachments</c> column of 0007, null when the message carries none.
/// </summary>
public sealed record MessageState(
    string Id,
    string ThreadId,
    string AuthorActorId,
    string Content,
    long Seq,
    DateTimeOffset CreatedAt,
    string? ReplyToId)
{
    /// <summary>
    /// The §2.6 list as it was published (TASK-0039, SPEC-0010 section G): read back from the
    /// JSON column of 0007, so the envelope reports the very metadata the send froze.
    /// </summary>
    public IReadOnlyList<Attachment> Attachments { get; init; } = [];
}

/// <summary>
/// What one append attempt answered (SPEC-0010 sections D and E): the row landed, the §9.6
/// refusal of a sender that never accepted the non-E2EE notice, the §9.9 refusal of a reply
/// target outside the thread, or the nothing an absent thread answers with — four outcomes
/// because the route owes four different statuses (200 / 428 / 422 / 404).
/// </summary>
public enum AppendOutcome
{
    /// <summary>Number taken and row committed in the same transaction (GAP-29).</summary>
    Appended,

    /// <summary>No <c>thread_no_e2ee_ack</c> row for (thread, sender) — the caller answers <c>428 precondition_required</c>.</summary>
    AckRequired,

    /// <summary>
    /// <c>replyToId</c> names no message of THIS thread (SPEC-0010 section E, FR-057, §9.9):
    /// another thread's message and a message that does not exist share this ONE answer, so the
    /// <c>422 invalid_reply_target</c> never tells a participant which case it was. Decided inside
    /// the append AFTER the ack gate (contract §6 makes 428 absolute), nothing written.
    /// </summary>
    InvalidReplyTarget,

    /// <summary>
    /// The <c>contentHashes</c> of section G (SPEC-0010, GAP-35, data-model §2.6): more than
    /// <see cref="Attachment.MaxPerPost"/>, a hash that was never uploaded, or an
    /// <see cref="AttachmentKind.Image"/>/<see cref="AttachmentKind.Video"/>/<see cref="AttachmentKind.Gif"/>
    /// whose <c>Alt</c> is empty — the §2.6 refusal of the PUBLICATION, decided inside the append
    /// AFTER the 428 and the 422 (precondition first), before the sequence is taken: the caller
    /// answers <c>400 validation_failed</c> with <see cref="AppendResult.Reason"/> and nothing was
    /// written. The upload, magic bytes, EXIF and the 507 quota stay in <c>POST /api/media</c>.
    /// </summary>
    InvalidAttachment,

    /// <summary>No such thread: nothing was written, the caller answers the byte-identical 404.</summary>
    NoSuchThread,
}

/// <summary>One append attempt: <see cref="Message"/> is the stored row only when <see cref="Outcome"/> is <see cref="AppendOutcome.Appended"/>.</summary>
public sealed record AppendResult(AppendOutcome Outcome, MessageState? Message)
{
    /// <summary>
    /// Why the append refused (TASK-0039): the house sentence of the <c>400</c> the route writes
    /// for <see cref="AppendOutcome.InvalidAttachment"/> — null on every other outcome.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>The vocabulary of data-model section 2.14 <c>Kind</c> on a reaction row (local only — a message reaction never federates).</summary>
public static class ReactionKinds
{
    /// <summary>The default 👍 — the kind section 2.14 says federates when the day comes.</summary>
    public const string Like = "like";

    /// <summary>Every other whitelisted emoji — the extension path, no federation distinction at this phase.</summary>
    public const string Custom = "custom";
}

/// <summary>
/// What one reaction attempt answered (SPEC-0010 section F, data-model section 2.14): the row
/// landed, the SAME (message, actor, emoji) was already there so nothing changed (AC-01's
/// idempotence), the actor's own row left, or the target was not there — no message of this thread
/// for a POST, no row of this actor for a DELETE —, which the caller answers with the ONE
/// byte-identical 404 so neither case ever tells a participant which one it was (SC-005).
/// </summary>
public enum ReactionOutcome
{
    /// <summary>Row inserted (one row per (message, actor, emoji) — the primary key of 0006).</summary>
    Created,

    /// <summary>The triple already existed: no second row, no count change, nothing written.</summary>
    Repeated,

    /// <summary>The actor's own row was deleted.</summary>
    Removed,

    /// <summary>No message of this thread, or no row to remove: the caller answers the byte-identical 404.</summary>
    Missing,
}

/// <summary>
/// One entry of the derived projection the read envelope carries (contract §6):
/// <c>{emoji, count, mine}</c> — <c>count</c> is <c>COUNT(*)</c> of the rows that exist right now
/// and <c>mine</c> says whether the READER holds one, so the number is reconciled at read time and
/// is never a stored counter (data-model section 2.14: projection, never source).
/// </summary>
public sealed record ReactionCount(string MessageId, string Emoji, long Count, bool Mine);

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
    /// with nothing written, which is what makes the 428 hold for EVERY path that sends. Section
    /// E rides the same attempt: when <paramref name="replyToId"/> arrives it is checked against
    /// the thread's own messages AFTER the gate (contract §6 makes 428 absolute, so a send missing
    /// both answers 428) and a target from another thread or a target that does not exist is
    /// <see cref="AppendOutcome.InvalidReplyTarget"/> — one answer for both, nothing written, no
    /// sequence taken; a target of this thread is stored as the row's <c>reply_to_id</c>. Absent
    /// thread is decided first (<see cref="AppendOutcome.NoSuchThread"/>), so a foreign thread
    /// still shares the byte-identical 404 whether or not the caller ever acked (SC-005). A
    /// racing append waits for the lock instead of failing, which is why every sender of a race
    /// still gets an answer, with its own number. Section G (TASK-0039) rides the SAME attempt:
    /// <paramref name="contentHashes"/> is checked inside the transaction AFTER the 428 and the
    /// 422 (precondition first) — at most 10, every hash an upload that exists, and the §2.6 Alt
    /// rule on image/video/gif — and a refusal is <see cref="AppendOutcome.InvalidAttachment"/>
    /// with <see cref="AppendResult.Reason"/>, no sequence taken and no row written; when the list
    /// stands, the <see cref="Attachment"/> list of §2.6 is built with <paramref name="origin"/>'s
    /// media URL and stored in the row's <c>attachments</c> column (0007). Nothing else of the
    /// pipeline moves: upload, magic bytes, EXIF and the 507 quota stay in <c>POST /api/media</c>.
    /// </summary>
    Task<AppendResult> AppendMessageAsync(
        string threadId, string messageId, string authorActorId, string content, DateTimeOffset createdAt,
        string? replyToId = null, IReadOnlyList<string>? contentHashes = null, string? origin = null);

    /// <summary>
    /// The exact delta of contract §9.3: every message of <paramref name="threadId"/> with
    /// <c>seq &gt; since</c>, no duplicates (one row per number) and ordered by <c>seq</c> — the
    /// index <c>(thread_id, seq)</c> of 0003 is what it reads through.
    /// </summary>
    Task<IReadOnlyList<MessageState>> ListMessagesAsync(string threadId, long since);

    /// <summary>
    /// The single probe the reaction routes authorize with (SPEC-0010 section F, FR-031 + §9.10):
    /// true only when <paramref name="threadId"/> exists, <paramref name="actorId"/> sits in its
    /// participant rows AND <paramref name="messageId"/> is a message OF THAT thread — the three
    /// misses return the same false, which is what lets a stranger, an absent thread and a message
    /// of another thread share ONE byte-identical 404 (SC-005) without ever telling which case it
    /// was. Reactions and counts live only behind this wall.
    /// </summary>
    Task<bool> MessageVisibleAsync(string threadId, string messageId, string actorId);

    /// <summary>
    /// The GAP-25 ampliation of SPEC-0010 section G (TASK-0039, AC-04): true only when
    /// <paramref name="contentHash"/> is referenced by a message of a thread
    /// <paramref name="actorId"/> sits in — the participant sees the media their conversation
    /// carries, and every other reader (another thread's participant included) reads false, so
    /// <c>GET /api/media/{contentHash}</c> keeps answering them the byte-identical 404 of SC-005.
    /// The post half of the rule (GAP-25's original) is untouched and decided by the caller.
    /// </summary>
    Task<bool> MessageMediaVisibleAsync(string contentHash, string actorId);

    /// <summary>
    /// Writes one reaction row (section F, data-model section 2.14): an insert that
    /// conflict-does-nothing, inside a transaction that FIRST re-checks the message belongs to the
    /// thread (the defensive twin of the probe above), so repeating the same (message, actor,
    /// emoji) answers <see cref="ReactionOutcome.Repeated"/> with no second row and no count change
    /// (AC-01's idempotence) and a target that vanished answers
    /// <see cref="ReactionOutcome.Missing"/> with nothing written. The emoji whitelist itself is
    /// configuration (GAP-34) and is checked at the edge, before this call.
    /// </summary>
    Task<ReactionOutcome> AddReactionAsync(
        string threadId, string messageId, string actorId, string emoji, DateTimeOffset createdAt);

    /// <summary>
    /// Deletes the ACTOR's own row for <paramref name="emoji"/> (section F): rows are deleted,
    /// never decremented, so the count a reader sees stays a projection of what exists. A message
    /// that belongs to another thread, a row the actor never wrote and an absent thread all answer
    /// <see cref="ReactionOutcome.Missing"/> with nothing touched — the caller maps that one word
    /// to the byte-identical 404 (AC-03: removing a reaction that does not exist is 404).
    /// </summary>
    Task<ReactionOutcome> RemoveReactionAsync(
        string threadId, string messageId, string actorId, string emoji);

    /// <summary>
    /// Every reaction of the delta <c>seq &gt; since</c>, grouped per (message, emoji) with
    /// <c>COUNT(*)</c> and whether <paramref name="readerActorId"/> holds one of those rows — the
    /// contract §6 <c>{emoji, count, mine}</c> items, read from the rows on EVERY call and never
    /// from a counter (section F: projection reconciled, never source — AC-03).
    /// </summary>
    Task<IReadOnlyList<ReactionCount>> ListReactionsAsync(string threadId, long since, string readerActorId);

    /// <summary>
    /// The same projection for ONE message: the answer <c>POST/DELETE .../reactions</c> return
    /// right after the effect, so the caller reports the reconciled count of the row it just wrote
    /// or removed (AC-01's unchanged count, AC-03's derived count).
    /// </summary>
    Task<IReadOnlyList<ReactionCount>> ReactionsOfAsync(
        string threadId, string messageId, string readerActorId);
}
