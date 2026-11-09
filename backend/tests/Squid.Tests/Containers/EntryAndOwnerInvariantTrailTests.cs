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
/// The two adjudicated corrections of TASK-0021 on the plain session host (no clock involved):
/// AC-07/S-30 — entry on a non-public container follows the join policy while <c>Invite</c>
/// without an invitation keeps the byte-identical 404 the QA-0018 pins; AC-08/F2 — the Owner
/// invariant refusals append their <c>Deny</c> line under the policy version in force while the
/// 422/400 validations append nothing (SPEC-0008 C).
/// </summary>
[Collection("ServiceHost")]
public sealed class EntryAndOwnerInvariantTrailTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public EntryAndOwnerInvariantTrailTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-07 — correction S-30
    public async Task Join_on_a_non_public_container_follows_the_policy_and_invite_stays_the_404_of_an_absent_slug()
    {
        var (owner, _) = await SignedInAsync("s30");
        var group = Slug("grupo-s30");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Grupo S30", slug = group }, owner.Csrf)).StatusCode);

        // Invite without an invitation on MembersOnly: the refusal of an absent slug, byte for byte.
        var (stranger, _) = await SignedInAsync("s30s");
        var invisible = await SendAsync(stranger, HttpMethod.Post,
            $"/api/containers/{group}/join", null, stranger.Csrf);
        var absent = await SendAsync(stranger, HttpMethod.Post,
            "/api/containers/que-nao-existe/join", null, stranger.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await absent.Content.ReadAsStringAsync(), await invisible.Content.ReadAsStringAsync());

        // The invited actor of the MembersOnly group walks in — the case the finding called broken.
        var (guest, guestHandle) = await SignedInAsync("s30g");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{group}/invites", new { handle = guestHandle }, owner.Csrf)).StatusCode);
        var joined = await SendAsync(guest, HttpMethod.Post, $"/api/containers/{group}/join",
            null, guest.Csrf);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        using (var json = JsonDocument.Parse(await joined.Content.ReadAsStringAsync()))
        {
            Assert.Equal("active", json.RootElement.GetProperty("state").GetString());
            Assert.Equal("member", json.RootElement.GetProperty("role").GetString());
        }

        // Open on the same non-public container creates its flow: straight to Active.
        Assert.Equal(2, await SetJoinAsync(owner, group, "open", 1));
        var (opener, openerHandle) = await SignedInAsync("s30o");
        var opened = await SendAsync(opener, HttpMethod.Post, $"/api/containers/{group}/join",
            null, opener.Csrf);
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        using (var json = JsonDocument.Parse(await opened.Content.ReadAsStringAsync()))
        {
            Assert.Equal("active", json.RootElement.GetProperty("state").GetString());
        }

        // Request creates the other flow: Requested, and the actor is not in yet.
        Assert.Equal(3, await SetJoinAsync(owner, group, "request", 2));
        var (asker, askerHandle) = await SignedInAsync("s30r");
        var asked = await SendAsync(asker, HttpMethod.Post, $"/api/containers/{group}/join",
            null, asker.Csrf);
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        using (var json = JsonDocument.Parse(await asked.Content.ReadAsStringAsync()))
        {
            Assert.Equal("requested", json.RootElement.GetProperty("state").GetString());
        }

        // Back on Invite the pin stands exactly where it was: 404, byte-identical to an absent slug.
        Assert.Equal(4, await SetJoinAsync(owner, group, "invite", 3));
        var (latecomer, latecomerHandle) = await SignedInAsync("s30l");
        var lateDenied = await SendAsync(latecomer, HttpMethod.Post,
            $"/api/containers/{group}/join", null, latecomer.Csrf);
        var lateAbsent = await SendAsync(latecomer, HttpMethod.Post,
            "/api/containers/tambem-nao-existe/join", null, latecomer.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, lateDenied.StatusCode);
        Assert.Equal(await lateAbsent.Content.ReadAsStringAsync(),
            await lateDenied.Content.ReadAsStringAsync());

        // Nothing joined by accident: the page holds the owner, the invitee and the opener only.
        var members = await Store().ListMembershipsAsync(group);
        Assert.Equal(MembershipState.Requested, members.Single(row => row.Handle == askerHandle).State);
        Assert.Equal(MembershipState.Active, members.Single(row => row.Handle == openerHandle).State);
        Assert.DoesNotContain(members, row => row.Handle == latecomerHandle && row.State == MembershipState.Active);
    }

    [Fact] // AC-08 — correction F2 of QA-0019
    public async Task The_owner_invariant_refusals_reach_the_trail_and_the_validations_write_no_line()
    {
        var (owner, ownerHandle) = await SignedInAsync("f2");
        var slug = Slug("trilha-f2");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Trilha F2", slug }, owner.Csrf)).StatusCode);

        // The policy moves to version 2 first, so "a PolicyVersion vigente" cannot pass as a 1.
        var policy = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { visibility = "public" }, owner.Csrf, ifMatch: "1");
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        using (var json = JsonDocument.Parse(await policy.Content.ReadAsStringAsync()))
        {
            Assert.Equal(2, json.RootElement.GetProperty("policy").GetProperty("version").GetInt32());
        }

        var baseline = (await RowsAsync(slug)).Count(entry => entry.Action == PolicyAudit.ChangeRole);

        // 422 (ban without justification) and 400 (the Owner word at the parse) are input
        // validation: no policy was evaluated, so neither writes a line (SPEC-0008 C).
        var empty = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { state = "ban" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        var grantOwner = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { role = "owner" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, grantOwner.StatusCode);
        Assert.Equal(baseline,
            (await RowsAsync(slug)).Count(entry => entry.Action == PolicyAudit.ChangeRole));

        // The ban refusal is a policy decision: Deny / ChangeRole / version in force (FR-019).
        var ban = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}",
            new { state = "ban", justification = "auto-ban impossivel" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, ban.StatusCode);
        var afterBan = await RowsAsync(slug);
        var banDeny = afterBan.Single(entry =>
            entry.Action == PolicyAudit.ChangeRole && entry.Result == PolicyAudit.Deny);
        Assert.Equal(ActorOf(ownerHandle), banDeny.ActorId);
        Assert.Equal(2, banDeny.PolicyVersion);
        Assert.False(string.IsNullOrWhiteSpace(banDeny.Reason));

        // The demotion refusal likewise: 409 last_owner with its own Deny line, row untouched.
        var demote = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { role = "admin" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Contains("last_owner", await demote.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var afterDemote = await RowsAsync(slug);
        Assert.Equal(2, afterDemote.Count(entry =>
            entry.Action == PolicyAudit.ChangeRole && entry.Result == PolicyAudit.Deny));
        Assert.All(afterDemote
            .Where(entry => entry.Action == PolicyAudit.ChangeRole && entry.Result == PolicyAudit.Deny),
            entry => Assert.Equal(2, entry.PolicyVersion));

        var ownerRow = (await Store().FindMembershipAsync(slug, ownerHandle))!;
        Assert.Equal(MembershipRole.Owner, ownerRow.Role);
        Assert.Equal(MembershipState.Active, ownerRow.State);
    }

    /// <summary>Replaces the join policy with the contract's word and returns the new version.</summary>
    private async Task<int> SetJoinAsync(AuthClient owner, string slug, string join, int version)
    {
        var response = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { join }, owner.Csrf, version.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("policy").GetProperty("version").GetInt32();
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
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
