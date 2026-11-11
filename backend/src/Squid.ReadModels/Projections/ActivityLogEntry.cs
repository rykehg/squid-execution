namespace Squid.ReadModels.Projections;

/// <summary>
/// One event of the append-only activity log as the read side sees it — the stored shape of the
/// edge's <c>ActivityEvent</c>, which this layer may not reference (storage depends on contracts,
/// never on the HTTP edge, Directory.Build.targets). The property names and order are the
/// document's own, so the persisted JSON deserializes without a mapper and <c>Payload</c> arrives
/// as the raw post JSON it was written with (R-13: never re-serialized). <c>Seq</c> is the GLOBAL
/// sequence of GAP-23 — the numbering the projection tables carry and the SSE id will use.
/// </summary>
/// <param name="Id">Canonical URI of the event: <c>urn:squid:activity:{token}</c>, unique per append.</param>
/// <param name="Type">The data-model §2.8 word: <c>Create</c> for a publish.</param>
/// <param name="ActorId">Canonical actor URI of the author.</param>
/// <param name="ObjectId">Canonical URI of the object the event is about.</param>
/// <param name="Seq">Global sequence assigned by the store's SQL counter on append.</param>
/// <param name="Payload">Raw JSON of the summary — parsed, never round-tripped.</param>
/// <param name="CreatedAt">UTC instant of the event.</param>
public sealed record ActivityLogEntry(
    string Id,
    string Type,
    string ActorId,
    string ObjectId,
    long Seq,
    string Payload,
    DateTimeOffset CreatedAt);

/// <summary>
/// The one stored document of the log (<c>GrainState</c> row of the store named <c>activities</c>):
/// the last number beside the list. Its shape is owned by <c>Squid.Service.SqliteActivityStore</c>,
/// which writes it; this mirror exists so the read side can read the same row without referencing
/// the composition root that implements the store.
/// </summary>
public sealed record ActivityLogDocument(long LastSeq, List<ActivityLogEntry> Events);
