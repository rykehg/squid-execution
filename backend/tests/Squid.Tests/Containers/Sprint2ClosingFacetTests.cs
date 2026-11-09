using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Auth;
using Squid.Domain;
using Squid.Domain.Containers;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>
/// AC-04 of TASK-0022 — the Sprint 2 closing facet end to end, on the routes the previous tasks
/// delivered: (a) the invite-only group that admits nobody in a hundred attempts and then walks
/// its invited actor in; (b) the <c>singlePublisher</c> gate decided by the host's own evaluator
/// with the actor context of section F over the rows the routes just wrote (there is no post
/// route in this phase to drive it through); (c) the succession claim and the operator override
/// that both restore an active Owner, so no command in the sequence leaves an orphan.
/// </summary>
[Collection("ServiceHost")]
public sealed class Sprint2ClosingFacetTests : IClassFixture<SuccessionHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly SuccessionHostFixture _host;

    public Sprint2ClosingFacetTests(SuccessionHostFixture host) => _host = host;

    [Fact] // AC-04 (a)
    public async Task The_invite_only_group_admits_nobody_in_a_hundred_attempts_and_then_its_invited_actor()
    {
        var (owner, ownerHandle) = await SignedInAsync("inv");
        var slug = Slug("por-convite");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "So por convite", slug }, owner.Csrf)).StatusCode);

        // 100 attempts without an invitation: every one is the 404 of an absent slug (FR-018,
        // SC-006) — zero successes, and the member page still holds only the owner.
        var attempts = 0;
        var successes = 0;
        for (var actor = 0; actor < 10; actor++)
        {
            var (client, _) = await SignedInAsync($"inv{actor}");
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var refused = await SendAsync(
                    client, HttpMethod.Post, $"/api/containers/{slug}/join", null, client.Csrf);
                attempts++;
                if (refused.IsSuccessStatusCode) successes++;
                Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            }
        }

        Assert.Equal(100, attempts);
        Assert.Equal(0, successes);
        Assert.Equal(new[] { ownerHandle }, Handles(await MembersAsync(owner, slug)));

        // The invitation is the ticket of GAP-13: the named actor alone walks in, Active.
        var (guest, guestHandle) = await SignedInAsync("invguest");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/invites", new { handle = guestHandle }, owner.Csrf)).StatusCode);

        var walkIn = await SendAsync(guest, HttpMethod.Post, $"/api/containers/{slug}/join", null, guest.Csrf);
        Assert.Equal(HttpStatusCode.OK, walkIn.StatusCode);
        using (var json = JsonDocument.Parse(await walkIn.Content.ReadAsStringAsync()))
        {
            Assert.Equal("active", json.RootElement.GetProperty("state").GetString());
            Assert.Equal("member", json.RootElement.GetProperty("role").GetString());
        }

        Assert.Contains(guestHandle, Handles(await MembersAsync(owner, slug)));
    }

    [Fact] // AC-04 (b)
    public async Task The_single_publisher_gate_denies_a_plain_member_and_admits_the_named_active_publisher()
    {
        var (owner, ownerHandle) = await SignedInAsync("pubo");
        var slug = Slug("editorial");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Editorial", slug }, owner.Csrf)).StatusCode);

        var (plain, plainHandle) = await SignedInAsync("pubp");
        var (publisher, publisherHandle) = await SignedInAsync("pubb");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(plain, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, plain.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(publisher, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, publisher.Csrf)).StatusCode);

        // The policy the route writes: singlePublisher naming the second member, version 1 → 2.
        var put = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { posting = "singlePublisher", publisherIds = new[] { ActorOf(publisherHandle) } },
            owner.Csrf, ifMatch: "1");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // It is the policy on the wire — this is what the evaluator below is handed.
        var read = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");
        using (var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync()))
        {
            var policy = json.RootElement.GetProperty("policy");
            Assert.Equal("singlePublisher", policy.GetProperty("posting").GetString());
            Assert.Equal(2, policy.GetProperty("version").GetInt32());
            Assert.Equal(ActorOf(publisherHandle), policy.GetProperty("publisherIds")[0].GetString());
        }

        // The rows those routes wrote, evaluated by the host's own IPolicyEvaluator with the
        // actor context of section F: role + membership state + the seat the policy names.
        var container = (await Store().FindAsync(slug))!.Container;
        var rows = await Store().ListMembershipsAsync(slug);
        var evaluator = _host.App.Services.GetRequiredService<IPolicyEvaluator>();

        var plainMember = evaluator.Evaluate(PolicyAction.Post,
            ContextOf(rows.Single(row => row.Handle == plainHandle), container), container.Policy);
        Assert.False(plainMember.Permitted);
        Assert.Contains("SinglePublisher", plainMember.Reason, StringComparison.Ordinal);
        Assert.Equal(2, plainMember.PolicyVersion);

        var named = evaluator.Evaluate(PolicyAction.Post,
            ContextOf(rows.Single(row => row.Handle == publisherHandle), container), container.Policy);
        Assert.True(named.Permitted);
        Assert.Contains("publisher", named.Reason, StringComparison.OrdinalIgnoreCase);

        // The role alone never opens the gate: the Owner is active, just not the named publisher.
        var ownerDecision = evaluator.Evaluate(PolicyAction.Post,
            ContextOf(rows.Single(row => row.Handle == ownerHandle), container), container.Policy);
        Assert.False(ownerDecision.Permitted);
    }

    [Fact] // AC-04 (c)
    public async Task The_succession_claim_and_the_operator_override_always_leave_an_active_owner()
    {
        var (owner, ownerHandle) = await SignedInAsync("own");
        var slug = Slug("sem-orfao");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Sem orfao", slug }, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug); // born with its owner

        var (heir, heirHandle) = await SignedInAsync("heir");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/successor", new { handle = heirHandle }, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // Owner inactive is what the claim waits out: inside the window it is the 403 of §9.12
        // and the rows do not move.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/claim", null, heir.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        _host.Clock.AdvanceDays(91);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/claim", null, heir.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);
        Assert.Equal(MembershipRole.Owner,
            (await Store().FindMembershipAsync(slug, heirHandle))!.Role);

        // The refusal of §9.12 changes nothing: no justification, no move, no orphan.
        var operatorClient = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await operatorClient.RegisterAsync(
            SuccessionHostFixture.OperatorHandle, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer", new { handle = ownerHandle },
            operatorClient.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // The operator override is the other half of the sentence: forced, justified, audited —
        // and the container has an active Owner after it like after every command before it.
        var forced = await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = ownerHandle, justification = "dono inativo ha meses" }, operatorClient.Csrf);
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        await NeverOrphanedAsync(slug);
        Assert.Equal(MembershipRole.Owner,
            (await Store().FindMembershipAsync(slug, ownerHandle))!.Role);
        Assert.Equal(ownerHandle, OwnerOf(await Store().ListMembershipsAsync(slug)));
    }

    /// <summary>The section F actor context: role and state of the row, seat only while active.</summary>
    private static PolicyActor ContextOf(ContainerMembership row, Container container) => new(
        row.State != MembershipState.Active
            ? ContainerRole.Guest
            : row.Role switch
            {
                MembershipRole.Owner => ContainerRole.Owner,
                MembershipRole.Admin => ContainerRole.Admin,
                MembershipRole.Moderator => ContainerRole.Moderator,
                _ => ContainerRole.Member,
            },
        row.State,
        row.State == MembershipState.Active
            && container.Policy.PublisherIds.Contains(row.ActorId, StringComparer.Ordinal));

    /// <summary>The DoD invariant of section G: exactly one active Owner, named by the aggregate.</summary>
    private async Task NeverOrphanedAsync(string slug)
    {
        var container = (await Store().FindAsync(slug))!.Container;
        var owners = (await Store().ListMembershipsAsync(slug))
            .Where(row => row.Role == MembershipRole.Owner && row.State == MembershipState.Active)
            .ToList();

        Assert.Single(owners);
        Assert.Equal(owners[0].ActorId, container.OwnerActorId);
    }

    private static string OwnerOf(IReadOnlyList<ContainerMembership> rows) =>
        rows.Single(row => row.Role == MembershipRole.Owner && row.State == MembershipState.Active).Handle;

    private async Task<(AuthClient Client, string Handle)> SignedInAsync(string prefix)
    {
        var handle = Handle(prefix);
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);
        return (client, handle);
    }

    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client,
        HttpMethod method,
        string path,
        object? body = null,
        string? csrf = null,
        string? ifMatch = null) =>
        client.SendAsync(method, path, body, request =>
        {
            if (csrf is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }

            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }
        });

    /// <summary>The member page through the route: whoever can see the container reads it here.</summary>
    private async Task<List<JsonElement>> MembersAsync(AuthClient client, string slug)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/containers/{slug}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.Clone()).ToList();
    }

    private static string[] Handles(IEnumerable<JsonElement> items) =>
        items.Select(item => item.GetProperty("handle").GetString()!).ToArray();

    private SqliteContainerStore Store()
    {
        var connection = $"Data Source={_host.StoragePath};Pooling=False";
        return new SqliteContainerStore(new SqliteGrainStorage(connection), connection);
    }

    private string ActorOf(string handle) =>
        $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress!.Authority}/users/{handle}";

    private static string Handle(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    private static string Slug(string prefix) =>
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
