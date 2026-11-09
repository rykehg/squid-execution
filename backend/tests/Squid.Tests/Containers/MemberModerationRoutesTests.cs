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
/// AC-01..AC-05 of TASK-0020 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// the audited role change with author, when and policy version (AC-01), the §3.3 moderation
/// verbs with their justifications and Owner invariants (AC-02), Silenced reading a members-only
/// container while its writes are denied and Banned reading a public one as the 404 of an absent
/// slug (AC-03), and the 401 of a sessionless PATCH beside the Moderator refused at papéis
/// (AC-05). AC-04's singlePublisher facet is the evaluator's own contract — there is no post
/// route in this phase to drive it over HTTP — and lives in the Domain suite.
/// </summary>
[Collection("ServiceHost")]
public sealed class MemberModerationRoutesTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public MemberModerationRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task An_admin_changes_a_role_and_the_trail_records_author_when_and_policy_version()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac01");
        var slug = Slug("papel");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Papel", slug }, owner.Csrf)).StatusCode);

        var (joiner, joinerHandle) = await SignedInAsync("ac01j");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(joiner, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, joiner.Csrf)).StatusCode);

        // Admin+ (the Owner is above Admin): Member → Moderator, answered with the new row.
        var patch = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{joinerHandle}", new { role = "moderator" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using (var json = JsonDocument.Parse(await patch.Content.ReadAsStringAsync()))
        {
            var member = json.RootElement;
            Assert.Equal("moderator", member.GetProperty("role").GetString());
            Assert.Equal("active", member.GetProperty("state").GetString());
            Assert.Equal(joinerHandle, member.GetProperty("handle").GetString());
        }

        // The change is in force, not only in the answer: the page shows the new role.
        var listed = (await MembersAsync(owner, slug))
            .Single(item => item.GetProperty("handle").GetString() == joinerHandle);
        Assert.Equal("moderator", listed.GetProperty("role").GetString());

        // FR-013 / SC-011: one trail row with author, when and the policy version in force.
        var allow = (await RowsAsync(slug)).Single(entry =>
            entry.Action == PolicyAudit.ChangeRole && entry.Result == PolicyAudit.Allow);
        Assert.Equal(ActorOf(ownerHandle), allow.ActorId);
        Assert.Equal(1, allow.PolicyVersion);
        Assert.NotEqual(default, allow.At);
        Assert.False(string.IsNullOrWhiteSpace(allow.Reason));
    }

    [Fact] // AC-02
    public async Task The_moderation_verbs_follow_33_with_justifications_and_the_owner_invariants()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac02");
        var slug = Slug("moderacao");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Moderacao", slug }, owner.Csrf)).StatusCode);
        await SetJoinAsync(owner, slug, "request", 1);

        // A member promoted into Moderator — §3.2 gives it approve/reject/silence, not papéis.
        var (mod, modHandle) = await SignedInAsync("ac02mod");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(mod, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, mod.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{modHandle}", new { state = "approve" }, owner.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{modHandle}", new { role = "moderator" }, owner.Csrf)).StatusCode);

        // approve (Requested → Active), executed by the Moderator of §3.2.
        var (requester, requesterHandle) = await SignedInAsync("ac02r");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(requester, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, requester.Csrf)).StatusCode);
        var approve = await SendAsync(mod, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{requesterHandle}", new { state = "approve" }, mod.Csrf);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(MembershipState.Active, (await Store().FindMembershipAsync(slug, requesterHandle))!.State);
        Assert.Equal(3, await MemberCountAsync(owner, slug)); // owner + moderator + requester

        // reject: §3.3's (removido) — the row itself is gone and the page never lists it.
        var (rejected, rejectedHandle) = await SignedInAsync("ac02x");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(rejected, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, rejected.Csrf)).StatusCode);
        var reject = await SendAsync(mod, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{rejectedHandle}", new { state = "reject" }, mod.Csrf);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Null(await Store().FindMembershipAsync(slug, rejectedHandle));
        Assert.DoesNotContain(rejectedHandle, Handles(await MembersAsync(owner, slug)));

        // silence and unsilence, both by the Moderator (§3.2: silencia).
        Assert.Equal("silenced", await StateAsync(await SendAsync(mod, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{requesterHandle}", new { state = "silence" }, mod.Csrf)));
        Assert.Equal(2, await MemberCountAsync(owner, slug)); // silenced leaves the count (§2.2)
        Assert.Equal("active", await StateAsync(await SendAsync(mod, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{requesterHandle}", new { state = "unsilence" }, mod.Csrf)));
        Assert.Equal(3, await MemberCountAsync(owner, slug));

        // ban demands a justification: 422 and nothing written.
        var noReason = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{requesterHandle}", new { state = "ban" }, owner.Csrf);
        Assert.Equal((HttpStatusCode)422, noReason.StatusCode);
        Assert.Contains("validation_failed", await noReason.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipState.Active, (await Store().FindMembershipAsync(slug, requesterHandle))!.State);

        // With one: Active → Banned, and the counter follows the write.
        Assert.Equal("banned", await StateAsync(await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{requesterHandle}",
            new { state = "ban", justification = "spam repetido" }, owner.Csrf)));
        Assert.Equal(MembershipState.Banned, (await Store().FindMembershipAsync(slug, requesterHandle))!.State);
        Assert.Equal(2, await MemberCountAsync(owner, slug));

        // The Owner is never banished: 403, transfer first, the row exactly where it was.
        var ownerBan = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}",
            new { state = "ban", justification = "por quê não" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, ownerBan.StatusCode);
        var ownerRow = (await Store().FindMembershipAsync(slug, ownerHandle))!;
        Assert.Equal(MembershipRole.Owner, ownerRow.Role);
        Assert.Equal(MembershipState.Active, ownerRow.State);

        // unban (Banned → Active) also demands one — a whitespace-only text is still empty.
        var emptyUnban = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{requesterHandle}",
            new { state = "unban", justification = "   " }, owner.Csrf);
        Assert.Equal((HttpStatusCode)422, emptyUnban.StatusCode);
        Assert.Equal(MembershipState.Banned, (await Store().FindMembershipAsync(slug, requesterHandle))!.State);

        Assert.Equal("active", await StateAsync(await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{requesterHandle}",
            new { state = "unban", justification = "apelido aceito" }, owner.Csrf)));
        Assert.Equal(3, await MemberCountAsync(owner, slug));

        // An edge the machine does not have is a refusal, never a guess: approve on Active.
        var wrongEdge = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{requesterHandle}", new { state = "approve" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, wrongEdge.StatusCode);
        Assert.Contains("validation_failed", await wrongEdge.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipState.Active, (await Store().FindMembershipAsync(slug, requesterHandle))!.State);

        // ban is Admin+ (§3.2): the Moderator's own ban is refused, the member stays active.
        var deniedBan = await SendAsync(mod, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{requesterHandle}",
            new { state = "ban", justification = "não gostei" }, mod.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, deniedBan.StatusCode);
        Assert.Equal(MembershipState.Active, (await Store().FindMembershipAsync(slug, requesterHandle))!.State);
    }

    [Fact] // AC-03, first half — Silenced reads members-only, and every write of its own is denied
    public async Task A_silenced_member_reads_a_members_only_container_but_its_writes_are_denied()
    {
        var (owner, _) = await SignedInAsync("ac03s");
        var slug = Slug("silenciado");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Silenciado", slug = slug }, owner.Csrf)).StatusCode);

        // Two members of the open community, one of them promoted to Admin (§3.2).
        var (member, memberHandle) = await SignedInAsync("ac03sm");
        var (admin, adminHandle) = await SignedInAsync("ac03sa");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(admin, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, admin.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{adminHandle}", new { role = "admin" }, owner.Csrf)).StatusCode);

        // The container turns members-only with both already in — the read gate of §3.3 now bites.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/policy", new { visibility = "membersOnly" }, owner.Csrf,
            ifMatch: "1")).StatusCode);

        foreach (var handle in new[] { memberHandle, adminHandle })
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
                $"/api/containers/{slug}/members/{handle}", new { state = "silence" }, owner.Csrf)).StatusCode);
        }

        // The read half of §3.3: the silenced member still gets 200 from the members-only container.
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(member, HttpMethod.Get, $"/api/containers/{slug}")).StatusCode);

        // The write half: the silenced Admin is denied by its state — the role alone would allow it.
        var denied = await SendAsync(admin, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{memberHandle}", new { role = "moderator" }, admin.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipRole.Member, (await Store().FindMembershipAsync(slug, memberHandle))!.Role);
        Assert.Equal(MembershipState.Silenced, (await Store().FindMembershipAsync(slug, adminHandle))!.State);
    }

    [Fact] // AC-03, second half — Banned reads a public container exactly like an absent slug
    public async Task A_banned_member_reads_a_public_container_as_the_404_of_an_absent_slug()
    {
        var (owner, _) = await SignedInAsync("ac03b");
        var slug = Slug("banido");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Banido", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("ac03bm");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{memberHandle}",
            new { state = "ban", justification = "regra quebrada" }, owner.Csrf)).StatusCode);

        // FR-018 / SC-005: the public preset no longer carries a banned reader — byte-identical
        // refusal to a slug that was never there.
        var read = await SendAsync(member, HttpMethod.Get, $"/api/containers/{slug}");
        var absent = await SendAsync(member, HttpMethod.Get, "/api/containers/que-nao-existe");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await absent.Content.ReadAsStringAsync(), await read.Content.ReadAsStringAsync());

        // The trail still records which of the two it was (the wire cannot tell them apart).
        Assert.Contains(await RowsAsync(slug), entry =>
            entry.Action == PolicyAudit.View && entry.Result == PolicyAudit.NotFound);
    }

    [Fact] // AC-05
    public async Task Patch_without_a_session_is_401_and_a_moderator_cannot_change_another_role()
    {
        var (owner, _) = await SignedInAsync("ac05");
        var slug = Slug("negado");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Negado", slug }, owner.Csrf)).StatusCode);

        var (mod, modHandle) = await SignedInAsync("ac05m");
        var (target, targetHandle) = await SignedInAsync("ac05t");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(mod, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, mod.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(target, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, target.Csrf)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{modHandle}", new { role = "moderator" }, owner.Csrf)).StatusCode);

        // No session at all: 401 before any role or row is even looked at.
        var anonymous = _host.NewClient();
        var withoutSession = await anonymous.SendAsync(HttpMethod.Patch,
            $"/api/containers/{slug}/members/{targetHandle}", new { role = "admin" });
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Contains("not_authenticated", await withoutSession.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Papéis are Admin+ (data-model §3.2): the Moderator's own role change is refused.
        var denied = await SendAsync(mod, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{targetHandle}", new { role = "admin" }, mod.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipRole.Member, (await Store().FindMembershipAsync(slug, targetHandle))!.Role);

        // The refusal reaches the trail too, with the Moderator as its author (SC-011).
        var deny = (await RowsAsync(slug)).Single(entry =>
            entry.Action == PolicyAudit.ChangeRole && entry.Result == PolicyAudit.Deny);
        Assert.Equal(ActorOf(modHandle), deny.ActorId);
        Assert.Equal(1, deny.PolicyVersion);
        Assert.False(string.IsNullOrWhiteSpace(deny.Reason));

        // Silence still belongs to the Moderator — §3.2 is a ladder, not a wall.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(mod, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{targetHandle}", new { state = "silence" }, mod.Csrf)).StatusCode);
        Assert.Equal(MembershipState.Silenced,
            (await Store().FindMembershipAsync(slug, targetHandle))!.State);
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

    /// <summary>The <c>state</c> word of a moderation answer, asserted to be a success first.</summary>
    private static async Task<string?> StateAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("state").GetString();
    }

    /// <summary>Replaces the join policy with the contract's word and returns the new version.</summary>
    private async Task<int> SetJoinAsync(AuthClient owner, string slug, string join, int version)
    {
        var response = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { join }, owner.Csrf, ifMatch: version.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("policy").GetProperty("version").GetInt32();
    }

    private async Task<List<JsonElement>> MembersAsync(AuthClient client, string slug)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/containers/{slug}/members");
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

    /// <summary>Every trail row of one container resource (the audit store of the slice).</summary>
    private async Task<List<PolicyAuditEntry>> RowsAsync(string slug)
    {
        var trail = new SqlitePolicyAuditStore(
            new SqliteGrainStorage($"Data Source={_host.StoragePath};Pooling=False"));
        var rows = await trail.QueryAsync(new PolicyAuditFilter(Limit: 1000));
        return rows
            .Where(entry => entry.ResourceId.EndsWith($"/c/{slug}", StringComparison.Ordinal))
            .ToList();
    }

    private SqliteContainerStore Store()
    {
        var connection = $"Data Source={_host.StoragePath};Pooling=False";
        return new(new SqliteGrainStorage(connection), connection);
    }

    private string ActorOf(string handle) =>
        $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress!.Authority}/users/{handle}";

    private static string Handle(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    private static string Slug(string prefix) =>
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
