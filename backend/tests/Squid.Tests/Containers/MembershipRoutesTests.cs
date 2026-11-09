using System.Net;
using System.Text.Json;
using Squid.Api.Auth;
using Squid.Domain.Containers;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>
/// AC-01..AC-05 of TASK-0019 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// entry per join policy and its refusals, SC-006's 100 attempts included (AC-01/AC-02), the
/// invitation addressed at one target actor (AC-03, GAP-13), leave with the 409 of the last owner
/// and the re-entry that follows the policy in force (AC-04, §9.11), and the member page filtered
/// by the reader and walked page by page (AC-05). Moderation has no route until TASK-0020, so the
/// Banned and Silenced rows are seeded straight into the host's own SQLite file through the store
/// of the slice — the same way the audit suite seeds a member.
/// </summary>
[Collection("ServiceHost")]
public sealed class MembershipRoutesTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public MembershipRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Joining_an_open_community_creates_an_active_member_that_shows_up_in_the_listing()
    {
        var (owner, _) = await SignedInAsync("ac01");
        var slug = Slug("comunidade");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Comunidade", slug }, owner.Csrf)).StatusCode);

        var (joiner, joinerHandle) = await SignedInAsync("ac01j");
        var joined = await SendAsync(joiner, HttpMethod.Post, $"/api/containers/{slug}/join", null, joiner.Csrf);

        Assert.Equal(HttpStatusCode.Created, joined.StatusCode);
        using var json = JsonDocument.Parse(await joined.Content.ReadAsStringAsync());
        var member = json.RootElement;
        Assert.Equal("active", member.GetProperty("state").GetString());
        Assert.Equal("member", member.GetProperty("role").GetString());
        Assert.Equal(joinerHandle, member.GetProperty("handle").GetString());
        Assert.Equal($"{Origin()}/users/{joinerHandle}", member.GetProperty("actorId").GetString());

        // The actor is on the page whoever can see the container looks at it.
        var listed = (await MembersAsync(owner, slug))
            .Single(item => item.GetProperty("handle").GetString() == joinerHandle);
        Assert.Equal("active", listed.GetProperty("state").GetString());
        Assert.Equal("member", listed.GetProperty("role").GetString());

        // data-model §2.2: the derived counter moved with the write (owner + joiner).
        Assert.Equal(2, await MemberCountAsync(owner, slug));
    }

    [Fact] // AC-02
    public async Task Entry_follows_the_policy_and_a_container_out_of_sight_answers_the_404_of_an_absent_slug()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac02");
        var group = Slug("grupo");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Grupo", slug = group }, owner.Csrf)).StatusCode);

        // MembersOnly + Invite: the stranger cannot even see it — join answers like an absent slug (FR-018).
        var (stranger, _) = await SignedInAsync("ac02s");
        var invisible = await SendAsync(stranger, HttpMethod.Post, $"/api/containers/{group}/join", null, stranger.Csrf);
        var absent = await SendAsync(stranger, HttpMethod.Post, "/api/containers/que-nao-existe/join", null, stranger.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await invisible.Content.ReadAsStringAsync(), await absent.Content.ReadAsStringAsync());

        var community = Slug("aberta");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Aberta", slug = community }, owner.Csrf)).StatusCode);

        // Request: the request lands as Requested and the actor does not enter.
        var version = await SetJoinAsync(owner, community, "request", 1);
        var (requester, requesterHandle) = await SignedInAsync("ac02r");
        var requested = await SendAsync(requester, HttpMethod.Post, $"/api/containers/{community}/join", null, requester.Csrf);
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        using (var json = JsonDocument.Parse(await requested.Content.ReadAsStringAsync()))
        {
            Assert.Equal("requested", json.RootElement.GetProperty("state").GetString());
        }

        var anonymous = _host.NewClient();
        Assert.DoesNotContain(requesterHandle, Handles(await MembersAsync(anonymous, community)));
        Assert.Contains(ownerHandle, Handles(await MembersAsync(anonymous, community)));

        // Closed: denies always — visible, so the refusal is 403 policy_denied, not a 404.
        version = await SetJoinAsync(owner, community, "closed", version);
        var closed = await SendAsync(stranger, HttpMethod.Post, $"/api/containers/{community}/join", null, stranger.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
        Assert.Contains("policy_denied", await closed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Invite without an invitation: 0 successes in 100 attempts (SC-006), nothing written.
        await SetJoinAsync(owner, community, "invite", version);
        var attempts = 0;
        for (var actor = 0; actor < 10; actor++)
        {
            var (client, _) = await SignedInAsync($"ac02i{actor}");
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var denied = await SendAsync(client, HttpMethod.Post, $"/api/containers/{community}/join", null, client.Csrf);
                attempts++;
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
        }

        Assert.Equal(100, attempts);
        Assert.Equal(new[] { ownerHandle }, Handles(await MembersAsync(anonymous, community)));
    }

    [Fact] // AC-03
    public async Task An_admin_invites_one_target_actor_and_only_that_actor_walks_in()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac03");
        var slug = Slug("convite");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Convite", slug }, owner.Csrf)).StatusCode);
        await SetJoinAsync(owner, slug, "invite", 1); // public + invite: visible, entry by invitation only

        // Inviting is an Admin+ capability: a member without the role is refused by the evaluator.
        var (outsider, outsiderHandle) = await SignedInAsync("ac03o");
        var deniedInvite = await SendAsync(outsider, HttpMethod.Post, $"/api/containers/{slug}/invites",
            new { handle = outsiderHandle }, outsider.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, deniedInvite.StatusCode);

        // The body must name a valid local handle: a malformed target writes nothing (400).
        var malformed = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/invites",
            new { handle = "FORA DA REGRA!" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Contains("validation_failed", await malformed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var (guest, guestHandle) = await SignedInAsync("ac03g");
        var (guest2, guest2Handle) = await SignedInAsync("ac03g2");

        // The invitation is the state Invited on the target's own row, carrying who invited (GAP-13).
        var invite = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/invites",
            new { handle = guestHandle }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        using (var json = JsonDocument.Parse(await invite.Content.ReadAsStringAsync()))
        {
            var invited = json.RootElement;
            Assert.Equal("invited", invited.GetProperty("state").GetString());
            Assert.Equal("member", invited.GetProperty("role").GetString());
            Assert.Equal($"{Origin()}/users/{ownerHandle}", invited.GetProperty("invitedBy").GetString());
        }

        // The same target addressed by its canonical actor id — one identity, two spellings.
        var byActorId = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/invites",
            new { actorId = $"{Origin()}/users/{guest2Handle}" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, byActorId.StatusCode);

        // The invited guest joins: Invited → Active, invitation record kept on the row.
        var walkIn = await SendAsync(guest, HttpMethod.Post, $"/api/containers/{slug}/join", null, guest.Csrf);
        Assert.Equal(HttpStatusCode.OK, walkIn.StatusCode);
        using (var json = JsonDocument.Parse(await walkIn.Content.ReadAsStringAsync()))
        {
            Assert.Equal("active", json.RootElement.GetProperty("state").GetString());
            Assert.Equal($"{Origin()}/users/{ownerHandle}", json.RootElement.GetProperty("invitedBy").GetString());
        }

        var second = await SendAsync(guest2, HttpMethod.Post, $"/api/containers/{slug}/join", null, guest2.Csrf);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Another actor without an invitation keeps being denied — the container is public, so 403.
        var denied = await SendAsync(outsider, HttpMethod.Post, $"/api/containers/{slug}/join", null, outsider.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var handles = Handles(await MembersAsync(owner, slug));
        Assert.Contains(guestHandle, handles);
        Assert.Contains(guest2Handle, handles);
        Assert.DoesNotContain(outsiderHandle, handles);
    }

    [Fact] // AC-04
    public async Task Leave_moves_active_to_left_the_last_owner_conflicts_and_reentry_follows_the_policy_in_force()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac04");
        var slug = Slug("saida");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Saida", slug }, owner.Csrf)).StatusCode);

        // The last (the only) Owner never leaves: 409 last_owner, state untouched (§9.11).
        var conflict = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/leave", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("last_owner", await conflict.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var ownerRow = (await MembersAsync(owner, slug))
            .Single(item => item.GetProperty("handle").GetString() == ownerHandle);
        Assert.Equal("active", ownerRow.GetProperty("state").GetString());

        // A member leaves: Active → Left, off the page, and the counter follows (data-model §2.2).
        var (member, memberHandle) = await SignedInAsync("ac04m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);

        var left = await SendAsync(member, HttpMethod.Post, $"/api/containers/{slug}/leave", null, member.Csrf);
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        using (var json = JsonDocument.Parse(await left.Content.ReadAsStringAsync()))
        {
            Assert.Equal("left", json.RootElement.GetProperty("state").GetString());
        }

        var anonymous = _host.NewClient();
        Assert.DoesNotContain(memberHandle, Handles(await MembersAsync(anonymous, slug)));
        Assert.Equal(1, await MemberCountAsync(anonymous, slug));

        // Re-entry follows the policy in force: Open lets them straight back in.
        var rejoined = await SendAsync(member, HttpMethod.Post, $"/api/containers/{slug}/join", null, member.Csrf);
        Assert.Equal(HttpStatusCode.OK, rejoined.StatusCode);
        using (var json = JsonDocument.Parse(await rejoined.Content.ReadAsStringAsync()))
        {
            Assert.Equal("active", json.RootElement.GetProperty("state").GetString());
        }

        Assert.Contains(memberHandle, Handles(await MembersAsync(anonymous, slug)));
        Assert.Equal(2, await MemberCountAsync(anonymous, slug));

        // The policy turns to Invite: after Left, entry needs a NEW invitation (still visible → 403).
        await SetJoinAsync(owner, slug, "invite", 1);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/leave", null, member.Csrf)).StatusCode);
        var withoutInvite = await SendAsync(member, HttpMethod.Post, $"/api/containers/{slug}/join", null, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, withoutInvite.StatusCode);

        // The new invitation is the entry ticket again.
        var reinvited = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/invites",
            new { handle = memberHandle }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, reinvited.StatusCode); // the row exists: a transition, not a creation
        var back = await SendAsync(member, HttpMethod.Post, $"/api/containers/{slug}/join", null, member.Csrf);
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        using (var json = JsonDocument.Parse(await back.Content.ReadAsStringAsync()))
        {
            Assert.Equal("active", json.RootElement.GetProperty("state").GetString());
        }
    }

    [Fact] // AC-05
    public async Task The_member_page_filters_by_the_reader_and_pages_without_repeating_or_losing()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac05");
        var slug = Slug("pagina");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Pagina", slug }, owner.Csrf)).StatusCode);

        var (m1, m1Handle) = await SignedInAsync("ac05m1");
        var (m2, m2Handle) = await SignedInAsync("ac05m2");
        var (lefty, leftyHandle) = await SignedInAsync("ac05l");
        foreach (var (client, _) in new[] { (m1, m1Handle), (m2, m2Handle), (lefty, leftyHandle) })
        {
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post,
                $"/api/containers/{slug}/join", null, client.Csrf)).StatusCode);
        }

        // A former member: the row exists with state Left — off the page for everyone.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(lefty, HttpMethod.Post,
            $"/api/containers/{slug}/leave", null, lefty.Csrf)).StatusCode);

        // A pending invitation and a pending request: Admin+ only.
        var (invitee, inviteeHandle) = await SignedInAsync("ac05i");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/invites", new { handle = inviteeHandle }, owner.Csrf)).StatusCode);

        await SetJoinAsync(owner, slug, "request", 1);
        var (requester, requesterHandle) = await SignedInAsync("ac05r");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(requester, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, requester.Csrf)).StatusCode);

        // Banned and Silenced have no route yet: seeded through the store of the slice.
        var bannedHandle = Handle("seedb");
        var silencedHandle = Handle("seeds");
        await SeedAsync(slug, bannedHandle, MembershipState.Banned);
        await SeedAsync(slug, silencedHandle, MembershipState.Silenced);

        // Whoever can see the container sees exactly who is active — pending, left, silenced and
        // banned (Banned never) stay off this page.
        var anonymous = _host.NewClient();
        Assert.Equal(new[] { ownerHandle, m1Handle, m2Handle }, Handles(await MembersAsync(anonymous, slug)));

        // A plain member reads the very same page: pending states are Admin+ only.
        Assert.Equal(
            Handles(await MembersAsync(anonymous, slug)),
            Handles(await MembersAsync(m1, slug)));

        // Admin+ additionally sees the pending states, and still never Banned, Silenced or Left.
        var adminPage = Handles(await MembersAsync(owner, slug));
        Assert.Equal(
            new[] { ownerHandle, m1Handle, m2Handle, inviteeHandle, requesterHandle },
            adminPage);
        Assert.DoesNotContain(bannedHandle, adminPage);
        Assert.DoesNotContain(silencedHandle, adminPage);
        Assert.DoesNotContain(leftyHandle, adminPage);

        // Pagination over that same page: limit/offset walk, no repeated and no lost item.
        var first = Handles(await MembersAsync(owner, slug, "?limit=2"));
        var second = Handles(await MembersAsync(owner, slug, "?limit=2&offset=2"));
        var third = Handles(await MembersAsync(owner, slug, "?limit=2&offset=4"));
        var past = Handles(await MembersAsync(owner, slug, "?limit=2&offset=6"));
        Assert.Equal(new[] { ownerHandle, m1Handle }, first);
        Assert.Equal(new[] { m2Handle, inviteeHandle }, second);
        Assert.Equal(new[] { requesterHandle }, third);
        Assert.Empty(past);
        Assert.Equal(adminPage, first.Concat(second).Concat(third).ToArray());

        // A malformed page is refused before it reads anything.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, HttpMethod.Get,
            $"/api/containers/{slug}/members?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, HttpMethod.Get,
            $"/api/containers/{slug}/members?offset=x")).StatusCode);

        // And an unseen container answers the 404 of an absent slug here too (FR-018).
        var group = Slug("oculto");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Oculto", slug = group }, owner.Csrf)).StatusCode);
        var hidden = await SendAsync(m1, HttpMethod.Get, $"/api/containers/{group}/members");
        var noSuch = await SendAsync(m1, HttpMethod.Get, "/api/containers/nao-existe/members");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noSuch.StatusCode);
        Assert.Equal(await hidden.Content.ReadAsStringAsync(), await noSuch.Content.ReadAsStringAsync());
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
            new { join }, owner.Csrf, ifMatch: version.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("policy").GetProperty("version").GetInt32();
    }

    private async Task<List<JsonElement>> MembersAsync(AuthClient client, string slug, string query = "")
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/containers/{slug}/members{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static string[] Handles(IEnumerable<JsonElement> items) =>
        items.Select(item => item.GetProperty("handle").GetString()!).ToArray();

    private async Task<long> MemberCountAsync(AuthClient client, string slug)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("memberCount").GetInt64();
    }

    /// <summary>
    /// A membership no route can write yet (moderation is TASK-0020), inserted through the store
    /// of the slice straight into the host's own SQLite file.
    /// </summary>
    private async Task SeedAsync(string slug, string handle, MembershipState state)
    {
        var connection = $"Data Source={_host.StoragePath};Pooling=False";
        await new SqliteContainerStore(new SqliteGrainStorage(connection), connection)
            .UpsertMembershipAsync(new ContainerMembership
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
