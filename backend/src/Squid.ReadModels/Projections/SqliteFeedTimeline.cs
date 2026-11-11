using Dapper;
using Microsoft.Data.Sqlite;

namespace Squid.ReadModels.Projections;

/// <summary>
/// One summary line of the feed projections (data-model §4): everything <c>GET feed</c> and
/// <c>GET /api/timeline</c> serve. It carries BOTH sequences — <see cref="Seq"/> is the global
/// Activity.Seq (GAP-23) that every <c>?since=</c> cursor speaks and both projection tables are
/// indexed by; <see cref="ObjectSeq"/> is the per-container Object.Seq of data-model §2.5 the
/// summary keeps for display.
/// </summary>
// Depth and PolicyVersion are long because SQLite reports every INTEGER column as Int64 and
// Dapper matches this constructor against exactly those types (int here = materialization error).
public sealed record FeedTimelineRow(
    string ContainerId,
    long Seq,
    long ObjectSeq,
    string PostId,
    string Author,
    string Content,
    long Depth,
    string? ReplyToId,
    long LikeCount,
    long ReplyCount,
    long ShareCount,
    string Visibility,
    long PolicyVersion,
    string CreatedAt);

/// <summary>
/// The Dapper read side of SPEC-0009 section C: the windows over <c>timeline_item</c> and
/// <c>home_timeline</c> behind <c>GET /api/containers/{slug}/feed?since=</c> and
/// <c>GET /api/timeline?since=</c>. Same file, same indexes as the projector (the seq index of
/// data-model §4 — a delta walks the index, never the history); read only, never on the write
/// path (R-18). Visibility is NOT decided here: every row comes back with its container id so the
/// route applies the CURRENT policy and membership (FR-026) before anything reaches the wire.
/// </summary>
public sealed class SqliteFeedTimeline
{
    /// <summary>The summary columns, aliased so Dapper's plain name matching reads them (no global config).</summary>
    private const string Columns =
        "container_id AS ContainerId, seq AS Seq, object_seq AS ObjectSeq, post_id AS PostId, " +
        "author AS Author, content AS Content, depth AS Depth, reply_to_id AS ReplyToId, " +
        "like_count AS LikeCount, reply_count AS ReplyCount, share_count AS ShareCount, " +
        "visibility AS Visibility, policy_version AS PolicyVersion, created_at AS CreatedAt";

    /// <summary>
    /// The same list table-qualified: <c>container_id</c>, <c>seq</c> and <c>post_id</c> exist on
    /// BOTH sides of the <c>home_timeline</c> join and SQLite refuses the bare name as ambiguous.
    /// </summary>
    private static readonly string JoinedColumns =
        string.Join(", ", Columns.Split(", ").Select(column => $"t.{column}"));

    // Contract §9.3: the delta is strictly seq > cursor, ascending, no repeats; the page without
    // a cursor is the newest window, descending (SPEC-0009 C, "mais recente primeiro").
    private static readonly string ContainerDelta =
        $"SELECT {Columns} FROM timeline_item WHERE container_id = @ContainerId AND seq > @After ORDER BY seq ASC LIMIT @Limit";

    private static readonly string ContainerNewest =
        $"SELECT {Columns} FROM timeline_item WHERE container_id = @ContainerId ORDER BY seq DESC LIMIT @Limit";

    // home_timeline carries the pointer; the summary itself lives once in timeline_item, so the
    // personal page joins the two on their shared (container_id, seq) key.
    private static readonly string ActorDelta =
        $"""
        SELECT {JoinedColumns} FROM home_timeline h
        JOIN timeline_item t ON t.container_id = h.container_id AND t.seq = h.seq
        WHERE h.actor_id = @ActorId AND h.seq > @After ORDER BY h.seq ASC LIMIT @Limit
        """;

    private static readonly string ActorOlder =
        $"""
        SELECT {JoinedColumns} FROM home_timeline h
        JOIN timeline_item t ON t.container_id = h.container_id AND t.seq = h.seq
        WHERE h.actor_id = @ActorId AND h.seq < @Before ORDER BY h.seq DESC LIMIT @Limit
        """;

    private static readonly string ActorNewest =
        $"""
        SELECT {JoinedColumns} FROM home_timeline h
        JOIN timeline_item t ON t.container_id = h.container_id AND t.seq = h.seq
        WHERE h.actor_id = @ActorId ORDER BY h.seq DESC LIMIT @Limit
        """;

    private readonly string _connectionString;

    /// <summary>Reads the same migrated file the host writes (its schema exists by then).</summary>
    public SqliteFeedTimeline(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// One window of a container feed: with <paramref name="afterSeq"/> the exact delta (rows
    /// strictly above it, ascending); without it the newest rows, descending.
    /// </summary>
    public async Task<IReadOnlyList<FeedTimelineRow>> ReadContainerAsync(
        string containerId, long? afterSeq, int limit)
    {
        await using var connection = Open();
        var rows = await connection.QueryAsync<FeedTimelineRow>(
            afterSeq.HasValue ? ContainerDelta : ContainerNewest,
            new { ContainerId = containerId, After = afterSeq, Limit = limit });
        return rows.AsList();
    }

    /// <summary>
    /// One window of an actor's personal timeline, same delta rule. <paramref name="beforeSeq"/>
    /// only steers the descending walk of a page whose rows the route filters afterwards — a
    /// cursor keeps moving instead of re-reading the same window.
    /// </summary>
    public async Task<IReadOnlyList<FeedTimelineRow>> ReadActorAsync(
        string actorId, long? afterSeq, long? beforeSeq, int limit)
    {
        var sql = afterSeq.HasValue ? ActorDelta : beforeSeq.HasValue ? ActorOlder : ActorNewest;
        await using var connection = Open();
        var rows = await connection.QueryAsync<FeedTimelineRow>(sql, new
        {
            ActorId = actorId,
            After = afterSeq,
            Before = beforeSeq,
            Limit = limit,
        });
        return rows.AsList();
    }

    /// <summary>
    /// The slug of a feed source: the tail of its canonical id (<c>{origin}/c/{slug}</c>) — the
    /// same rule <see cref="ActivityProjector"/> writes the membership key with, kept in one
    /// place so a reader and the projector can never spell a slug differently.
    /// </summary>
    public static string SlugOf(string containerId)
    {
        var separator = containerId.LastIndexOf('/');
        if (separator < 0 || separator == containerId.Length - 1)
        {
            throw new InvalidOperationException(
                $"a projection row carries a feed source id without a slug: '{containerId}'");
        }

        return containerId[(separator + 1)..];
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
