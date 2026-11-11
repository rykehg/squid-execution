using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Orleans.Runtime;
using Squid.Domain.Containers;

namespace Squid.ReadModels.Projections;

/// <summary>
/// The Dapper projection of SPEC-0009 section B (AC-02): one <c>Activity: Create</c> becomes one
/// <c>timeline_item</c> line — the post summary, its author, its counters and the visibility it
/// was published with — plus one <c>home_timeline</c> line per member that is Active when the
/// event is projected. Both writes happen in ONE transaction per event, with
/// <c>ON CONFLICT DO NOTHING</c> on the primary key, so a re-run after a crash never duplicates a
/// line and a resumed pump never sees half an event (AC-04). Never called from the write path:
/// the pump owns that decision (R-18).
/// </summary>
public sealed class ActivityProjector : IActivityProjector
{
    /// <summary>The event words this projection acts on; every other type belongs to a later task.</summary>
    private const string Create = "Create";

    private const string Delete = "Delete";

    /// <summary>
    /// The identity a projection-position line wears in <c>home_timeline</c>: it belongs to no
    /// actor, no container and no post, so no feed, timeline or white-box query that filters by
    /// any of the three can ever reach it.
    /// </summary>
    private const string ProjectionActor = "urn:squid:projection";

    /// <summary>Membership document's store name — the value <c>Squid.Service.SqliteContainerStore</c> writes.</summary>
    private const string MembersStoreName = "container-members";

    private const string InsertTimeline = """
        INSERT INTO timeline_item
            (container_id, seq, object_seq, post_id, author, content, depth, reply_to_id,
             visibility, policy_version, created_at)
        VALUES
            (@ContainerId, @Seq, @ObjectSeq, @PostId, @Author, @Content, @Depth, @ReplyToId,
             @Visibility, @PolicyVersion, @CreatedAt)
        ON CONFLICT (container_id, seq) DO NOTHING
        """;

    private const string InsertHome = """
        INSERT INTO home_timeline (actor_id, seq, container_id, post_id)
        VALUES (@ActorId, @Seq, @ContainerId, @PostId)
        ON CONFLICT (actor_id, seq) DO NOTHING
        """;

    private const string RemoveTimeline = "DELETE FROM timeline_item WHERE post_id = @PostId";

    private const string RemoveHome = "DELETE FROM home_timeline WHERE post_id = @PostId";

    /// <summary>
    /// The position line a removal leaves behind: one row at the Delete event's own global Seq,
    /// owned by <see cref="ProjectionActor"/>. The pump's high-water mark is MAX(seq) over these
    /// tables, so a removal that simply dropped every row would drag the mark back under the
    /// Create — the next pass would re-project it and the post would flicker back into the feed.
    /// This line holds the mark exactly at the event it belongs to, and it can never be read:
    /// no actor, container or post query matches it.
    /// </summary>
    private const string InsertPosition = """
        INSERT INTO home_timeline (actor_id, seq, container_id, post_id)
        VALUES (@ActorId, @Seq, '', '')
        ON CONFLICT (actor_id, seq) DO NOTHING
        """;

    private const string ReadMembers = """
        SELECT State FROM GrainState WHERE StoreName = @StoreName AND GrainId = @GrainId
        """;

    private const string MaxTimelineSeq = "SELECT MAX(seq) FROM timeline_item";

    private const string MaxHomeSeq = "SELECT MAX(seq) FROM home_timeline";

    private readonly string _connectionString;

