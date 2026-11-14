using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Api.Crypto;
using Squid.Api.Federation;
using Squid.Domain.Containers;
using Squid.Grains.Actors;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using ActorStore = Squid.Tests.Actors.SqliteActorEventStore;

namespace Squid.Tests.Federation;

/// <summary>
/// AC-01..AC-06 of TASK-0045 — the outbound half of SPEC-0011 section F, proved over HTTP
/// against the host <see cref="SquidServiceHost"/> builds: the fan-out of a publication by
/// visibility with the destination the GAP-48 amendment names (<c>audience</c> + <c>squid:slug</c>),
/// the follow route of GAP-41, the R-05 re-evaluation that cancels instead of leaking, and the
/// Delete that walks the Create's recipients. Every signature is real draft-cavage over the
/// actor's runtime key and every "peer" is the fake sender the GAP-17 seam swaps in — the
/// production pipeline runs end to end, only the sockets are fake.
/// </summary>
[Collection("ServiceHost")]
public sealed class OutboundFanOutTests : IClassFixture<InboxHostFixture>
{
    private readonly InboxHostFixture _host;

    public OutboundFanOutTests(InboxHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Ac01_public_post_enqueues_one_delivery_per_remote_follower_and_names_the_destination()
    {
        _host.Sender.Calls.Clear();
        var (owner, handle, slug) = await InboxHarness.CommunityAsync(_host, "ac01");
        var first = Remote("ac01a");
        var second = Remote("ac01b");
        var local = $"{_host.Origin}/users/{InboxHarness.Unique("ac01l")}";
        await FollowAsync(handle, first, second, local); // the local follower never crosses the wire

        var objectId = await PublishAsync(owner, slug, "publicado para fora");

        var rows = await Store.ForObjectAsync(objectId, PeerActivity.Create);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { first, second }, rows.Select(row => row.RecipientActorId).ToArray());
        Assert.All(rows, row =>
        {
            Assert.Equal(PeerDeliveryState.Pending, row.State);
            Assert.Equal(1, row.PolicyVersionAtEnqueue);
            Assert.Equal(rows[0].ActivityId, row.ActivityId); // one activity, N recipients
        });

        // AC-01's payload: §2.1 addressing AND the named destination D-01 resolves remotely.
        using var payload = JsonDocument.Parse(rows[0].Payload);
        var activity = payload.RootElement;
        Assert.Equal("Create", activity.GetProperty("type").GetString());
        Assert.Equal($"{_host.Origin}/users/{handle}", activity.GetProperty("actor").GetString());
        Assert.Equal(
            new[] { InboxHarness.Public },
            activity.GetProperty("to").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            new[] { $"{_host.Origin}/users/{handle}/followers" },
            activity.GetProperty("cc").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal($"{_host.Origin}/c/{slug}", activity.GetProperty("audience").GetString());

        var note = activity.GetProperty("object");
        Assert.Equal(objectId, note.GetProperty("id").GetString());
        Assert.Equal(slug, note.GetProperty("squid:slug").GetString());
        Assert.Equal("public", note.GetProperty("squid:policy").GetProperty("visibility").GetString());

        Assert.Empty(_host.Sender.Calls); // enqueued ≠ sent (FR-036)
    }

    [Fact] // AC-05
    public async Task Ac05_publish_returns_with_every_delivery_already_pending_and_the_wire_untouched()
    {
        _host.Sender.Calls.Clear();
        var (owner, handle, slug) = await InboxHarness.CommunityAsync(_host, "ac05");
        await FollowAsync(handle, Remote("ac05"));

        // The answer the CLIENT gets: 201, and the queue is already readable as Pending — the
        // publish never waited on a peer, and never will (FR-036).
        var response = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "resposta sem esperar a entrega" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(await Store.ForObjectAsync(
            body.RootElement.GetProperty("id").GetString()!, PeerActivity.Create));
        Assert.Equal(PeerDeliveryState.Pending, row.State);
        Assert.Equal(0, row.Attempts);
        Assert.Empty(_host.Sender.Calls);
    }

    [Fact] // AC-02 (unlisted)
    public async Task Ac02_unlisted_delivers_only_to_followers()
    {
        _host.Sender.Calls.Clear();
        var (owner, handle, slug) = await InboxHarness.CommunityAsync(_host, "ac02u");
        var follower = Remote("ac02uf");
        var member = Remote("ac02um");
        await FollowAsync(handle, follower);
        await MemberAsync(slug, member, MembershipState.Active); // member ≠ follower
        await VisibilityAsync(owner, slug, "unlisted");

        var objectId = await PublishAsync(owner, slug, "só para seguidores");

        var row = Assert.Single(await Store.ForObjectAsync(objectId, PeerActivity.Create));
        Assert.Equal(follower, row.RecipientActorId);

        using var payload = JsonDocument.Parse(row.Payload);
        var activity = payload.RootElement;
        Assert.Equal(
            new[] { $"{_host.Origin}/users/{handle}/followers" },
            activity.GetProperty("to").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Empty(activity.GetProperty("cc").EnumerateArray()); // unlisted addresses nobody in cc (§4)
        Assert.Empty(_host.Sender.Calls);
    }

    [Fact] // AC-02 (membersOnly)
    public async Task Ac02_members_only_delivers_only_to_active_members()
    {
        _host.Sender.Calls.Clear();
        var (owner, handle, slug) = await InboxHarness.CommunityAsync(_host, "ac02m");
        var follower = Remote("ac02mf"); // follows, but is no member
        var active = Remote("ac02ma");
        var banned = Remote("ac02mb");
        await FollowAsync(handle, follower);
        await MemberAsync(slug, active, MembershipState.Active);
        await MemberAsync(slug, banned, MembershipState.Banned);
        await VisibilityAsync(owner, slug, "membersOnly");

        var objectId = await PublishAsync(owner, slug, "só para membros ativos");

        var row = Assert.Single(await Store.ForObjectAsync(objectId, PeerActivity.Create));
        Assert.Equal(active, row.RecipientActorId); // neither the follower nor the banned member

        using var payload = JsonDocument.Parse(row.Payload);
        var activity = payload.RootElement;
        Assert.Empty(activity.GetProperty("cc").EnumerateArray()); // nada em cc (AC-02)
        Assert.Contains(
            active,
            activity.GetProperty("to").EnumerateArray().Select(item => item.GetString()));
        Assert.DoesNotContain(
            follower,
            activity.GetProperty("to").EnumerateArray().Select(item => item.GetString()));
        Assert.DoesNotContain(
            banned,
            activity.GetProperty("to").EnumerateArray().Select(item => item.GetString()));
        var note = activity.GetProperty("object");
        Assert.Equal("membersOnly", note.GetProperty("squid:policy").GetProperty("visibility").GetString());
        Assert.Empty(_host.Sender.Calls);
    }

    [Fact] // AC-02 (private)
    public async Task Ac02_private_creates_no_delivery_at_all()
    {
        _host.Sender.Calls.Clear();
        var (owner, handle, slug) = await InboxHarness.CommunityAsync(_host, "ac02p");
        await FollowAsync(handle, Remote("ac02pf"), Remote("ac02pg"));
        await VisibilityAsync(owner, slug, "private");

        var objectId = await PublishAsync(owner, slug, "não federa em claro");

        Assert.Empty(await Store.ForObjectAsync(objectId, PeerActivity.Create));
        Assert.Empty(_host.Sender.Calls); // §7.3: private never generates a delivery
    }

    [Fact] // AC-03
    public async Task Ac03_follow_route_enqueues_a_signed_follow_and_the_relation_settles_on_accept()
    {
        _host.Sender.Calls.Clear();
        var (client, handle) = await SignableAsync("ac03");
        var origin = _host.Origin;
        var target = $"https://remote.example/users/{InboxHarness.Unique("ac03t")}";
        var body = new { targetActorUri = target };

        // Session and CSRF are the route's own gate (GAP-41): 401 without the first, 403 without
        // the second, and only the pair earns the enqueue.
        var anonymous = _host.NewClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync("/api/federation/follow", body)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await PostRoutes.SendAsync(client, HttpMethod.Post, "/api/federation/follow", body)).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await PostRoutes.SendAsync(client, HttpMethod.Post, "/api/federation/follow",
                new { targetActorUri = $"{origin}/users/{handle}" }, client.Csrf)).StatusCode);

