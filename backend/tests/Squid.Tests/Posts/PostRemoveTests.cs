using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.ReadModels.Projections;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Feed;

namespace Squid.Tests.Posts;

/// <summary>
/// TASK-0030 / SPEC-0009 section H (FR-027): the removal ladder and its evidence — the author's
/// soft delete writes the tombstone, the <c>Activity: Delete</c> event and the <c>RemoveContent</c>
/// trail row (AC-01); a Moderator+ removal owes a non-empty <c>takedownReason</c> while anyone who
/// is neither is refused with an audited <c>Deny</c> (AC-02); the post leaves feed, timeline and
/// <c>?since=</c> once the projector reads the event (AC-03); and a reader who could not see it
/// keeps the byte-identical 404 while no trail row is ever erased (AC-04).
/// </summary>
[Collection("ServiceHost")]
public sealed class PostRemoveTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public PostRemoveTests(AuthHostFixture host) => _host = host;

    private IPolicyAuditStore Audit =>
        _host.App.Services.GetRequiredService<IPolicyAuditStore>();

    /// <summary>The read side of the log — the very source the pump consumes from.</summary>
    private SqliteActivityLog Log =>
        _host.App.Services.GetRequiredService<SqliteActivityLog>();

    [Fact] // AC-01
    public async Task Author_removing_own_post_writes_the_tombstone_the_event_and_the_trail_row()
    {
        var (owner, handle) = await PostRoutes.SignedInAsync(_host, "rm01");
        var slug = await ContainerAsync(owner, "group", "Remocao do autor");

        const string content = "post que o autor remove";
        var (id, key, policyVersion) = await PublishAsync(owner, slug, content);

        // The author owes no reason (the task's decided semantics): a bare DELETE answers 200 with
        // the state of what was removed — the instant it happened and the absent reason.
        var removed = await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{key}", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var body = JsonDocument.Parse(await removed.Content.ReadAsStringAsync());
        Assert.Equal(id, body.RootElement.GetProperty("id").GetString());
        var deletedAt = body.RootElement.GetProperty("deletedAt").GetString();
        Assert.False(string.IsNullOrEmpty(deletedAt));
        Assert.Null(body.RootElement.GetProperty("takedownReason").GetString());

        // Soft delete, not a purge: the row stays with its content and gains a tombstone beside it.
        var row = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteObjectStore.StoreName, GrainId.Create("post", key).ToString());
        Assert.NotNull(row);
        Assert.Contains(content, row, StringComparison.Ordinal);

        var tombstone = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteObjectStore.TombstoneStoreName,
            GrainId.Create("posttomb", key).ToString());
        Assert.NotNull(tombstone);
        Assert.Contains("deletedAt", tombstone, StringComparison.OrdinalIgnoreCase);

        // ...and from here every read answers the shared 404.
        var gone = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{key}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        // The event is on the log, on the GLOBAL sequence, past the create it cancels (AC-01).
        var entries = await Log.ReadAsync();
        var create = Assert.Single(entries, entry => entry.ObjectId == id && entry.Type == "Create");
        var delete = Assert.Single(entries, entry => entry.ObjectId == id && entry.Type == "Delete");
        Assert.True(delete.Seq > create.Seq, $"Delete.Seq {delete.Seq} must follow Create.Seq {create.Seq}");

        // The trail row §2.11 asks for: author, instant, policy version and the tombstone state.
        var rows = await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: handle, Action: "RemoveContent", Limit: 500));
        var trail = Assert.Single(rows);
        Assert.Equal(PolicyAudit.Allow, trail.Result);
        Assert.Equal($"{PostRoutes.Origin(_host)}/users/{handle}", trail.ActorId);
        Assert.Equal(policyVersion, trail.PolicyVersion);
        using var state = JsonDocument.Parse(trail.Reason);
        Assert.Equal(deletedAt, state.RootElement.GetProperty("deletedAt").GetString());
        Assert.Null(state.RootElement.GetProperty("takedownReason").GetString());
    }

    [Fact] // AC-02
    public async Task A_non_author_is_denied_and_a_moderator_owes_a_takedown_reason()
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, "rm02dono");
        var (author, _) = await PostRoutes.SignedInAsync(_host, "rm02autor");
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "rm02membro");
        var slug = await ContainerAsync(owner, "community", "Remocao de terceiros");
        await JoinAsync(author, slug);
        await JoinAsync(member, slug);

        var (id, key, policyVersion) = await PublishAsync(author, slug, "post de terceiro");

        // An active plain member is neither the author nor Moderator+: refused with 403 and the
        // Deny row of data-model §2.11 — the post itself is untouched.
        var denied = await PostRoutes.SendAsync(member, HttpMethod.Delete, $"/api/posts/{key}", null, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var denies = await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: memberHandle, Action: "RemoveContent", Limit: 500));
        var deny = Assert.Single(denies);
        Assert.Equal(PolicyAudit.Deny, deny.Result);
        Assert.Equal(id, deny.ResourceId);
        Assert.Equal(policyVersion, deny.PolicyVersion);
        Assert.Equal(HttpStatusCode.OK,
            (await PostRoutes.SendAsync(author, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // The Owner is Moderator+: the removal goes through, but not without the reason §2.5
        // owes for third-party content (R-07) — absent means empty, whitespace means empty.
        var withoutReason = await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{key}", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutReason.StatusCode);
        Assert.Contains("validation_failed", await withoutReason.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var blankReason = await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{key}",
            new { takedownReason = "   " }, owner.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blankReason.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await PostRoutes.SendAsync(author, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // The reason may ride the query (the contract fixes no carrier): it echoes back and the
        // post is gone.
        const string queryReason = "regras-quebradas";
        var removed = await PostRoutes.SendAsync(owner, HttpMethod.Delete,
            $"/api/posts/{key}?takedownReason={queryReason}", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var body = JsonDocument.Parse(await removed.Content.ReadAsStringAsync()))
        {
            Assert.Equal(queryReason, body.RootElement.GetProperty("takedownReason").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound,
            (await PostRoutes.SendAsync(author, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // ...or the body: a second post of the same author, removed with the reason JSON-carried.
        var (_, secondKey, _) = await PublishAsync(author, slug, "segundo post de terceiro");
        const string bodyReason = "spam-reincidente";
        var byBody = await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{secondKey}",
            new { takedownReason = bodyReason }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, byBody.StatusCode);
        using (var body = JsonDocument.Parse(await byBody.Content.ReadAsStringAsync()))
        {
            Assert.Equal(bodyReason, body.RootElement.GetProperty("takedownReason").GetString());
        }

        // Both removals left their Allow row with the reason inside — the trail keeps the why.
        var allows = await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: ownerHandle, Action: "RemoveContent", Limit: 500));
        Assert.Equal(2, allows.Count);
        Assert.All(allows, entry => Assert.Equal(PolicyAudit.Allow, entry.Result));
        Assert.Contains(allows, entry => entry.ResourceId == id
            && entry.Reason.Contains(queryReason, StringComparison.Ordinal));
        Assert.Contains(allows, entry => entry.Reason.Contains(bodyReason, StringComparison.Ordinal));
    }

    [Fact] // AC-03
    public async Task The_removed_post_leaves_feed_timeline_and_delta_when_the_projector_runs()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "rm03dono");
        var (author, _) = await PostRoutes.SignedInAsync(_host, "rm03autor");
        var (member, _) = await PostRoutes.SignedInAsync(_host, "rm03membro");
        var slug = await ContainerAsync(owner, "community", "Remocao projecao");
        await JoinAsync(author, slug);
        await JoinAsync(member, slug);

        const string content = "some da projecao remocao";
        var (_, key, _) = await PublishAsync(author, slug, content);

        await FeedHarness.ProjectPendingAsync(_host);

        // Before: the projection serves it — feed, personal timeline and the post itself.
        var feedBefore = await FeedHarness.JsonOfAsync(await PostRoutes.SendAsync(
            owner, HttpMethod.Get, $"/api/containers/{slug}/feed?since=0"));
        Assert.Contains(content, FeedHarness.Contents(FeedHarness.Items(feedBefore)));

        var timelineBefore = await FeedHarness.JsonOfAsync(await PostRoutes.SendAsync(
            member, HttpMethod.Get, "/api/timeline?since=0"));
        Assert.Contains(content, FeedHarness.Contents(FeedHarness.Items(timelineBefore)));

        Assert.Equal(HttpStatusCode.OK,
            (await PostRoutes.SendAsync(member, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        var removed = await PostRoutes.SendAsync(owner, HttpMethod.Delete,
            $"/api/posts/{key}?takedownReason=some", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        await FeedHarness.ProjectPendingAsync(_host);

        // After: every window that served it stops — newest page, ?since= delta, the personal
        // timeline of a member who could read it, and the post itself (404 of section H).
        foreach (var path in new[] { $"/api/containers/{slug}/feed?since=0", $"/api/containers/{slug}/feed" })
        {
            var page = await FeedHarness.JsonOfAsync(
                await PostRoutes.SendAsync(owner, HttpMethod.Get, path));
            Assert.DoesNotContain(content, FeedHarness.Contents(FeedHarness.Items(page)));
        }

        var timelineAfter = await FeedHarness.JsonOfAsync(await PostRoutes.SendAsync(
            member, HttpMethod.Get, "/api/timeline?since=0"));
        Assert.DoesNotContain(content, FeedHarness.Contents(FeedHarness.Items(timelineAfter)));

        Assert.Equal(HttpStatusCode.NotFound,
            (await PostRoutes.SendAsync(member, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // The pump is caught up: a removal holds the high-water mark at its own event, so the
        // Create is never re-projected — a re-projected Create would resurrect the row the
        // removal just took out of every read.
        var pending = await Log.ReadAsync(
            await _host.App.Services.GetRequiredService<IActivityProjector>().LastProjectedSeqAsync());
        Assert.Empty(pending);
    }

    [Fact] // AC-04
    public async Task A_reader_who_never_saw_it_keeps_the_same_404_and_no_trail_row_is_erased()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "rm04dono");
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "rm04estr");
        var slug = await ContainerAsync(owner, "group", "Remocao invisivel");
        var (_, key, _) = await PublishAsync(owner, slug, "post fechado");

        // A stranger behind the closed container gets the shared 404 BEFORE the removal...
        var before = await stranger.SendAsync(HttpMethod.Get, $"/api/posts/{key}");
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        var beforeBytes = await before.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("traceId", Encoding.UTF8.GetString(beforeBytes), StringComparison.Ordinal);

        // ...and the whole trail as it stands right now, for the hash AC-04 asks for.
        var trailBefore = await Audit.QueryAsync(new PolicyAuditFilter(Limit: 1000));
        Assert.NotEmpty(trailBefore);
        var hashBefore = HashOf(trailBefore);

        // The author removes their own post (no reason owed — AC-01's path).
        var removed = await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{key}", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        // The removal reveals nothing: the very same bytes, no traceId, existence still unknown.
        var after = await stranger.SendAsync(HttpMethod.Get, $"/api/posts/{key}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        Assert.Equal(beforeBytes, await after.Content.ReadAsByteArrayAsync());

        // ...and not one row of the trail that existed before is gone or changed: the survivors,
        // hashed, are the same trail (the removal only ever appends).
        var trailAfter = await Audit.QueryAsync(new PolicyAuditFilter(Limit: 1000));
        var survivors = trailAfter.Where(entry => trailBefore.Contains(entry)).ToList();
        Assert.Equal(trailBefore.Count, survivors.Count);
        Assert.Equal(hashBefore, HashOf(survivors));
        Assert.True(trailAfter.Count > trailBefore.Count, "the removal must APPEND its own rows");
    }

    /// <summary>The SHA-256 of a trail slice — equal hashes mean not one row was erased or edited.</summary>
    private static string HashOf(IReadOnlyList<PolicyAuditEntry> rows) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', rows.Select(row => row.ToString())))));

    /// <summary>A container the caller owns, created through the route like every other suite does.</summary>
    private static async Task<string> ContainerAsync(AuthClient owner, string kind, string name)
    {
        var slug = PostRoutes.Slug("remocao");
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(owner, HttpMethod.Post,
            "/api/containers", new { kind, name, slug }, owner.Csrf)).StatusCode);
        return slug;
    }

    private static async Task JoinAsync(AuthClient client, string slug)
    {
        var joined = await PostRoutes.SendAsync(client, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, client.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");
    }

    /// <summary>Publishes one post and answers its canonical id, the route's key and the policy version.</summary>
    private static async Task<(string Id, string Key, int PolicyVersion)> PublishAsync(
        AuthClient author, string slug, string content)
    {
        var response = await PostRoutes.SendAsync(author, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content }, author.Csrf);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString()!;
        return (id, id[(id.LastIndexOf('/') + 1)..],
            json.RootElement.GetProperty("policyVersion").GetInt32());
    }
}