    /// <summary>Projects into the same migrated file the host writes (its schema exists by then).</summary>
    public ActivityProjector(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<long> LastProjectedSeqAsync()
    {
        await using var connection = Open();
        var timeline = await connection.QuerySingleAsync<long?>(MaxTimelineSeq);
        var home = await connection.QuerySingleAsync<long?>(MaxHomeSeq);
        return Math.Max(timeline ?? 0, home ?? 0);
    }

    /// <inheritdoc />
    public async Task ProjectAsync(ActivityLogEntry activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        // SPEC-0009 section H: a removal takes the post's rows out of every read; anything that is
        // neither Create nor Delete is not this slice's event.
        if (string.Equals(activity.Type, Delete, StringComparison.Ordinal))
        {
            await RemoveAsync(activity);
            return;
        }

        if (!string.Equals(activity.Type, Create, StringComparison.Ordinal)) return;

        using var payload = JsonDocument.Parse(activity.Payload);
        var summary = payload.RootElement;
        var containerId = Required(summary, "containerId");
        var postId = Required(summary, "id");
        var replyToId = summary.TryGetProperty("replyToId", out var reply)
            && reply.ValueKind != JsonValueKind.Null
            ? reply.GetString()
            : null;
        var createdAt = summary.GetProperty("createdAt").GetDateTimeOffset()
            .ToString("O", CultureInfo.InvariantCulture);

        await using var connection = Open();
        // shortcut: the member list is read when the event is PROJECTED, not when it was written —
        // the payload carries no snapshot of it (and Squid.Api, which writes the payload, is
        // outside this task's touches), so a membership changing in the gap between publish and
        // projection resolves to the state read here. The event-time snapshot is parked for the
        // phase that owns membership history.
        var members = await ActiveMembersAsync(connection, SqliteFeedTimeline.SlugOf(containerId));

        await using var transaction = connection.BeginTransaction(deferred: false);
        await connection.ExecuteAsync(InsertTimeline, new
        {
            ContainerId = containerId,
            activity.Seq,
            ObjectSeq = summary.GetProperty("seq").GetInt64(),
            PostId = postId,
            Author = Required(summary, "author"),
            Content = Required(summary, "content"),
            Depth = summary.GetProperty("depth").GetInt32(),
            ReplyToId = replyToId,
            Visibility = Required(summary, "visibilityAtPublish"),
            PolicyVersion = summary.GetProperty("policyVersion").GetInt32(),
            CreatedAt = createdAt,
        }, transaction);

        foreach (var actorId in members)
        {
            await connection.ExecuteAsync(InsertHome, new
            {
                ActorId = actorId,
                activity.Seq,
                ContainerId = containerId,
                PostId = postId,
            }, transaction);
        }

        await transaction.CommitAsync();
    }

    /// <summary>
    /// SPEC-0009 section H (AC-03): one <c>Activity: Delete</c> takes the removed post out of
    /// every projection — both of its rows, wherever they are — inside ONE transaction, and
    /// leaves the position line of <see cref="InsertPosition"/> so the pump's high-water mark
    /// stays at this event (the Create can never be re-projected, which would resurrect the row).
    /// Re-running an already projected removal changes nothing: the deletes find nothing and the
    /// position line conflicts with itself (AC-04's resume rule).
    /// </summary>
    private async Task RemoveAsync(ActivityLogEntry activity)
    {
        using var payload = JsonDocument.Parse(activity.Payload);
        var postId = Required(payload.RootElement, "id");

        await using var connection = Open();
        await using var transaction = connection.BeginTransaction(deferred: false);
        await connection.ExecuteAsync(RemoveTimeline, new { PostId = postId }, transaction);
        await connection.ExecuteAsync(RemoveHome, new { PostId = postId }, transaction);
        await connection.ExecuteAsync(InsertPosition, new { ActorId = ProjectionActor, activity.Seq }, transaction);
        await transaction.CommitAsync();
    }

    /// <summary>The actors whose membership document says Active — the readers this line reaches.</summary>
    private static async Task<IReadOnlyList<string>> ActiveMembersAsync(
        SqliteConnection connection, string slug)
    {
        var state = await connection.QuerySingleOrDefaultAsync<string>(ReadMembers, new
        {
            StoreName = MembersStoreName,
            // Derived, never spelled: the key's shape belongs to Orleans (measured: members/{slug}),
            // the same call SqliteContainerStore writes with.
            GrainId = GrainId.Create("members", slug).ToString(),
        });

        if (string.IsNullOrEmpty(state)) return [];
        var document = JsonSerializer.Deserialize<MembersDocument>(state);
        return document?.Members?
            .Where(member => member.State == MembershipState.Active)
            .Select(member => member.ActorId)
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
    }

    /// <summary>One payload field, or a loud failure: a Create without its summary cannot project.</summary>
    private static string Required(JsonElement summary, string property) =>
        summary.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException(
                $"the event payload has no '{property}' string — not a post summary this projection understands");

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>The stored membership row (the same document <c>SqliteContainerStore</c> writes).</summary>
    private sealed record MembersDocument(string Slug, List<ContainerMembership> Members);
}
