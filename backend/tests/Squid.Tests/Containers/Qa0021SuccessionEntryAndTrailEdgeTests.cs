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
/// QA-0020 (TASK-0021) over HTTP on the section G host: AC-01's claim window with the owner's own
/// writes restarting the clock, AC-02's operator gate (which writes no trail line) and override,
/// AC-03's transfer onto a target that has no membership row yet, AC-04's DELETE ladder in its
/// exact order plus the grace window, and AC-05's never-orphan property over a sequence of this
/// phase that is NOT the one DEV's own property test walks. Every trail word is asserted as the
/// literal of data-model §2.11, so a changed constant cannot silently follow the test.
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0021SectionGEdgeTests : IClassFixture<SuccessionHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly SuccessionHostFixture _host;

    public Qa0021SectionGEdgeTests(SuccessionHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Claim_opens_only_for_the_named_successor_and_the_owners_own_writes_restart_the_clock()
    {
        var (owner, ownerHandle) = await SignedInAsync("q21a1");
        var slug = Slug("qa-sucessao");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Sucessao QA", slug }, owner.Csrf)).StatusCode);

        var (heir, heirHandle) = await SignedInAsync("q21a1h");
        var (stranger, strangerHandle) = await SignedInAsync("q21a1s");

        // Nobody named yet: the claim is the refusal, with its Deny line (SPEC-0008 G, §9.12).
        var unnamed = await SendAsync(heir, HttpMethod.Post, $"/api/containers/{slug}/claim", null, heir.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, unnamed.StatusCode);
        Assert.Contains("policy_denied", await unnamed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(await Store().FindMembershipAsync(slug, heirHandle));

        // The Owner names a successor; the row carries the canonical actor id.
        var named = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/successor",
            new { handle = heirHandle }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal(ActorOf(heirHandle), (await Store().FindAsync(slug))!.Container.SuccessorActorId);

        // An actor who is not the successor never reaches the move — 403 and no row.
        var notHeir = await SendAsync(stranger, HttpMethod.Post, $"/api/containers/{slug}/claim", null, stranger.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, notHeir.StatusCode);
        Assert.Contains("policy_denied", await notHeir.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(await Store().FindMembershipAsync(slug, strangerHandle));

        // Day 0: the named successor is still inside the window (§9.12's 403).
        var early = await SendAsync(heir, HttpMethod.Post, $"/api/containers/{slug}/claim", null, heir.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, early.StatusCode);
        Assert.Null(await Store().FindMembershipAsync(slug, heirHandle));

        // The Owner's own PATCH restarts the inactivity clock (DEV decision: OwnerLastActiveAt
        // renews on the Owner's writes only) — without it, the claim below would open at day 91.
        _host.Clock.AdvanceDays(40);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}", new { name = "Sucessao QA editada" }, owner.Csrf)).StatusCode);
        _host.Clock.AdvanceDays(51); // day 91 of the container, 51 after the owner's write
        var stillInside = await SendAsync(heir, HttpMethod.Post, $"/api/containers/{slug}/claim", null, heir.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, stillInside.StatusCode);
        Assert.Null(await Store().FindMembershipAsync(slug, heirHandle));

        // The Owner's own PUT /policy restarts it again — the second spelling of that decision.
        _host.Clock.AdvanceDays(30); // day 121
        var policy = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { visibility = "public" }, owner.Csrf, ifMatch: "1");
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        _host.Clock.AdvanceDays(89); // day 210: 89 days after the owner's policy write
        var beforeWindow = await SendAsync(heir, HttpMethod.Post, $"/api/containers/{slug}/claim", null, heir.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, beforeWindow.StatusCode);
        Assert.Null(await Store().FindMembershipAsync(slug, heirHandle));

        // The window opens: the heir takes over Active, the old Owner moves to Admin (§G).
        _host.Clock.AdvanceDays(2); // day 212: 91 days after the last owner write
        var claimed = await SendAsync(heir, HttpMethod.Post, $"/api/containers/{slug}/claim", null, heir.Csrf);
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);

        var members = await Store().ListMembershipsAsync(slug);
        var heirRow = members.Single(row => row.Handle == heirHandle);
        Assert.Equal(MembershipRole.Owner, heirRow.Role);
        Assert.Equal(MembershipState.Active, heirRow.State);
        var oldOwnerRow = members.Single(row => row.Handle == ownerHandle);
        Assert.Equal(MembershipRole.Admin, oldOwnerRow.Role);
        Assert.Equal(ActorOf(heirHandle), (await Store().FindAsync(slug))!.Container.OwnerActorId);
        Assert.Single(members, row => row.Role == MembershipRole.Owner && row.State == MembershipState.Active);

        // The trail: refusals and takeover, author by author, under the version in force (= 2 here).
        var trail = await RowsAsync(slug);
        Assert.Contains(trail, row => row.Action == "Successor" && row.Result == "Deny"
            && row.ActorId == ActorOf(heirHandle));
        Assert.Contains(trail, row => row.Action == "Successor" && row.Result == "Deny"
            && row.ActorId == ActorOf(strangerHandle));
        var allow = trail.Single(row => row.Action == "Successor" && row.Result == "Allow"
            && row.ActorId == ActorOf(heirHandle));
        Assert.Equal(2, allow.PolicyVersion); // the version the owner's last PUT left in force
        Assert.False(string.IsNullOrWhiteSpace(allow.Reason));
    }

    [Fact] // AC-02
    public async Task The_operator_gate_writes_no_line_and_the_override_moves_the_ownership_with_its_justification()
    {
        var (owner, ownerHandle) = await SignedInAsync("q21a2");
        var slug = Slug("qa-override");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Override QA", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("q21a2m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);

        var (outsider, outsiderHandle) = await SignedInAsync("q21a2x");
        var operatorClient = await OperatorAsync();

        // The trail as the refusals find it (the member's join wrote its own lines).
        var baseline = (await RowsAsync(slug)).Count;

        // No session at all: 401 (SPEC-0008 D) — the gate before anything else.
        var anonymous = _host.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = memberHandle, justification = "forjado" })).StatusCode);

        // A session outside Instance:Operators: 403 policy_denied, nothing written, and the
        // operator gate itself appends NOTHING (the precedent of the /api/admin/audit route).
        var denied = await SendAsync(outsider, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = memberHandle, justification = "quero assumir" }, outsider.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipRole.Member, (await Store().FindMembershipAsync(slug, memberHandle))!.Role);
        Assert.Null(await Store().FindMembershipAsync(slug, outsiderHandle));
        Assert.Equal(baseline, (await RowsAsync(slug)).Count);

        // §9.12: blank justification is 422 — validation evaluates no policy, so no line either.
        var blank = await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = memberHandle, justification = "   " }, operatorClient.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        var missing = await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer", new { handle = memberHandle }, operatorClient.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Equal(baseline, (await RowsAsync(slug)).Count);

        // The Owner moves the policy to version 2 first: "the version in force" cannot be a 1.
        var policy = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { visibility = "public" }, owner.Csrf, ifMatch: "1");
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        var beforeOverride = (await RowsAsync(slug)).Count;

        // The override itself: the move lands and the row carries author, version and reason (§7).
        var moved = await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = memberHandle, justification = "dono inativo ha meses" }, operatorClient.Csrf);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var members = await Store().ListMembershipsAsync(slug);
        var newOwnerRow = members.Single(row => row.Handle == memberHandle);
        Assert.Equal(MembershipRole.Owner, newOwnerRow.Role);
        Assert.Equal(MembershipState.Active, newOwnerRow.State);
        Assert.Equal(MembershipRole.Admin, members.Single(row => row.Handle == ownerHandle).Role);
        Assert.Equal(ActorOf(memberHandle), (await Store().FindAsync(slug))!.Container.OwnerActorId);
        Assert.Single(members, row => row.Role == MembershipRole.Owner && row.State == MembershipState.Active);

        var rows = await RowsAsync(slug);
        var allow = rows.Single(row => row.Action == "Transfer" && row.Result == "Allow");
        Assert.Equal(ActorOf(SuccessionHostFixture.OperatorHandle), allow.ActorId);
        Assert.Equal(2, allow.PolicyVersion);
        Assert.Contains("dono inativo ha meses", allow.Reason, StringComparison.Ordinal);
        Assert.Contains(ActorOf(ownerHandle), allow.Reason, StringComparison.Ordinal);
        Assert.Contains(ActorOf(memberHandle), allow.Reason, StringComparison.Ordinal);
        // Exactly one new line: the override. Every refusal above left the trail untouched.
        Assert.Equal(beforeOverride + 1, rows.Count);
    }

    [Fact] // AC-03
    public async Task The_owner_transfers_onto_a_target_without_a_row_and_the_refusals_stay_audited()
    {
        var (owner, ownerHandle) = await SignedInAsync("q21a3");
        var slug = Slug("qa-transfer");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Transfer QA", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("q21a3m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);

        // Sem sessão → 401 before the ladder moves.
        var anonymous = _host.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(HttpMethod.Post,
            $"/api/containers/{slug}/transfer", new { handle = memberHandle })).StatusCode);

        // A session that is not the Owner: 403 + the Deny line of the attempt (AC-03).
        var denied = await SendAsync(member, HttpMethod.Post, $"/api/containers/{slug}/transfer",
            new { handle = ownerHandle }, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(await RowsAsync(slug), row => row.Action == "Transfer" && row.Result == "Deny"
            && row.ActorId == ActorOf(memberHandle) && row.PolicyVersion == 1);

        // A body with no identity at all is the 400 of GAP-13's spelling rule — and it writes
        // no Transfer Allow (validation, no policy evaluated).
        var bad = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/transfer",
            new { name = "sem identidade" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.DoesNotContain(await RowsAsync(slug), row => row.Action == "Transfer" && row.Result == "Allow");
        Assert.Equal(MembershipRole.Owner, (await Store().FindMembershipAsync(slug, ownerHandle))!.Role);

        // The move onto a target that has NO membership row creates it Owner + Active (DEV
        // decision verified) and the old Owner lands Admin.
        var (target, targetHandle) = await SignedInAsync("q21a3t");
        Assert.Null(await Store().FindMembershipAsync(slug, targetHandle));
        var moved = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/transfer",
            new { handle = targetHandle }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var members = await Store().ListMembershipsAsync(slug);
        var targetRow = members.Single(row => row.Handle == targetHandle);
        Assert.Equal(MembershipRole.Owner, targetRow.Role);
        Assert.Equal(MembershipState.Active, targetRow.State);
        Assert.Equal(ActorOf(targetHandle), targetRow.ActorId);
        var oldRow = members.Single(row => row.Handle == ownerHandle);
        Assert.Equal(MembershipRole.Admin, oldRow.Role);
        Assert.Equal(MembershipState.Active, oldRow.State);
        Assert.Equal(ActorOf(targetHandle), (await Store().FindAsync(slug))!.Container.OwnerActorId);
        Assert.Single(members, row => row.Role == MembershipRole.Owner && row.State == MembershipState.Active);

        var trail = await RowsAsync(slug);
        var allow = trail.Single(row => row.Action == "Transfer" && row.Result == "Allow");
        Assert.Equal(ActorOf(ownerHandle), allow.ActorId);
        Assert.Equal(1, allow.PolicyVersion);
        Assert.Contains(ActorOf(ownerHandle), allow.Reason, StringComparison.Ordinal);
        Assert.Contains(ActorOf(targetHandle), allow.Reason, StringComparison.Ordinal);

        // The second spelling of the same identity (GAP-13): the canonical actor id works too,
        // and the demoted old Owner cannot move the property back (403 + Deny).
        var (_, thirdHandle) = await SignedInAsync("q21a3u");
        var byActorId = await SendAsync(target, HttpMethod.Post, $"/api/containers/{slug}/transfer",
            new { actorId = ActorOf(thirdHandle) }, target.Csrf);
        Assert.Equal(HttpStatusCode.OK, byActorId.StatusCode);
        var thirdRow = (await Store().FindMembershipAsync(slug, thirdHandle))!;
        Assert.Equal(ActorOf(thirdHandle), thirdRow.ActorId);
        Assert.Equal(MembershipRole.Owner, thirdRow.Role);
        Assert.Equal(MembershipState.Active, thirdRow.State);
        Assert.Equal(MembershipRole.Admin, (await Store().FindMembershipAsync(slug, targetHandle))!.Role);

        var back = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/transfer",
            new { handle = targetHandle }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, back.StatusCode);
        var rows = await RowsAsync(slug);
        Assert.Equal(2, rows.Count(row => row.Action == "Transfer" && row.Result == "Deny"));
        Assert.Single(await Store().ListMembershipsAsync(slug),
            row => row.Role == MembershipRole.Owner && row.State == MembershipState.Active);
        Assert.Equal(ActorOf(thirdHandle),
            (await Store().FindAsync(slug))!.Container.OwnerActorId);
        Assert.Equal(MembershipState.Active,
            (await Store().FindMembershipAsync(slug, memberHandle))!.State);
    }

    [Fact] // AC-04
    public async Task Delete_walks_session_visibility_capability_confirm_in_order_and_keeps_the_row_for_the_grace()
    {
        var (owner, ownerHandle) = await SignedInAsync("q21a4");
        var slug = Slug("qa-exclusao");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Exclusao QA", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("q21a4m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);

        // 1. session: no session → 401, whatever the query says.
        var anonymous = _host.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(
            HttpMethod.Delete, $"/api/containers/{slug}?confirm={slug}")).StatusCode);

        // 2/3. capability BEFORE the confirmation: a member without the exact confirm gets the
        // 403 and its Deny line, not the 400 of the payload (DEV's ladder decision, executed).
        var memberNoConfirm = await SendAsync(member, HttpMethod.Delete, $"/api/containers/{slug}",
            null, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, memberNoConfirm.StatusCode);
        var afterMember = await RowsAsync(slug);
        var deny = afterMember.Single(row => row.Action == "Delete" && row.Result == "Deny");
        Assert.Equal(ActorOf(memberHandle), deny.ActorId);
        Assert.False(string.IsNullOrWhiteSpace(deny.Reason));

        // 4. the exact confirmation is payload: wrong or missing → 400, no Allow line, nothing
        // deleted — and the denial above never became a grant.
        var wrongConfirm = await SendAsync(owner, HttpMethod.Delete,
            $"/api/containers/{slug}?confirm={slug}x", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, wrongConfirm.StatusCode);
        Assert.Contains("validation_failed", await wrongConfirm.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var noConfirm = await SendAsync(owner, HttpMethod.Delete, $"/api/containers/{slug}",
            null, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, noConfirm.StatusCode);
        Assert.DoesNotContain(await RowsAsync(slug), row => row.Action == "Delete" && row.Result == "Allow");
        Assert.Null((await Store().FindAsync(slug))!.Container.DeletedAt);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}")).StatusCode);

        // The confirmed delete: soft, stamped by the injected clock, 30 days of grace by default.
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

        // The state: DeletedAt set, out of any search, still in the store (recoverable).
        var row = (await Store().FindAsync(slug))!;
        Assert.NotNull(row.Container.DeletedAt);
        Assert.True(row.Container.IsDeleted);
        Assert.False(row.Container.IsSearchable);
        Assert.Equal(clockNow, row.Container.DeletedAt);
        Assert.Equal(clockNow.AddDays(30), row.Container.RecoverableUntil(30));
        Assert.Single(await RowsAsync(slug), r => r.Action == "Delete" && r.Result == "Allow");

        // GET answers the byte-identical 404 of an absent slug (FR-018) — and so does every
        // ownership route: the soft delete hides the container from writers too.
        var gone = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");
        var absent = await SendAsync(owner, HttpMethod.Get, "/api/containers/que-nao-existe");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(await absent.Content.ReadAsStringAsync(), await gone.Content.ReadAsStringAsync());

        var (heir, heirHandle) = await SignedInAsync("q21a4h");
        var successorAfter = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/successor",
            new { handle = heirHandle }, owner.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, successorAfter.StatusCode);
        var claimAfter = await SendAsync(heir, HttpMethod.Post, $"/api/containers/{slug}/claim",
            null, heir.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, claimAfter.StatusCode);
        var transferAfter = await SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/transfer",
            new { handle = heirHandle }, owner.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, transferAfter.StatusCode);

        // The window: the row survives into the grace (day 29 of 30) with the same stamp.
        _host.Clock.AdvanceDays(29);
        var stillThere = (await Store().FindAsync(slug))!;
        Assert.NotNull(stillThere.Container.DeletedAt);
        Assert.Equal(clockNow, stillThere.Container.DeletedAt);
        Assert.Equal(ActorOf(ownerHandle),
            (await Store().FindMembershipAsync(slug, ownerHandle))!.ActorId);
    }

    [Fact] // AC-05
    public async Task No_sequence_of_this_phase_leaves_the_container_without_one_active_owner()
    {
        var (owner, ownerHandle) = await SignedInAsync("q21a5");
        var slug = Slug("qa-invariante");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Invariante QA", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("q21a5m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);
        var (heir, heirHandle) = await SignedInAsync("q21a5h");
        var operatorClient = await OperatorAsync();

        await NeverOrphanedAsync(slug); // born with its owner

        // a. the Owner refuses to be moderated: ban → 403, demote → 409, leave → 409.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}",
            new { state = "ban", justification = "auto-ban impossivel" }, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { role = "admin" }, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/leave", null, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // b. the claim inside the window changes nothing.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/successor", new { handle = heirHandle }, owner.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/claim", null, heir.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // c. the transfer moves both rows (the property holds on the new owner).
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/transfer", new { handle = memberHandle }, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // d. the demoted old Owner is a plain Admin now: banning IT does not orphan anything.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(member, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}",
            new { state = "ban", justification = "saiu do projeto" }, member.Csrf)).StatusCode);
        Assert.Equal(MembershipState.Banned,
            (await Store().FindMembershipAsync(slug, ownerHandle))!.State);
        await NeverOrphanedAsync(slug);

        // e. the banned ex-Owner cannot delete the container either.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(owner, HttpMethod.Delete,
            $"/api/containers/{slug}?confirm={slug}", null, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // f. with the clock run out, the claim restores an active Owner (the DoD sentence).
        _host.Clock.AdvanceDays(91);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/claim", null, heir.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);

        // g. the operator override restores one too — GAP-14's gate over the same move.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = ownerHandle, justification = "dono banido, linha Owner precisa voltar" },
            operatorClient.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);
        Assert.Equal(MembershipRole.Owner, (await Store().FindMembershipAsync(slug, ownerHandle))!.Role);
        Assert.Equal(MembershipState.Active, (await Store().FindMembershipAsync(slug, ownerHandle))!.State);

        // h. the soft delete keeps the row — a deleted container is not an orphaned one.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Delete,
            $"/api/containers/{slug}?confirm={slug}", null, owner.Csrf)).StatusCode);
        await NeverOrphanedAsync(slug);
        Assert.NotNull((await Store().FindAsync(slug))!.Container.DeletedAt);
    }

    /// <summary>The DoD invariant as a property: exactly one Owner row Active, named by the aggregate.</summary>
    private async Task NeverOrphanedAsync(string slug)
    {
        var container = (await Store().FindAsync(slug))!.Container;
        var owners = (await Store().ListMembershipsAsync(slug))
            .Where(row => row.Role == MembershipRole.Owner && row.State == MembershipState.Active)
            .ToList();

        Assert.Single(owners);
        Assert.Equal(owners[0].ActorId, container.OwnerActorId);
    }

    /// <summary>
    /// A client for the configured instance operator. The host is shared by this class, so the
    /// account may already exist (whichever of the two tests runs first registers it): a second
    /// register is a 409 and the session then comes from a login with the same secret.
    /// </summary>
    private async Task<AuthClient> OperatorAsync()
    {
        var client = _host.NewClient();
        var registered = await client.RegisterAsync(SuccessionHostFixture.OperatorHandle, Password);
        if (registered.StatusCode != HttpStatusCode.Created)
        {
            var login = await client.LoginAsync(SuccessionHostFixture.OperatorHandle, Password);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        return client;
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

/// <summary>
/// QA-0020 (TASK-0021) on the plain session host — the two adjudicated corrections: AC-07/S-30
/// (entry on a non-public container follows the join policy end to end, while Invite without an
/// invitation keeps the byte-identical 404 the QA-0018 pins) and AC-08/F2 (the Owner invariant
/// refusals append Deny/ChangeRole under the version in force, while 422/400 append nothing).
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0021EntryAndTrailEdgeTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public Qa0021EntryAndTrailEdgeTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-07 — correction S-30
    public async Task Entry_on_a_members_only_container_walks_invite_open_and_request_to_the_end()
    {
        var (owner, ownerHandle) = await SignedInAsync("q21a7");
        var group = Slug("qa-entrada");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Entrada QA", slug = group }, owner.Csrf)).StatusCode);

        // Invite WITHOUT an invitation: the refusal of an absent slug, byte for byte (QA-0018's pin).
        var (stranger, strangerHandle) = await SignedInAsync("q21a7s");
        var invisible = await SendAsync(stranger, HttpMethod.Post,
            $"/api/containers/{group}/join", null, stranger.Csrf);
        var absent = await SendAsync(stranger, HttpMethod.Post,
            "/api/containers/que-nao-existe/join", null, stranger.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await absent.Content.ReadAsStringAsync(), await invisible.Content.ReadAsStringAsync());

        // The invited actor walks in: Active + Member, invitation kept on the row (the DoD case
        // the finding called broken).
        var (guest, guestHandle) = await SignedInAsync("q21a7g");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{group}/invites", new { handle = guestHandle }, owner.Csrf)).StatusCode);
        var joined = await SendAsync(guest, HttpMethod.Post, $"/api/containers/{group}/join",
            null, guest.Csrf);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        using (var json = JsonDocument.Parse(await joined.Content.ReadAsStringAsync()))
        {
            Assert.Equal("active", json.RootElement.GetProperty("state").GetString());
            Assert.Equal("member", json.RootElement.GetProperty("role").GetString());
            Assert.Equal(ActorOf(ownerHandle),
                json.RootElement.GetProperty("invitedBy").GetString());
        }

        Assert.Contains(await RowsAsync(group), row => row.Action == "Join" && row.Result == "Allow"
            && row.ActorId == ActorOf(guestHandle));

        // Open on the same non-public container: straight to Active (201 — a row was created).
        Assert.Equal(2, await SetJoinAsync(owner, group, "open", 1));
        var (opener, openerHandle) = await SignedInAsync("q21a7o");
        var opened = await SendAsync(opener, HttpMethod.Post, $"/api/containers/{group}/join",
            null, opener.Csrf);
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        using (var json = JsonDocument.Parse(await opened.Content.ReadAsStringAsync()))
        {
            Assert.Equal("active", json.RootElement.GetProperty("state").GetString());
        }

        // Request on the same non-public container: Requested first, then the approval that
        // completes the flow — the asker cannot read the page while pending.
        Assert.Equal(3, await SetJoinAsync(owner, group, "request", 2));
        var (asker, askerHandle) = await SignedInAsync("q21a7r");
        var asked = await SendAsync(asker, HttpMethod.Post, $"/api/containers/{group}/join",
            null, asker.Csrf);
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        using (var json = JsonDocument.Parse(await asked.Content.ReadAsStringAsync()))
        {
            Assert.Equal("requested", json.RootElement.GetProperty("state").GetString());
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(asker, HttpMethod.Get, $"/api/containers/{group}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{group}/members/{askerHandle}", new { state = "approve" },
            owner.Csrf)).StatusCode);
        Assert.Equal(MembershipState.Active,
            (await Store().FindMembershipAsync(group, askerHandle))!.State);

        // Back on Invite: the pin stands exactly where QA-0018 left it — 404, byte-identical.
        Assert.Equal(4, await SetJoinAsync(owner, group, "invite", 3));
        var (latecomer, latecomerHandle) = await SignedInAsync("q21a7l");
        var lateDenied = await SendAsync(latecomer, HttpMethod.Post,
            $"/api/containers/{group}/join", null, latecomer.Csrf);
        var lateAbsent = await SendAsync(latecomer, HttpMethod.Post,
            "/api/containers/tambem-nao-existe/join", null, latecomer.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, lateDenied.StatusCode);
        Assert.Equal(await lateAbsent.Content.ReadAsStringAsync(),
            await lateDenied.Content.ReadAsStringAsync());

        // A Banned row never walks in, on a non-public container either — also the 404.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{group}/members/{openerHandle}",
            new { state = "ban", justification = "quebrou as regras" }, owner.Csrf)).StatusCode);
        var bannedJoin = await SendAsync(opener, HttpMethod.Post,
            $"/api/containers/{group}/join", null, opener.Csrf);
        var bannedAbsent = await SendAsync(opener, HttpMethod.Post,
            "/api/containers/ainda-nao-existe/join", null, opener.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, bannedJoin.StatusCode);
        Assert.Equal(await bannedAbsent.Content.ReadAsStringAsync(),
            await bannedJoin.Content.ReadAsStringAsync());

        // Nothing leaked in: the rows hold exactly the states this test created.
        var members = await Store().ListMembershipsAsync(group);
        Assert.Equal(MembershipState.Active, members.Single(row => row.Handle == guestHandle).State);
        Assert.Equal(MembershipState.Banned, members.Single(row => row.Handle == openerHandle).State);
        Assert.Equal(MembershipState.Active, members.Single(row => row.Handle == askerHandle).State);
        Assert.DoesNotContain(members, row => row.Handle == latecomerHandle && row.State == MembershipState.Active);
        Assert.DoesNotContain(members, row => row.Handle == strangerHandle && row.State == MembershipState.Active);
    }

    [Fact] // AC-08 — correction F2 of QA-0019
    public async Task The_owner_invariant_refusals_write_deny_under_the_version_in_force_and_validations_write_nothing()
    {
        var (owner, ownerHandle) = await SignedInAsync("q21a8");
        var slug = Slug("qa-trilha");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Trilha QA", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("q21a8m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);

        // Two policy bumps: the version in force is 3, so neither a 1 nor a hardcoded 2 passes.
        Assert.Equal(2, await PutPolicyAsync(owner, slug, new { visibility = "membersOnly" }, "1"));
        Assert.Equal(3, await PutPolicyAsync(owner, slug, new { join = "request" }, "2"));

        // Promote the member to Admin so the refusals below are written by a second author.
        var promoted = await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{memberHandle}", new { role = "admin" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);

        async Task<int> ChangeRoleCountAsync() =>
            (await RowsAsync(slug)).Count(row => row.Action == "ChangeRole");
        async Task<int> DenyCountAsync() =>
            (await RowsAsync(slug)).Count(row => row.Result == "Deny");

        Assert.Equal(1, await ChangeRoleCountAsync()); // exactly the promotion Allow
        Assert.Equal(0, await DenyCountAsync());

        // 422 (ban with a justification of blanks), 400 (owner word) and 400 (no change at all):
        // input validation — no line, no Deny, nothing written (SPEC-0008 C).
        var empty = await SendAsync(member, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}",
            new { state = "ban", justification = "   " }, member.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        var grantOwner = await SendAsync(member, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { role = "owner" }, member.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, grantOwner.StatusCode);
        var noChange = await SendAsync(member, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { justification = "nada" }, member.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, noChange.StatusCode);
        Assert.Equal(1, await ChangeRoleCountAsync());
        Assert.Equal(0, await DenyCountAsync());

        // The ban refusal is a policy decision: 403 + Deny/ChangeRole under version 3 (FR-019).
        var ban = await SendAsync(member, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}",
            new { state = "ban", justification = "auto-ban impossivel" }, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, ban.StatusCode);
        Assert.Contains("policy_denied", await ban.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var afterBan = await RowsAsync(slug);
        var banDeny = afterBan.Single(row => row.Action == "ChangeRole" && row.Result == "Deny");
        Assert.Equal(ActorOf(memberHandle), banDeny.ActorId);
        Assert.Equal(3, banDeny.PolicyVersion);
        Assert.False(string.IsNullOrWhiteSpace(banDeny.Reason));
        Assert.Equal(MembershipRole.Owner, (await Store().FindMembershipAsync(slug, ownerHandle))!.Role);
        Assert.Equal(MembershipState.Active, (await Store().FindMembershipAsync(slug, ownerHandle))!.State);

        // The demotion refusal likewise: 409 last_owner with its own Deny line under version 3.
        var demote = await SendAsync(member, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { role = "admin" }, member.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Contains("last_owner", await demote.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var afterDemote = await RowsAsync(slug);
        Assert.Equal(2, afterDemote.Count(row => row.Action == "ChangeRole" && row.Result == "Deny"));
        Assert.All(afterDemote
            .Where(row => row.Action == "ChangeRole" && row.Result == "Deny"),
            row => Assert.Equal(3, row.PolicyVersion));

        // And a validation after them still appends nothing at all.
        var blankAgain = await SendAsync(member, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{ownerHandle}", new { state = "ban" }, member.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blankAgain.StatusCode);
        Assert.Equal(3, await ChangeRoleCountAsync()); // promotion Allow + the two refusals
        Assert.Equal(2, await DenyCountAsync());
        Assert.Equal(MembershipRole.Owner, (await Store().FindMembershipAsync(slug, ownerHandle))!.Role);
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

    /// <summary>Replaces the policy with the contract's words and returns the new version.</summary>
    private async Task<int> PutPolicyAsync(AuthClient owner, string slug, object body, string version)
    {
        var response = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            body, owner.Csrf, ifMatch: version);
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
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