        var accepted = await PostRoutes.SendAsync(
            client, HttpMethod.Post, "/api/federation/follow", body, client.Csrf);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        var rows = await Store.ForRecipientAsync(target);
        var follow = Assert.Single(rows);
        Assert.Equal(PeerActivity.Follow, follow.Activity);
        Assert.Equal(PeerDeliveryState.Pending, follow.State);
        using var payload = JsonDocument.Parse(follow.Payload);
        Assert.Equal("Follow", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal($"{origin}/users/{handle}", payload.RootElement.GetProperty("actor").GetString());
        Assert.Equal(target, payload.RootElement.GetProperty("object").GetString());

        // The outbound edge is born PENDING in the document the store keeps — the reading port
        // hides pending edges on purpose (they never federate), so the raw row is the proof.
        var document = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteFollowStore.FollowingStoreName,
            GrainId.Create("following", handle).ToString());
        Assert.NotNull(document);
        using var edge = JsonDocument.Parse(document!);
        var pending = Assert.Single(edge.RootElement.GetProperty("Rows").EnumerateArray());
        Assert.Equal(target, pending.GetProperty("ActorUri").GetString());
        Assert.False(pending.GetProperty("Established").GetBoolean());

        // Accept remoto → the local relation stands established (AC-03, TASK-0044's application).
        var follows = _host.App.Services.GetRequiredService<IFollowStore>();
        var (peer, peerKeyId, peerRsa) = InboxHarness.Remote(_host, "ac03peer");
        var accept = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "{{peer}}/activities/{{InboxHarness.Unique("ac03acc")}}",
              "type": "Accept",
              "actor": "{{peer}}",
              "object": { "type": "Follow", "actor": "{{origin}}/users/{{handle}}", "object": "{{target}}" }
            }
            """;
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(
                _host.Http, $"/users/{handle}/inbox", accept, peerRsa, peerKeyId)).StatusCode);
        Assert.True(Assert.Single(await follows.FollowingAsync(handle)).Established);

        // Follow remoto → 202 with the Accept owed back in the body (AC-03, TASK-0044).
        var incoming = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "{{peer}}/activities/{{InboxHarness.Unique("ac03fol")}}",
              "type": "Follow",
              "actor": "{{peer}}",
              "object": "{{origin}}/users/{{handle}}"
            }
            """;
        var answered = await InboxHarness.SendSignedAsync(
            _host.Http, $"/users/{handle}/inbox", incoming, peerRsa, peerKeyId);
        Assert.Equal(HttpStatusCode.Accepted, answered.StatusCode);
        using var owed = JsonDocument.Parse(await answered.Content.ReadAsStringAsync());
        Assert.Equal("Accept", owed.RootElement.GetProperty("type").GetString());

        // And when the queued Follow goes out it is SIGNED by the local actor (contract §5).
        var verdict = await Outbound.SendAsync(follow);
        Assert.Equal(PeerDeliveryState.Sent, verdict.State);
        var sent = Assert.Single(_host.Sender.Calls);
        Assert.Equal($"{target}/inbox", sent.Inbox);
        Assert.Contains($"keyId=\"{origin}/users/{handle}#main-key\"", sent.Signature, StringComparison.Ordinal);
        Assert.Contains("rsa-sha256", sent.Signature, StringComparison.Ordinal);
    }

    [Fact] // AC-04
    public async Task Ac04_policy_change_before_send_cancels_with_a_reason_and_sends_nothing()
    {
        _host.Sender.Calls.Clear();
        var (owner, handle, slug) = await InboxHarness.CommunityAsync(_host, "ac04");
        var follower = Remote("ac04f");
        await FollowAsync(handle, follower);
        var objectId = await PublishAsync(owner, slug, "enfileirado antes da troca de política");

        var row = Assert.Single(await Store.ForObjectAsync(objectId, PeerActivity.Create));
        Assert.Equal(PeerDeliveryState.Pending, row.State);

        // The policy changes BETWEEN enqueue and send (FR-038, §7 item 7).
        await VisibilityAsync(owner, slug, "membersOnly");

        var verdict = await Outbound.SendAsync(row);
        Assert.Equal(PeerDeliveryState.Cancelled, verdict.State);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason)); // motivo auditável
        Assert.Contains("política vigente", verdict.Reason, StringComparison.Ordinal);

        var persisted = await Store.FindAsync(row.ActivityId, row.RecipientActorId);
        Assert.NotNull(persisted);
        Assert.Equal(PeerDeliveryState.Cancelled, persisted!.State);
        Assert.Equal(verdict.Reason, persisted.Reason);

        // Nothing went out, no error was thrown, and a second send of the resolved row is a no-op.
        Assert.Empty(_host.Sender.Calls);
        var again = await Outbound.SendAsync(persisted);
        Assert.Equal(PeerDeliveryState.Cancelled, again.State);
        Assert.Empty(_host.Sender.Calls);
    }

    [Fact] // AC-06
    public async Task Ac06_removing_the_post_propagates_a_signed_delete_to_the_recipients_that_received_the_create()
    {
        _host.Sender.Calls.Clear();
        var (owner, handle, slug) = await SignableCommunityAsync("ac06");
        var origin = _host.Origin;
        var received = Remote("ac06r"); // remote follower AND active member: its Create goes out
        var skipped = Remote("ac06s"); // remote follower, never a member
        await FollowAsync(handle, received, skipped);
        await MemberAsync(slug, received, MembershipState.Active);

        var objectId = await PublishAsync(owner, slug, "some e propaga");
        Assert.Equal(2, (await Store.ForObjectAsync(objectId, PeerActivity.Create)).Count);

        // The member's Create goes out; the non-member's delivery is cancelled by the policy that
        // is now in force — so it never received anything.
        await VisibilityAsync(owner, slug, "membersOnly");
        var creates = await Store.ForObjectAsync(objectId, PeerActivity.Create);
        var denied = await Outbound.SendAsync(
            creates.Single(row => row.RecipientActorId == skipped));
        Assert.Equal(PeerDeliveryState.Cancelled, denied.State);
        var delivered = await Outbound.SendAsync(
            creates.Single(row => row.RecipientActorId == received));
        Assert.Equal(PeerDeliveryState.Sent, delivered.State);

        // Removing the local post (R-08): the Delete walks exactly the Create's recipients.
        var removed = await PostRoutes.SendAsync(
            owner, HttpMethod.Delete, $"/api/posts/{Uri.EscapeDataString(objectId)}", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        var row = Assert.Single(await Store.ForObjectAsync(objectId, PeerActivity.Delete));
        Assert.Equal(received, row.RecipientActorId);
        Assert.Equal(PeerDeliveryState.Pending, row.State);
        using var payload = JsonDocument.Parse(row.Payload);
        Assert.Equal("Delete", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal(objectId, payload.RootElement.GetProperty("object").GetString());
        Assert.Equal($"{origin}/users/{handle}", payload.RootElement.GetProperty("actor").GetString());

        var verdict = await Outbound.SendAsync(row);
        Assert.Equal(PeerDeliveryState.Sent, verdict.State);
        var sent = _host.Sender.Calls.Single(call =>
            call.Body.Contains("\"type\":\"Delete\"", StringComparison.Ordinal));
        Assert.Equal($"{received}/inbox", sent.Inbox);
        Assert.Contains($"keyId=\"{origin}/users/{handle}#main-key\"", sent.Signature, StringComparison.Ordinal);
        Assert.Contains("SHA-256=", sent.Digest, StringComparison.Ordinal);
        Assert.Equal(row.ActivityId, JsonDocument.Parse(sent.Body).RootElement.GetProperty("id").GetString());
    }

    // ---- harness --------------------------------------------------------------------------

    private IPeerDeliveryStore Store =>
        _host.App.Services.GetRequiredService<IPeerDeliveryStore>();

    private IOutboundPipeline Outbound =>
        _host.App.Services.GetRequiredService<IOutboundPipeline>();

    private static string Remote(string prefix) =>
        $"https://remote.example/users/{InboxHarness.Unique(prefix)}";

    private async Task FollowAsync(string handle, params string[] followers)
    {
        var follows = _host.App.Services.GetRequiredService<IFollowStore>();
        foreach (var follower in followers)
        {
            await follows.AddFollowerAsync(handle, follower, DateTimeOffset.UtcNow);
        }
    }

    private async Task MemberAsync(string slug, string actorUri, MembershipState state)
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

    private async Task<string> PublishAsync(AuthClient client, string slug, string content)
    {
        var response = await PostRoutes.SendAsync(
            client, HttpMethod.Post, $"/api/containers/{slug}/posts", new { content }, client.Csrf);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private async Task VisibilityAsync(AuthClient owner, string slug, string visibility)
    {
        var response = await PostRoutes.SendAsync(
            owner, HttpMethod.Put, $"/api/containers/{slug}/policy", new { visibility },
            owner.Csrf, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// An account that can SIGN: HTTP registration mints a fresh per-actor RK the boot
    /// <c>Squid:RecoveryKey</c> cannot open, so the actor is written exactly as
    /// <c>FederationRuntimeKeyTests</c> writes it — material sealed with the runtime key — and the
    /// credential row is created straight, which is all a login needs.
    /// </summary>
    private async Task<(AuthClient Client, string Handle)> SignableAsync(string prefix)
    {
        var handle = PostRoutes.Handle(prefix);
        var keys = KeyState.Create(_host.RuntimeKey, PostRoutes.Password);
        var material = new ActorKeyMaterial(
            [
                new ActorPublicKey("RSA-2048", "#main-key", Convert.ToBase64String(keys.RsaPublicKey)),
                new ActorPublicKey("Ed25519", "#ed25519", Convert.ToBase64String(keys.Ed25519PublicKey)),
            ],
            Convert.ToBase64String(keys.WrappedRsaPrivateKey));
        await new ActorGrain(
            new ActorStore($"Data Source={_host.StoragePath};Pooling=False"))
            .CreateAsync(handle, "Saída federada", material);

        await _host.App.Services.GetRequiredService<IAuthStore>().CreateAsync(
            handle, new AuthAccount { PasswordHash = PasswordHasher.Hash(PostRoutes.Password) });

        var client = _host.NewClient();
        var login = await client.LoginAsync(handle, PostRoutes.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (client, handle);
    }

    /// <summary>The signable account with its own community container — the AC-06 scenario.</summary>
    private async Task<(AuthClient Client, string Handle, string Slug)> SignableCommunityAsync(string prefix)
    {
        var (client, handle) = await SignableAsync(prefix);
        var slug = PostRoutes.Slug(prefix);
        var created = await PostRoutes.SendAsync(client, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Comunidade", slug, summary = "saída de teste" },
            client.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (client, handle, slug);
    }
}
