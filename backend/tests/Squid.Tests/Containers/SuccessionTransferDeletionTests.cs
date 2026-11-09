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
/// AC-01..AC-05 of TASK-0021 over HTTP against the host with the controlled clock: the successor's
/// claim waits out <c>Containers:SuccessorInactivityDays</c> without a test sleeping (AC-01), the
/// owner's transfer moves both rows under the trail (AC-03), the soft delete answers the exact
/// confirmation and the grace window the default fixes (AC-04), and the DoD invariant runs as a
/// property over a whole sequence of this phase's commands (AC-05). The operator gate of AC-02 and
/// the configured timers live with their own host in <see cref="AdminOverrideAndConfiguredTimersTests"/>.
/// </summary>
[Collection("ServiceHost")]
public sealed class SuccessionTransferDeletionTests : IClassFixture<SuccessionHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly SuccessionHostFixture _host;

    public SuccessionTransferDeletionTests(SuccessionHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task The_successor_claims_only_after_the_inactivity_clock_and_the_owners_swap()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac01");
        var slug = Slug("sucessao");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Sucessao", slug }, owner.Csrf)).StatusCode);

        // The Owner names a successor who is not even a member — section G never asks for a row.
        var (heir, heirHandle) = await SignedInAsync("ac01h");
        var named = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/successor",
            new { handle = heirHandle }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal(ActorOf(heirHandle), (await Store().FindAsync(slug))!.Container.SuccessorActorId);

        // §9.12: before the deadline the claim is the 403, and no membership row appears.
        var early = await SendAsync(heir, HttpMethod.Post, $"/api/containers/{slug}/claim", null, heir.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, early.StatusCode);
        Assert.Contains("policy_denied", await early.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(await Store().FindMembershipAsync(slug, heirHandle));

        // Day 89 of the default 90: still refused (GAP-17's default proven from a clean config).
        _host.Clock.AdvanceDays(89);
        var still = await SendAsync(heir, HttpMethod.Post, $"/api/containers/{slug}/claim", null, heir.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, still.StatusCode);
        Assert.Null(await Store().FindMembershipAsync(slug, heirHandle));

        // Day 91: the window is open — the heir takes over, Active, and the old owner moves down.
        _host.Clock.AdvanceDays(2);
        var claimed = await SendAsync(heir, HttpMethod.Post, $"/api/containers/{slug}/claim", null, heir.Csrf);
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);

        var members = await Store().ListMembershipsAsync(slug);
        var heirRow = members.Single(member => member.Handle == heirHandle);
        Assert.Equal(MembershipRole.Owner, heirRow.Role);
        Assert.Equal(MembershipState.Active, heirRow.State);
        var oldOwnerRow = members.Single(member => member.Handle == ownerHandle);
        Assert.Equal(MembershipRole.Admin, oldOwnerRow.Role);
        Assert.Equal(MembershipState.Active, oldOwnerRow.State);
        Assert.Equal(ActorOf(heirHandle), (await Store().FindAsync(slug))!.Container.OwnerActorId);

        // Audit: the refusals and the takeover both reach the trail under the version in force —
        // the naming row is the owner's, the takeover row is the heir's.
        var trail = await RowsAsync(slug);
        Assert.Contains(trail, entry =>
            entry.Action == PolicyAudit.Successor && entry.Result == PolicyAudit.Deny
            && entry.ActorId == ActorOf(heirHandle));
        Assert.Contains(trail, entry =>
            entry.Action == PolicyAudit.Successor && entry.Result == PolicyAudit.Allow
            && entry.ActorId == ActorOf(ownerHandle));
        var allow = trail.Single(entry =>
            entry.Action == PolicyAudit.Successor && entry.Result == PolicyAudit.Allow
            && entry.ActorId == ActorOf(heirHandle));
        Assert.Equal(ActorOf(heirHandle), allow.ActorId);
        Assert.Equal(1, allow.PolicyVersion);
        Assert.False(string.IsNullOrWhiteSpace(allow.Reason));
    }

    [Fact] // AC-03
    public async Task The_owner_transfers_and_everybody_else_is_denied_with_a_trail_row()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac03o");
        var slug = Slug("transferencia");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Transferencia", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("ac03m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);

        // No session at all: 401 before anything else runs.
        var anonymous = _host.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(HttpMethod.Post,
            $"/api/containers/{slug}/transfer", new { handle = memberHandle })).StatusCode);

        // A session that is not the Owner: denied, and the roles stay exactly where they were.
        var denied = await SendAsync(member, HttpMethod.Post, $"/api/containers/{slug}/transfer",
            new { handle = ownerHandle }, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipRole.Member, (await Store().FindMembershipAsync(slug, memberHandle))!.Role);
        Assert.Equal(MembershipRole.Owner, (await Store().FindMembershipAsync(slug, ownerHandle))!.Role);

        // The Owner transfers: new Owner Active, old Owner Admin, both audited (data-model §2.2).
        var moved = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/transfer",
            new { handle = memberHandle }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var members = await Store().ListMembershipsAsync(slug);
        var newOwnerRow = members.Single(row => row.Handle == memberHandle);
        Assert.Equal(MembershipRole.Owner, newOwnerRow.Role);
        Assert.Equal(MembershipState.Active, newOwnerRow.State);
        var oldOwnerRow = members.Single(row => row.Handle == ownerHandle);
        Assert.Equal(MembershipRole.Admin, oldOwnerRow.Role);
        Assert.Equal(MembershipState.Active, oldOwnerRow.State);
        Assert.Equal(ActorOf(memberHandle), (await Store().FindAsync(slug))!.Container.OwnerActorId);

        var trail = await RowsAsync(slug);
        Assert.Contains(trail, entry =>
            entry.Action == PolicyAudit.Transfer && entry.Result == PolicyAudit.Deny
            && entry.ActorId == ActorOf(memberHandle));
        var allow = trail.Single(entry =>
            entry.Action == PolicyAudit.Transfer && entry.Result == PolicyAudit.Allow);
        Assert.Equal(ActorOf(ownerHandle), allow.ActorId);
        Assert.Equal(1, allow.PolicyVersion);
        // The row carries where the ownership came from and where it went.
        Assert.Contains(ActorOf(ownerHandle), allow.Reason, StringComparison.Ordinal);
        Assert.Contains(ActorOf(memberHandle), allow.Reason, StringComparison.Ordinal);
    }

    [Fact] // AC-04
    public async Task Delete_confirms_by_the_exact_slug_and_soft_deletes_into_the_grace_window()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac04");
        var slug = Slug("exclusao");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Exclusao", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("ac04m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);

        // No session: 401 (the session ladder every route of this slice answers with).
        var anonymous = _host.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(
            HttpMethod.Delete, $"/api/containers/{slug}?confirm={slug}")).StatusCode);

        // GAP-18: no confirmation, or one that is not this exact slug, changes nothing (400).
        var noConfirm = await SendAsync(owner, HttpMethod.Delete, $"/api/containers/{slug}",
            null, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, noConfirm.StatusCode);
        Assert.Contains("validation_failed", await noConfirm.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var wrongConfirm = await SendAsync(owner, HttpMethod.Delete,
            $"/api/containers/{slug}?confirm={slug}x", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, wrongConfirm.StatusCode);

        // Input validation evaluates no policy: the two refusals left not one Delete row (SPEC-0008 C).
        Assert.DoesNotContain(await RowsAsync(slug), entry => entry.Action == PolicyAudit.Delete);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}")).StatusCode);

        // Only the Owner deletes: a member with the right confirmation still gets the denial.
        var denied = await SendAsync(member, HttpMethod.Delete,
            $"/api/containers/{slug}?confirm={slug}", null, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains(await RowsAsync(slug), entry =>
            entry.Action == PolicyAudit.Delete && entry.Result == PolicyAudit.Deny);

        // The confirmed delete: soft, stamped by the injected clock, grace window from the default 30.
        var clockNow = _host.Clock.GetUtcNow();
        var deleted = await SendAsync(owner, HttpMethod.Delete,
            $"/api/containers/{slug}?confirm={slug}", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using (var json = JsonDocument.Parse(await deleted.Content.ReadAsStringAsync()))
        {
            Assert.Equal(slug, json.RootElement.GetProperty("slug").GetString());
            Assert.Equal(clockNow, json.RootElement.GetProperty("deletedAt").GetDateTimeOffset());
            Assert.Equal(clockNow.AddDays(30),
                json.RootElement.GetProperty("recoverableUntil").GetDateTimeOffset());
        }

        // GET answers the byte-identical 404 of an absent slug, and the members page follows.
        var gone = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");
        var absent = await SendAsync(owner, HttpMethod.Get, "/api/containers/que-nao-existe");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await absent.Content.ReadAsStringAsync(), await gone.Content.ReadAsStringAsync());

        // The state is still there — recoverable through its DeletedAt stamp — just out of any
        // search (SPEC-0008 H's "só Listed=true e não excluídos"), and never orphaned by deletion.
        var row = (await Store().FindAsync(slug))!;
        Assert.NotNull(row.Container.DeletedAt);
        Assert.True(row.Container.IsDeleted);
        Assert.False(row.Container.IsSearchable);
        Assert.Equal(MembershipRole.Owner,
            (await Store().FindMembershipAsync(slug, ownerHandle))!.Role);
        Assert.Contains(await RowsAsync(slug), entry =>
            entry.Action == PolicyAudit.Delete && entry.Result == PolicyAudit.Allow);
    }

    [Fact] // AC-05
    public async Task No_sequence_of_this_phase_leaves_the_container_without_an_active_owner()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac05");
        var slug = Slug("invariante");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Invariante", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("ac05m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);
        var (heir, heirHandle) = await SignedInAsync("ac05h");
        var operatorClient = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await operatorClient.RegisterAsync(
            SuccessionHostFixture.OperatorHandle, Password)).StatusCode);

        await NeverOrphanedAsync(slug); // born with its owner

        // 1. the last Owner cannot leave — 409, the pin of QA-0019, state untouched.
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/leave", null, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // 2. the Owner cannot be banished — 403, the row stays Active.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}",
            new { state = "ban", justification = "auto-ban impossivel" }, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // 3. the Owner row is never demoted here — 409 last_owner.
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { role = "admin" }, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // 4. the claim with an owner still inside the window changes nothing.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/successor", new { handle = heirHandle }, owner.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/claim", null, heir.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // 5. the owner's transfer swaps the rows.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/transfer", new { handle = memberHandle }, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // 6. with the clock run out, the claim restores an active Owner (the DoD sentence).
        _host.Clock.AdvanceDays(91);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/claim", null, heir.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // 7. the operator override restores one too — GAP-14's gate over the same move.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = ownerHandle, justification = "dono inativo ha meses" },
            operatorClient.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // 8. the Admin that was demoted in step 7 leaves without touching the owner row.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/leave", null, heir.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // 9. the soft delete keeps the row — a deleted container is not an orphaned one.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Delete,
            $"/api/containers/{slug}?confirm={slug}", null, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);
    }

    /// <summary>
    /// The DoD invariant as a property: every completed command of this phase leaves exactly one
    /// row that is Owner + Active, and the aggregate's <c>OwnerActorId</c> names that very row.
    /// </summary>
    private async Task NeverOrphanedAsync(string slug)
    {
        var container = (await Store().FindAsync(slug))!.Container;
        var owners = (await Store().ListMembershipsAsync(slug))
            .Where(row => row.Role == MembershipRole.Owner && row.State == MembershipState.Active)
            .ToList();

        Assert.Single(owners);
        Assert.Equal(owners[0].ActorId, container.OwnerActorId);
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
        string? csrf = null) =>
        client.SendAsync(method, path, body, request =>
        {
            if (csrf is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
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
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
