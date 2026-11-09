using System.Net;
using System.Text.Json;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Domain.Containers;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>
/// QA-0018 (TASK-0019): the three DEV decisions of this task that no DEV test asserted —
/// the member page's paging contract (default 100, ceiling 1000, body <c>{ items }</c> and
/// nothing else), the §3.3 refusal of a join by a <c>Silenced</c>/<c>Banned</c> row at the
/// route itself, and the trail: <c>Join</c>/<c>Invite</c> Allow and Deny reach it while
/// <c>leave</c> adds no policy decision of its own. AC-01..AC-04 and AC-07 stay with the DEV
/// tests this QA executed unchanged; AC-05's filter half stays with
/// <see cref="MembershipRoutesTests"/>.
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0018MembershipPageAndTrailTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public Qa0018MembershipPageAndTrailTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-05 — paging contract: default 100, ceiling 1000, `{ items }`
    public async Task The_member_page_defaults_to_100_caps_at_1000_and_walks_every_row_exactly_once()
    {
        var (owner, ownerHandle) = await SignedInAsync("qa18pg");
        var slug = Slug("paging");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Pagina QA18", slug }, owner.Csrf)).StatusCode);

        // 1000 more Active rows straight through the store of the slice: 1001 rows in total,
        // the smallest page set in which "capped at 1000" and "no ceiling" are different answers.
        var store = Store();
        var seeded = new string[1000];
        for (var i = 0; i < seeded.Length; i++)
        {
            seeded[i] = Handle("qaseed");
            await store.UpsertMembershipAsync(new ContainerMembership
            {
                Slug = slug,
                ActorId = $"{Origin()}/users/{seeded[i]}",
                Handle = seeded[i],
                Role = MembershipRole.Member,
                State = MembershipState.Active,
                JoinedAt = DateTimeOffset.UtcNow,
            });
        }

        var expected = seeded.ToHashSet();
        expected.Add(ownerHandle); // 1001: the owner born with the container

        var anonymous = _host.NewClient();

        // Default page: exactly 100 rows, and the body is `{ items }` and nothing else.
        var def = await SendAsync(anonymous, HttpMethod.Get, $"/api/containers/{slug}/members");
        Assert.Equal(HttpStatusCode.OK, def.StatusCode);
        using var defJson = JsonDocument.Parse(await def.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "items" }, defJson.RootElement.EnumerateObject().Select(property => property.Name));
        var page = Handles(Items(defJson.RootElement));
        Assert.Equal(100, page.Length);

        // Ceiling: limit=5000 is clamped to 1000 — 200 with 1000 rows, neither refused nor 5000.
        var capped = Handles(await PageAsync(anonymous, slug, "?limit=5000"));
        Assert.Equal(1000, capped.Length);

        // The same walk finishes at offset 1000: one row left, none lost and none repeated.
        var tail = Handles(await PageAsync(anonymous, slug, "?limit=1000&offset=1000"));
        Assert.Single(tail);

        var walked = capped.Concat(tail).ToArray();
        Assert.Equal(walked.Length, walked.Distinct().Count());
        Assert.True(expected.SetEquals(walked), "the walked pages must be exactly the 1001 members");
        Assert.Equal(capped.Take(page.Length), page); // the default page is the prefix of the same order
    }

    [Fact] // §3.3 — a join never moves Silenced or Banned (state machine of section E)
    public async Task A_silenced_or_banned_actor_is_refused_by_join_and_the_row_never_moves()
    {
        var (owner, _) = await SignedInAsync("qa18sb");
        var slug = Slug("modera");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Moderacao QA18", slug }, owner.Csrf)).StatusCode);

        var (banned, bannedHandle) = await SignedInAsync("qa18b");
        var (silenced, silencedHandle) = await SignedInAsync("qa18s");
        await SeedAsync(slug, bannedHandle, MembershipState.Banned);
        await SeedAsync(slug, silencedHandle, MembershipState.Silenced);

        // Visible (public) and entry allowed by policy: the refusal can only come from §3.3.
        var byBanned = await SendAsync(banned, HttpMethod.Post, $"/api/containers/{slug}/join", null, banned.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, byBanned.StatusCode);
        Assert.Contains("policy_denied", await byBanned.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var bySilenced = await SendAsync(silenced, HttpMethod.Post, $"/api/containers/{slug}/join", null, silenced.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, bySilenced.StatusCode);
        Assert.Contains("policy_denied", await bySilenced.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The rows are exactly where §3.3 left them — read from the store, not from the page.
        var store = Store();
        Assert.Equal(MembershipState.Banned, (await store.FindMembershipAsync(slug, bannedHandle))!.State);
        Assert.Equal(MembershipState.Silenced, (await store.FindMembershipAsync(slug, silencedHandle))!.State);

        // And neither row is on anyone's page: Banned never, Silenced out of this page (§F).
        var adminPage = Handles(await MembersAsync(owner, slug));
        Assert.DoesNotContain(bannedHandle, adminPage);
        Assert.DoesNotContain(silencedHandle, adminPage);
    }

    [Fact] // trail decision of TASK-0019: Join/Invite Allow + Deny audited, leave evaluates nothing
    public async Task Join_and_invite_allow_and_deny_reach_the_trail_and_leave_writes_no_decision()
    {
        var (owner, _) = await SignedInAsync("qa18tr");
        var slug = Slug("trilha");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Trilha QA18", slug }, owner.Csrf)).StatusCode);
        await SetJoinAsync(owner, slug, "invite", 1); // public + invite: visible, entry by invitation

        var (guest, guestHandle) = await SignedInAsync("qa18g");
        var (outsider, outsiderHandle) = await SignedInAsync("qa18o");

        // Invite Allow (Admin+) and Invite Deny (a session with no role at all).
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/invites", new { handle = guestHandle }, owner.Csrf)).StatusCode);
        var inviteDenied = await SendAsync(outsider, HttpMethod.Post, $"/api/containers/{slug}/invites",
            new { handle = outsiderHandle }, outsider.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, inviteDenied.StatusCode);

        // Join Allow (the invited guest walks in) and Join Deny (the outsider has no invitation).
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(guest, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, guest.Csrf)).StatusCode);
        var joinDenied = await SendAsync(outsider, HttpMethod.Post, $"/api/containers/{slug}/join", null, outsider.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, joinDenied.StatusCode);

        var trail = new SqlitePolicyAuditStore(
            new SqliteGrainStorage($"Data Source={_host.StoragePath};Pooling=False"));
        var before = await RowsAsync(trail, slug);
        Assert.Contains(before, entry => entry.Action == PolicyAudit.Invite && entry.Result == PolicyAudit.Allow);
        Assert.Contains(before, entry => entry.Action == PolicyAudit.Invite && entry.Result == PolicyAudit.Deny);
        Assert.Contains(before, entry => entry.Action == PolicyAudit.Join && entry.Result == PolicyAudit.Allow);
        Assert.Contains(before, entry => entry.Action == PolicyAudit.Join && entry.Result == PolicyAudit.Deny);

        // The member leaves: the only row this route may add is the visibility decision it reads.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(guest, HttpMethod.Post,
            $"/api/containers/{slug}/leave", null, guest.Csrf)).StatusCode);

        var after = await RowsAsync(trail, slug);
        Assert.Equal(before.Count(entry => entry.Action == PolicyAudit.Join),
            after.Count(entry => entry.Action == PolicyAudit.Join));
        Assert.Equal(before.Count(entry => entry.Action == PolicyAudit.Invite),
            after.Count(entry => entry.Action == PolicyAudit.Invite));
        Assert.All(after, entry => Assert.True(
            entry.Action is PolicyAudit.View or PolicyAudit.ChangePolicy
                or PolicyAudit.Join or PolicyAudit.Invite,
            $"unexpected action in the trail: {entry.Action}"));
        Assert.DoesNotContain(after, entry => entry.Action == "Leave");
    }

    /// <summary>Every trail row of one container resource, newest first (contract §7 fields).</summary>
    private static async Task<List<PolicyAuditEntry>> RowsAsync(IPolicyAuditStore trail, string slug)
    {
        var rows = await trail.QueryAsync(new PolicyAuditFilter(Limit: 1000));
        return rows
            .Where(entry => entry.ResourceId.EndsWith($"/c/{slug}", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Signs a fresh account in: the cookie jar of this test's own client.</summary>
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

    /// <summary>Replaces the join policy with the contract's word and returns the new version.</summary>
    private async Task<int> SetJoinAsync(AuthClient owner, string slug, string join, int version)
    {
        var response = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { join }, owner.Csrf, version.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("policy").GetProperty("version").GetInt32();
    }

    private async Task<JsonElement[]> PageAsync(AuthClient client, string slug, string query)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/containers/{slug}/members{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Items(json.RootElement);
    }

    private static JsonElement[] Items(JsonElement root)
    {
        var items = new List<JsonElement>();
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            items.Add(item.Clone());
        }

        return items.ToArray();
    }

    private static string[] Handles(IEnumerable<JsonElement> items) =>
        items.Select(item => item.GetProperty("handle").GetString()!).ToArray();

    private async Task<List<JsonElement>> MembersAsync(AuthClient client, string slug)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/containers/{slug}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Items(json.RootElement).ToList();
    }

    private SqliteContainerStore Store()
    {
        var connection = $"Data Source={_host.StoragePath};Pooling=False";
        return new(new SqliteGrainStorage(connection), connection);
    }

    /// <summary>A membership no route can write yet, inserted through the store of the slice.</summary>
    private async Task SeedAsync(string slug, string handle, MembershipState state)
    {
        await Store().UpsertMembershipAsync(new ContainerMembership
        {
            Slug = slug,
            ActorId = $"{Origin()}/users/{handle}",
            Handle = handle,
            Role = MembershipRole.Member,
            State = state,
            JoinedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>The origin the host itself is reached at — the rule of AC-07, scheme + Host.</summary>
    private string Origin() =>
        $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress!.Authority}";

    private static string Handle(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    private static string Slug(string prefix) =>
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
