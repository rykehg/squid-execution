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
using Squid.Tests.Projections;

namespace Squid.Tests.Posts;

/// <summary>
/// QA-0030 / TASK-0030 — the five acceptance criteria of FR-027 over the host
/// <see cref="SquidServiceHost"/> builds, aimed at the three hard spots this task's dispatch
/// names: the 404 that must be byte-identical between a REMOVED post and one that NEVER existed
/// (AC-04), the <c>RemoveContent</c> trail line that must survive the traffic after the removal
/// (AC-01/AC-04, hashed before and after), and the pump convergence after a TRAILING removal —
/// the Create projected BEFORE the Delete, which is exactly the order the neutral position row
/// exists for (AC-03). Every expected value is read from an artifact this run produced.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0030Tests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public QaTask0030Tests(AuthHostFixture host) => _host = host;

    private IPolicyAuditStore Audit =>
        _host.App.Services.GetRequiredService<IPolicyAuditStore>();

    /// <summary>The read side of the log — the very source the pump consumes from.</summary>
    private SqliteActivityLog Log =>
        _host.App.Services.GetRequiredService<SqliteActivityLog>();

    private IActivityProjector Projector =>
        _host.App.Services.GetRequiredService<IActivityProjector>();

    [Fact] // AC-04 — hard spot: removed vs never-existed, byte for byte
    public async Task A_removed_post_answers_the_very_same_404_bytes_as_one_that_never_existed()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa30404");
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "qa30404e");
        var anonymous = new AuthClient(_host.Http);
        var slug = await ContainerAsync(owner, "group", "QA-0030 invisivel");
        const string content = "post fechado que a QA-0030 remove";
        var (_, key, _) = await PublishAsync(owner, slug, content);

        // An id that never existed, for a signed-in stranger and for a sessionless reader.
        var ghost = "nunca-existiu-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

        // BEFORE the removal: the post outside the stranger's sight and the id that never existed
        // answer the same bytes — no traceId, no hint of what exists (SC-005).
        var invisibleBefore = await stranger.SendAsync(HttpMethod.Get, $"/api/posts/{key}");
        var ghostBefore = await stranger.SendAsync(HttpMethod.Get, $"/api/posts/{ghost}");
        Assert.Equal(HttpStatusCode.NotFound, invisibleBefore.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ghostBefore.StatusCode);
        var ghostBytes = await ghostBefore.Content.ReadAsByteArrayAsync();
        Assert.Equal(ghostBytes, await invisibleBefore.Content.ReadAsByteArrayAsync());
        Assert.DoesNotContain("traceId", Encoding.UTF8.GetString(ghostBytes), StringComparison.Ordinal);

        // The author removes their own post (AC-01's path, no reason owed).
        var removed = await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{key}", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        // AFTER: the removal reveals nothing to anyone — the removed post answers the very same
        // bytes as the id that never existed, for the stranger, for the author (who could read it
        // one request ago) and for a reader with no session at all.
        var afterStranger = await stranger.SendAsync(HttpMethod.Get, $"/api/posts/{key}");
        Assert.Equal(HttpStatusCode.NotFound, afterStranger.StatusCode);
        Assert.Equal(ghostBytes, await afterStranger.Content.ReadAsByteArrayAsync());

        var afterAuthor = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{key}");
        Assert.Equal(HttpStatusCode.NotFound, afterAuthor.StatusCode);
        Assert.Equal(ghostBytes, await afterAuthor.Content.ReadAsByteArrayAsync());

        var afterAnonymous = await anonymous.SendAsync(HttpMethod.Get, $"/api/posts/{key}");
        var ghostAnonymous = await anonymous.SendAsync(HttpMethod.Get, $"/api/posts/{ghost}");
        Assert.Equal(HttpStatusCode.NotFound, afterAnonymous.StatusCode);
        Assert.Equal(ghostBytes, await afterAnonymous.Content.ReadAsByteArrayAsync());
        Assert.Equal(ghostBytes, await ghostAnonymous.Content.ReadAsByteArrayAsync());

        // ...and a repeat DELETE (the tombstone already exists) answers the same 404 as a DELETE
        // on an id that never existed — the second removal is indistinguishable from no post.
        var repeat = await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{key}", null, owner.Csrf);
        var ghostDelete = await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{ghost}", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, repeat.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ghostDelete.StatusCode);
        Assert.Equal(ghostBytes, await repeat.Content.ReadAsByteArrayAsync());
        Assert.Equal(ghostBytes, await ghostDelete.Content.ReadAsByteArrayAsync());
    }

    [Fact] // AC-01 — hard spot: the trail line survives the removal and the traffic after it
    public async Task The_removal_persists_the_tombstone_the_delete_event_and_a_trail_line_that_survives()
    {
        var (owner, handle) = await PostRoutes.SignedInAsync(_host, "qa30aut");
        var (reader, _) = await PostRoutes.SignedInAsync(_host, "qa30lei");
        var slug = await ContainerAsync(owner, "group", "QA-0030 trilha");
        const string content = "post que a QA-0030 remove e audita";
        var (id, key, policyVersion) = await PublishAsync(owner, slug, content);

        // The whole trail as it stands right now, for the hash AC-04 asks for.
        var trailBefore = await Audit.QueryAsync(new PolicyAuditFilter(Limit: 1000));
        Assert.NotEmpty(trailBefore);
        var hashBefore = HashOf(trailBefore);

        var removed = await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{key}", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var body = JsonDocument.Parse(await removed.Content.ReadAsStringAsync());
        var deletedAt = body.RootElement.GetProperty("deletedAt").GetString();
        Assert.False(string.IsNullOrEmpty(deletedAt));

        // Soft delete, not a purge: the row keeps its content and the tombstone carries DeletedAt.
        var row = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteObjectStore.StoreName, GrainId.Create("post", key).ToString());
        Assert.NotNull(row);
        Assert.Contains(content, row, StringComparison.Ordinal);
        var tombstone = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteObjectStore.TombstoneStoreName,
            GrainId.Create("posttomb", key).ToString());
        Assert.NotNull(tombstone);
        Assert.Contains("deletedAt", tombstone, StringComparison.OrdinalIgnoreCase);

        // The event is on the log, on the GLOBAL sequence, past the create it cancels.
        var entries = await Log.ReadAsync();
        var create = Assert.Single(entries, entry => entry.ObjectId == id && entry.Type == "Create");
        var delete = Assert.Single(entries, entry => entry.ObjectId == id && entry.Type == "Delete");
        Assert.True(delete.Seq > create.Seq, $"Delete.Seq {delete.Seq} must follow Create.Seq {create.Seq}");

        // The trail row §2.11 asks for: author, instant, policy version and the tombstone state.
        var trail = Assert.Single(await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: handle, Action: "RemoveContent", Limit: 500)));
        Assert.Equal(PolicyAudit.Allow, trail.Result);
        Assert.Equal($"{PostRoutes.Origin(_host)}/users/{handle}", trail.ActorId);
        Assert.Equal(policyVersion, trail.PolicyVersion);
        Assert.NotEqual(default, trail.At);
        using var state = JsonDocument.Parse(trail.Reason);
        Assert.Equal(deletedAt, state.RootElement.GetProperty("deletedAt").GetString());

        // ...and the line SURVIVES everything that happens after the removal: a stranger's read
        // (404) and a repeat DELETE (404) both append their own rows, none rewrites the old ones.
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostRoutes.SendAsync(reader, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostRoutes.SendAsync(owner, HttpMethod.Delete, $"/api/posts/{key}", null, owner.Csrf)).StatusCode);

        var trailAfter = await Audit.QueryAsync(new PolicyAuditFilter(Limit: 1000));
        var survivors = trailAfter.Where(entry => trailBefore.Contains(entry)).ToList();
        Assert.Equal(trailBefore.Count, survivors.Count);
        Assert.Equal(hashBefore, HashOf(survivors));
        Assert.True(trailAfter.Count > trailBefore.Count, "the traffic after must APPEND rows");
        Assert.Contains(trail, trailAfter);
    }

    [Fact] // AC-03 — hard spot: pump convergence after a trailing removal (project-then-remove)
    public async Task After_a_trailing_removal_the_pump_converges_and_the_post_never_flickers_back()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa30pro");
        var (member, _) = await PostRoutes.SignedInAsync(_host, "qa30prom");
        var slug = await ContainerAsync(owner, "community", "QA-0030 projecao");
        await JoinAsync(member, slug);
        const string content = "some da projecao que a QA-0030 remove";
        var (id, key, _) = await PublishAsync(owner, slug, content);

        // The Create is projected FIRST — the order the neutral position row exists for: the post
        // is served, then the removal arrives as the TRAILING event of the log.
        await FeedHarness.ProjectPendingAsync(_host);
        var before = await FeedHarness.JsonOfAsync(await PostRoutes.SendAsync(
            owner, HttpMethod.Get, $"/api/containers/{slug}/feed?since=0"));
        Assert.Contains(content, FeedHarness.Contents(FeedHarness.Items(before)));
        Assert.Equal(HttpStatusCode.OK,
            (await PostRoutes.SendAsync(member, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        var removed = await PostRoutes.SendAsync(owner, HttpMethod.Delete,
            $"/api/posts/{key}?takedownReason=some", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var delete = Assert.Single(await Log.ReadAsync(),
            entry => entry.ObjectId == id && entry.Type == "Delete");

        await FeedHarness.ProjectPendingAsync(_host);

        // Every window that served it stops: newest page, ?since= delta, the personal timeline of
        // the member who could read it, and the post itself (404 of section H).
        foreach (var path in new[] { $"/api/containers/{slug}/feed?since=0", $"/api/containers/{slug}/feed" })
        {
            var page = await FeedHarness.JsonOfAsync(
                await PostRoutes.SendAsync(owner, HttpMethod.Get, path));
            Assert.DoesNotContain(content, FeedHarness.Contents(FeedHarness.Items(page)));
        }

        var timeline = await FeedHarness.JsonOfAsync(await PostRoutes.SendAsync(
            member, HttpMethod.Get, "/api/timeline?since=0"));
        Assert.DoesNotContain(content, FeedHarness.Contents(FeedHarness.Items(timeline)));
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostRoutes.SendAsync(member, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // The high-water mark sits AT the removal itself: no event is pending, and the neutral
        // position line the Delete arm writes is on disk at that exact seq — without it the mark
        // would fall back under the Create and the next pass would re-project it.
        Assert.Equal(delete.Seq, await Projector.LastProjectedSeqAsync());
        Assert.Empty(await Log.ReadAsync(await Projector.LastProjectedSeqAsync()));
        var position = await ProjectionDb.QueryAsync<PositionRow>(_host.StoragePath,
            "SELECT actor_id AS ActorId, seq AS Seq FROM home_timeline" +
            " WHERE actor_id = 'urn:squid:projection' AND seq = @Seq",
            new { Seq = delete.Seq });
        Assert.Single(position);

        // A second pass over a caught-up pump changes nothing and resurrects nothing — the
        // flicker scenario: a re-projected Create would put the row back into every read.
        await FeedHarness.ProjectPendingAsync(_host);
        Assert.Empty(await Log.ReadAsync(await Projector.LastProjectedSeqAsync()));
        foreach (var path in new[] { $"/api/containers/{slug}/feed?since=0", $"/api/containers/{slug}/feed" })
        {
            var page = await FeedHarness.JsonOfAsync(
                await PostRoutes.SendAsync(owner, HttpMethod.Get, path));
            Assert.DoesNotContain(content, FeedHarness.Contents(FeedHarness.Items(page)));
        }

        var timelineAgain = await FeedHarness.JsonOfAsync(await PostRoutes.SendAsync(
            member, HttpMethod.Get, "/api/timeline?since=0"));
        Assert.DoesNotContain(content, FeedHarness.Contents(FeedHarness.Items(timelineAgain)));
    }

    [Fact] // AC-02 — the LITERAL Moderator role (not only the Owner) on the third-party ladder
    public async Task A_literal_moderator_removes_a_third_party_post_only_with_a_reason()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa30don");
        var (author, _) = await PostRoutes.SignedInAsync(_host, "qa30aut2");
        var (mod, modHandle) = await PostRoutes.SignedInAsync(_host, "qa30mod");
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "qa30mbr");
        var slug = await ContainerAsync(owner, "community", "QA-0030 moderacao");
        await JoinAsync(author, slug);
        await JoinAsync(mod, slug);
        await JoinAsync(member, slug);

        // The literal data-model §3.2 Moderator role — the evaluator's own capability path,
        // never a role `if` in the route.
        var promoted = await PostRoutes.SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{modHandle}", new { role = "moderator" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);

        var (id, key, policyVersion) = await PublishAsync(author, slug, "post de terceiro da QA-0030");

        // A plain active member is neither the author nor Moderator+: 403 with the audited Deny.
        var denied = await PostRoutes.SendAsync(member, HttpMethod.Delete, $"/api/posts/{key}", null, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var deny = Assert.Single(await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: memberHandle, Action: "RemoveContent", Limit: 500)));
        Assert.Equal(PolicyAudit.Deny, deny.Result);
        Assert.Equal(id, deny.ResourceId);
        Assert.Equal(policyVersion, deny.PolicyVersion);
        Assert.Equal(HttpStatusCode.OK,
            (await PostRoutes.SendAsync(author, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // The Moderator owes the reason §2.5 owes for third-party content (R-07): whitespace means
        // empty → 422, and the post is untouched (no partial removal behind a refused payload).
        var blank = await PostRoutes.SendAsync(mod, HttpMethod.Delete, $"/api/posts/{key}",
            new { takedownReason = "   " }, mod.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        Assert.Contains("validation_failed", await blank.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK,
            (await PostRoutes.SendAsync(author, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // With a real reason the literal Moderator's removal goes through and echoes it back.
        const string reason = "violou-as-regras-da-comunidade";
        var removed = await PostRoutes.SendAsync(mod, HttpMethod.Delete, $"/api/posts/{key}",
            new { takedownReason = reason }, mod.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var json = JsonDocument.Parse(await removed.Content.ReadAsStringAsync()))
        {
            Assert.Equal(reason, json.RootElement.GetProperty("takedownReason").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound,
            (await PostRoutes.SendAsync(author, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // One Allow row for the Moderator (the 422 wrote none — validations do not gravam), the
        // refusal and the grant both keep the reason and the policy version.
        var allow = Assert.Single(await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: modHandle, Action: "RemoveContent", Limit: 500)));
        Assert.Equal(PolicyAudit.Allow, allow.Result);
        Assert.Equal(id, allow.ResourceId);
        Assert.Equal(policyVersion, allow.PolicyVersion);
        Assert.Contains(reason, allow.Reason, StringComparison.Ordinal);
    }

    /// <summary>The SHA-256 of a trail slice — equal hashes mean not one row was erased or edited.</summary>
    private static string HashOf(IReadOnlyList<PolicyAuditEntry> rows) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', rows.Select(row => row.ToString())))));

    /// <summary>A container the caller owns, created through the route like every other suite does.</summary>
    private static async Task<string> ContainerAsync(AuthClient owner, string kind, string name)
    {
        var slug = PostRoutes.Slug("qa30");
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

    /// <summary>One neutral position line the Delete arm leaves in <c>home_timeline</c>.</summary>
    private sealed record PositionRow(string ActorId, long Seq);
}
