namespace Squid.Api.Posts;

/// <summary>
/// One <c>Activity</c> of data-model §2.8 — the event every publish appends (SPEC-0009 section A,
/// R-13): the source of the projections of TASK-0024, of the <c>since</c> delta and of the SSE
/// id. <c>Payload</c> is the raw JSON of the object, preserved verbatim for the federation phase;
/// <c>Seq</c> is the GLOBAL sequence of GAP-23 (unlike <c>Object.Seq</c>, which restarts per
/// container).
/// </summary>
/// <param name="Id">Canonical URI of the event — <c>urn:squid:activity:{token}</c>, unique per append.</param>
/// <param name="Type">The data-model §2.8 word: <c>Create</c> for a publish, <c>Delete</c> later.</param>
/// <param name="ActorId">Canonical actor URI of the author.</param>
/// <param name="ObjectId">Canonical URI of the object the event is about.</param>
/// <param name="Seq">Global, monotonic; assigned by the store on append, never by a caller.</param>
/// <param name="Payload">Raw JSON — never re-serialized by a reader (R-13).</param>
/// <param name="CreatedAt">UTC instant of the event.</param>
public sealed record ActivityEvent(
    string Id,
    string Type,
    string ActorId,
    string ObjectId,
    long Seq,
    string Payload,
    DateTimeOffset CreatedAt);

/// <summary>
/// The append-only activity log of SPEC-0009 section A (ADR-35: the edge declares it,
/// <c>Squid.Service</c> implements it). The interface owns the counter's contract, not its home:
/// today <see cref="AppendAsync"/> numbers events from a counter inside its own document, and
/// TASK-0024's migration moves that counter into its own table without any caller changing.
/// </summary>
public interface IActivityStore
{
    /// <summary>
    /// Appends one event at the end of the log and returns it with the global <c>Seq</c>
    /// assigned. Nothing ever rewrites or removes an event (data-model §2.8 invariant).
    /// </summary>
    Task<ActivityEvent> AppendAsync(ActivityEvent activity);

    /// <summary>Every event with <c>Seq &gt; afterSeq</c> in <c>Seq</c> order — the exact delta read of FR-025/R-16.</summary>
    Task<IReadOnlyList<ActivityEvent>> ReadAsync(long afterSeq = 0);
}
