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
/// QA-0019 (TASK-0020) edge cases the DEV suite does not reach: an Admin BELOW the Owner
/// executing the audited role change against the policy version actually in force (AC-01's
/// "Admin+" and "policyVersion vigente", proven as version 2 rather than the fresh 1), and the
/// Owner invariants plus justification cap beside the Admin-only unban (AC-02). Every expected
/// value is read from the response or the store, never remembered.
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0019MemberModerationEdgeTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public Qa0019MemberModerationEdgeTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01 — "Admin+" means the Admin rung too, and the trail carries the version in force
    public async Task An_admin_below_the_owner_changes_a_role_under_the_policy_version_in_force()
    {
        var (owner, ownerHandle) = await SignedInAsync("qa19a");
        var slug = Slug("qa-admin");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "QaAdmin", slug }, owner.Csrf)).StatusCode);

        var (joiner, joinerHandle) = await SignedInAsync("qa19j");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(joiner, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, joiner.Csrf)).StatusCode);
        var (admin, adminHandle) = await SignedInAsync("qa19m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(admin, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, admin.Csrf)).StatusCode);

        // The Owner hands the Admin rung down: the actor under test is a plain Admin, not the Owner.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{adminHandle}", new { role = "admin" }, owner.Csrf)).StatusCode);

        // The policy moves to version 2 BEFORE the change, so "vigente" cannot pass as a hard-coded 1.
        var policy = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { join = "request" }, owner.Csrf, ifMatch: "1");
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        using (var json = JsonDocument.Parse(await policy.Content.ReadAsStringAsync()))
        {
            Assert.Equal(2, json.RootElement.GetProperty("policy").GetProperty("version").GetInt32());
        }

        // AC-01: the Admin (below the Owner) changes Member → Moderator and gets the new row back.
        var patch = await SendAsync(admin, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{joinerHandle}", new { role = "moderator" }, admin.Csrf);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using (var json = JsonDocument.Parse(await patch.Content.ReadAsStringAsync()))
        {
            Assert.Equal("moderator", json.RootElement.GetProperty("role").GetString());
            Assert.Equal("active", json.RootElement.GetProperty("state").GetString());
        }

        // The change is in force in the store and on the page, not only in the answer.
        Assert.Equal(MembershipRole.Moderator,
            (await Store().FindMembershipAsync(slug, joinerHandle))!.Role);
        var listed = (await MembersAsync(owner, slug))
            .Single(item => item.GetProperty("handle").GetString() == joinerHandle);
        Assert.Equal("moderator", listed.GetProperty("role").GetString());

        // FR-013/SC-011: the ONE trail row this Admin wrote carries author, when and version 2.
        var rows = await RowsAsync(slug);
        var allow = rows.Single(entry =>
            entry.Action == PolicyAudit.ChangeRole
            && entry.Result == PolicyAudit.Allow
            && entry.ActorId == ActorOf(adminHandle));
        Assert.Equal(ActorOf(adminHandle), allow.ActorId);
        Assert.Equal(2, allow.PolicyVersion); // the version in force, not the fresh container's 1
        Assert.NotEqual(default, allow.At);
        Assert.False(string.IsNullOrWhiteSpace(allow.Reason));
        // The row's detail is the JSON of before/after: the answer really moved this member.
        Assert.Contains("moderator", allow.Reason, StringComparison.Ordinal);
        Assert.Contains(joinerHandle, allow.Reason, StringComparison.Ordinal);
    }

    [Fact] // AC-02 — Owner invariants (400 grant / 409 demote), the 1000-char cap, Admin-only unban
    public async Task The_owner_invariants_the_cap_and_the_admin_only_unban_answer_as_decided()
    {
        var (owner, ownerHandle) = await SignedInAsync("qa19b");
        var slug = Slug("qa-invariante");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "QaInvariante", slug }, owner.Csrf)).StatusCode);

        var (victim, victimHandle) = await SignedInAsync("qa19v");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(victim, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, victim.Csrf)).StatusCode);
        var (mod, modHandle) = await SignedInAsync("qa19w");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(mod, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, mod.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{modHandle}", new { role = "moderator" }, owner.Csrf)).StatusCode);

        // DEV decision: granting the Owner word is refused at the parse — 400, row untouched.
        var grantOwner = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{victimHandle}", new { role = "owner" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, grantOwner.StatusCode);
        Assert.Contains("validation_failed", await grantOwner.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipRole.Member, (await Store().FindMembershipAsync(slug, victimHandle))!.Role);

        // DEV decision + §9.11 removal half: demoting the Owner row is 409 last_owner, row untouched.
        var demoteOwner = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { role = "admin" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, demoteOwner.StatusCode);
        Assert.Contains("last_owner", await demoteOwner.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var ownerRow = (await Store().FindMembershipAsync(slug, ownerHandle))!;
        Assert.Equal(MembershipRole.Owner, ownerRow.Role);
        Assert.Equal(MembershipState.Active, ownerRow.State);

        // The cap assumed by the gap (≤ 1000): 1001 chars is a bad payload, nothing is written.
        var tooLong = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{victimHandle}",
            new { state = "ban", justification = new string('x', 1001) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("validation_failed", await tooLong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipState.Active, (await Store().FindMembershipAsync(slug, victimHandle))!.State);

        // Exactly 1000 (the boundary, derived from the response's own message) is accepted.
        var boundary = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{victimHandle}",
            new { state = "ban", justification = new string('x', 1000) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
        Assert.Equal(MembershipState.Banned, (await Store().FindMembershipAsync(slug, victimHandle))!.State);

        // Banned → Active is Admin+ (§3.2): the Moderator's unban is refused, the row stays banned.
        var deniedUnban = await SendAsync(mod, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{victimHandle}",
            new { state = "unban", justification = "quero de volta" }, mod.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, deniedUnban.StatusCode);
        Assert.Contains("policy_denied", await deniedUnban.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipState.Banned, (await Store().FindMembershipAsync(slug, victimHandle))!.State);

        // And the Owner (Admin+) unbans with a justification: Banned → Active, counter follows.
        Assert.Equal("active", await StateAsync(await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{victimHandle}",
            new { state = "unban", justification = "quero de volta" }, owner.Csrf)));
        Assert.Equal(MembershipState.Active, (await Store().FindMembershipAsync(slug, victimHandle))!.State);
    }

    /// <summary>Signs a fresh account in: the cookie jar of this test's own client.</summary>
    private async Task<(AuthClient Client, string Handle)> SignedInAsync(string prefix)
    {
        var handle = Handle(prefix);
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);
        return (client, handle);
    }

    private Task<HttpResponseMessage> SendAsync(
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

    private async Task<List<JsonElement>> MembersAsync(AuthClient client, string slug)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/containers/{slug}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
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

    /// <summary>The serialized detail of one trail row, where the before/after roles live.</summary>
    private static async Task<string> TrailJsonAsync(List<PolicyAuditEntry> rows, PolicyAuditEntry row)
    {
        await Task.CompletedTask;
        var detail = typeof(PolicyAuditEntry).GetProperty("Reason")?.GetValue(row) as string;
        return detail ?? string.Empty;
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
