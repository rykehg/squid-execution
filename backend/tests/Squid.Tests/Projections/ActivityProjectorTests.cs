using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Containers;
using Squid.Domain.Containers;
using Squid.ReadModels.Projections;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Projections;

/// <summary>
/// AC-02 of TASK-0024 — the projection function, invoked DIRECTLY (SPEC-0009 section B): one
/// <c>Activity: Create</c> becomes one <c>timeline_item</c> line with the post summary, the
/// author, the counters and the visibility it was published with, plus one <c>home_timeline</c>
/// line per member that is Active — and only those: the invited and the banned get nothing.
/// </summary>
[Collection("ServiceHost")]
public sealed class ActivityProjectorTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ActivityProjectorTests(AuthHostFixture host) => _host = host;

    private IActivityProjector Projector =>
        _host.App.Services.GetRequiredService<IActivityProjector>();

    /// <summary>The read side of the log — the very source the pump consumes from.</summary>
    private SqliteActivityLog Log =>
        _host.App.Services.GetRequiredService<SqliteActivityLog>();

    [Fact]
    public async Task Ac02_a_create_writes_the_post_summary_and_one_home_line_per_active_member()
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, "proj02dono");
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "proj02membro");
        var origin = PostRoutes.Origin(_host);
        var slug = PostRoutes.Slug("projeto");

        // A community admits joiners (the preset that opens the entry route — the group preset
        // keeps the container closed to outsiders, which would answer 404 here).
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(owner, HttpMethod.Post,
            "/api/containers", new { kind = "community", name = "Projecao", slug }, owner.Csrf)).StatusCode);

        // Active by the entry route...
        var joined = await PostRoutes.SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");

        // ...and two members that are NOT active, written straight through the store: one still
        // Invited, one Banned (data-model §3.3) — neither may receive the line.
        var store = _host.App.Services.GetRequiredService<IContainerStore>();
        var invitedHandle = PostRoutes.Handle("proj02convidado");
        var bannedHandle = PostRoutes.Handle("proj02banido");
        await store.UpsertMembershipAsync(Membership(slug, origin, invitedHandle, MembershipState.Invited));
        await store.UpsertMembershipAsync(Membership(slug, origin, bannedHandle, MembershipState.Banned));

        // Publish: the event arrives carrying the raw JSON of the post (R-13).
        var published = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "linha da projecao" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);

        string postId, containerId, visibility;
        long objectSeq;
        using (var view = JsonDocument.Parse(await published.Content.ReadAsStringAsync()))
        {
            postId = view.RootElement.GetProperty("id").GetString()!;
            containerId = view.RootElement.GetProperty("containerId").GetString()!;
            visibility = view.RootElement.GetProperty("visibilityAtPublish").GetString()!;
            objectSeq = view.RootElement.GetProperty("seq").GetInt64();
        }

        var activity = Assert.Single(await Log.ReadAsync(), entry => entry.ObjectId == postId);

        // AC-02: the function itself, called straight from the test — no pump, no hosted service.
        await Projector.ProjectAsync(activity);

        var item = Assert.Single(await ProjectionDb.QueryAsync<TimelineRow>(_host.StoragePath,
            "SELECT seq AS Seq, object_seq AS ObjectSeq, post_id AS PostId, author AS Author, "
            + "content AS Content, depth AS Depth, reply_to_id AS ReplyToId, "
            + "like_count AS LikeCount, reply_count AS ReplyCount, share_count AS ShareCount, "
            + "visibility AS Visibility, policy_version AS PolicyVersion "
            + "FROM timeline_item WHERE container_id = @ContainerId",
            new { ContainerId = containerId }));

        Assert.Equal(activity.Seq, item.Seq);          // the GLOBAL numbering (GAP-23) orders the feed
        Assert.Equal(objectSeq, item.ObjectSeq);        // the per-container number of data-model §2.5
        Assert.Equal(postId, item.PostId);
        Assert.Equal($"{origin}/users/{ownerHandle}", item.Author);
        Assert.Equal("linha da projecao", item.Content);
        Assert.Equal(0, item.Depth);
        Assert.Null(item.ReplyToId);
        Assert.Equal(visibility, item.Visibility);      // visibilidade efetiva, congelada na publicação
        Assert.Equal(0, item.LikeCount);                // contadores nascem zerados (derivados)
        Assert.Equal(0, item.ReplyCount);
        Assert.Equal(0, item.ShareCount);

        // ...and exactly one home line per member Active when the event was projected.
        var actors = await ProjectionDb.QueryAsync<string>(_host.StoragePath,
            "SELECT actor_id FROM home_timeline WHERE container_id = @ContainerId AND seq = @Seq",
            new { ContainerId = containerId, Seq = activity.Seq });

        Assert.Equal(
            new[] { $"{origin}/users/{ownerHandle}", $"{origin}/users/{memberHandle}" }
                .Order(StringComparer.Ordinal),
            actors.Order(StringComparer.Ordinal));
        Assert.DoesNotContain($"{origin}/users/{invitedHandle}", actors);
        Assert.DoesNotContain($"{origin}/users/{bannedHandle}", actors);
    }

    /// <summary>One membership row in a given state, without going through the routes that birth them.</summary>
    private static ContainerMembership Membership(
        string slug, string origin, string handle, MembershipState state) => new()
    {
        Slug = slug,
        ActorId = $"{origin}/users/{handle}",
        Handle = handle,
        Role = MembershipRole.Member,
        State = state,
        JoinedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>The timeline line as the columns carry it (Dapper binds the constructor).</summary>
    private sealed record TimelineRow(
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
        long PolicyVersion);
}
