using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Containers;
using Squid.Api.Federation;
using Squid.Api.Posts;
using Squid.Domain.Containers;
using Squid.Tests.Posts;

namespace Squid.Tests.Federation;

/// <summary>
/// QA-0045's own scenario for TASK-0045 AC-01 — the clause the DEV's suite does not reach:
/// a <c>public</c> publication must ALSO enqueue a <c>PeerDelivery</c> for the container's own
/// collection ("e para a coleção <c>followers</c> do contêiner"). The product renders that
/// collection as the container's <see cref="MembershipState.Active"/> memberships
/// (<c>OutboundPipeline</c> Public branch; declared at <c>IOutboundPipeline.cs:17-18</c>) —
/// no other container-followers relation exists in the MVP (contract §1.2 publishes
/// <c>members</c>, there is no <c>/c/{slug}/followers</c> route, and an inbound
/// <c>Follow</c> whose object is a container URI is answered 202 without applying
/// state). The reading itself is a QA finding for ODEV to adjudicate; this test pins the
/// behaviour the AC describes under the reading in force. Every expected value is derived
/// from the stores at run time, never from a remembered label.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0045FanOutGapTests : IClassFixture<InboxHostFixture>
{
    private readonly InboxHostFixture _host;

    public QaTask0045FanOutGapTests(InboxHostFixture host) => _host = host;

    [Fact] // AC-01 (coleção followers do contêiner)
    public async Task Ac01_public_post_also_enqueues_for_the_container_collection()
    {
        _host.Sender.Calls.Clear();
        var (owner, handle, slug) = await InboxHarness.CommunityAsync(_host, "qa45ac01");
        var follower = $"https://remote.example/users/{InboxHarness.Unique("qa45f")}";
        var member = $"https://remote.example/users/{InboxHarness.Unique("qa45m")}";
        await FollowAsync(handle, follower); // remote follower of the author
        await MembershipAsync(slug, member, MembershipState.Active); // container collection, NOT following the author

        var response = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "publicado para fora" }, owner.Csrf);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        using var published = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var objectId = published.RootElement.GetProperty("id").GetString()!;

        // Expectations derived from the artifacts the stores hold (never a remembered label).
        var container = (await _host.App.Services.GetRequiredService<IContainerStore>().FindAsync(slug))!.Container;
        var post = (await _host.App.Services.GetRequiredService<IObjectStore>().FindAsync(objectId))!;

        var rows = await _host.App.Services.GetRequiredService<IPeerDeliveryStore>()
            .ForObjectAsync(objectId, PeerActivity.Create);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.RecipientActorId == follower);
        Assert.Contains(rows, row => row.RecipientActorId == member); // the AC's parenthetical
        Assert.All(rows, row =>
        {
            Assert.Equal(PeerDeliveryState.Pending, row.State);
            Assert.Equal(container.Policy.Version, row.PolicyVersionAtEnqueue);
        });

        using var payload = JsonDocument.Parse(rows[0].Payload);
        var activity = payload.RootElement;
        Assert.Equal(container.Id, activity.GetProperty("audience").GetString()); // D-01 named destination
        Assert.Equal(post.AuthorActorId, activity.GetProperty("actor").GetString());
        Assert.Equal(
            new[] { InboxHarness.Public },
            activity.GetProperty("to").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            new[] { $"{post.AuthorActorId}/followers" },
            activity.GetProperty("cc").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(container.Slug, activity.GetProperty("object").GetProperty("squid:slug").GetString());

        Assert.Empty(_host.Sender.Calls); // enqueued only — the wire is TASK-0046's clock (FR-036)
    }

    private async Task FollowAsync(string handle, params string[] followers)
    {
        var follows = _host.App.Services.GetRequiredService<IFollowStore>();
        foreach (var follower in followers)
        {
            await follows.AddFollowerAsync(handle, follower, DateTimeOffset.UtcNow);
        }
    }

    private async Task MembershipAsync(string slug, string actorUri, MembershipState state)
    {
        await _host.App.Services.GetRequiredService<IContainerStore>().UpsertMembershipAsync(
            new ContainerMembership
            {
                Slug = slug,
                ActorId = actorUri,
                Handle = actorUri[(actorUri.LastIndexOf('/') + 1)..],
                Role = MembershipRole.Member,
                State = state,
                JoinedAt = DateTimeOffset.UtcNow,
            });
    }
}
