using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Squid.Api.Auth;
using Squid.Api.Discovery;
using Squid.Domain;
using Squid.Domain.Containers;
using Squid.Grains.Actors;

namespace Squid.Api.Containers;

/// <summary>The create body of contracts/http-api.md §4 (quickstart US2/US4 sends exactly this).</summary>
public sealed record CreateContainerRequest(string? Kind, string? Name, string? Slug, string? Summary);

/// <summary>The patch body: only <c>name</c>, <c>summary</c> and <c>listed</c> are writable (§4).</summary>
public sealed record PatchContainerRequest(string? Name, string? Summary, bool? Listed);

/// <summary>The four interaction gates inside the policy body of §4, each one optional.</summary>
public sealed record InteractionRequest(string? CanComment, string? CanReact, string? CanShare, string? CanAttach);

/// <summary>
/// The <c>PUT /policy</c> body: the policy of contracts/http-api.md §4. A field the body omits
/// keeps the value in force — the replacement is always a complete policy, never a half one.
/// <c>version</c> is not a field of the body: the precondition carries it and the server owns it.
/// </summary>
public sealed record PutPolicyRequest(
    string? Posting,
    string? Visibility,
    string? Join,
    InteractionRequest? Interaction,
    IReadOnlyList<string>? PublisherIds);

/// <summary>
/// The <c>POST /invites</c> body: the target of the invitation (GAP-13 — an invitation belongs to
/// one target actor, no shared code, no expiry). Contract §4 names no body for the route, so both
/// local spellings of the same identity arrive: the handle, or the canonical actor id that ends
/// in it.
/// </summary>
public sealed record CreateInviteRequest(string? Handle, string? ActorId);

/// <summary>
/// The <c>PATCH /members/{actorId}</c> body (contract §4, SPEC-0008 section F): the new
/// <c>role</c>, or the §3.3 moderation <c>state</c> verb, plus the <c>justification</c> that
/// <c>ban</c> and <c>unban</c> demand. At least one of role/state must arrive.
/// </summary>
public sealed record PatchMemberRequest(string? Role, string? State, string? Justification);

/// <summary>
/// The <c>PUT /successor</c> body: the successor, spelled the two ways the invite body already
/// accepts (GAP-13's rule) — the handle, or the canonical actor id that ends in it. The contract
/// §4 table names no body for the route, so the house spelling of every identity field is the
/// one this route reads.
/// </summary>
public sealed record SuccessorRequest(string? Handle, string? ActorId);

/// <summary>
/// The body of a transfer — the owner's <c>POST /transfer</c> carries the new owner only, the
/// operator's override adds the <c>justification</c> section G and §9.12 demand (empty → 422).
/// </summary>
public sealed record TransferRequest(string? Handle, string? ActorId, string? Justification);

/// <summary>
/// SPEC-0008 section A: create, read and edit the container core with its owner membership.
/// Session + CSRF on every mutation (the middleware of <c>AuthEndpoints</c> already gates both),
/// and the 404 rule of FR-018/SC-005 — a container the reader cannot see answers byte for byte
/// like a slug that does not exist, so existence never leaks as metadata.
/// Section B joins it here too: <c>PUT /policy</c> is the versioned policy write, and every
/// decision these routes execute is appended to the audit trail of section C (FR-019, SC-011).
/// Section E completes the entry half: <c>join</c>, <c>invites</c>, <c>leave</c> and the paginated
/// member listing of FR-014, decided by the policy evaluator plus the §3.3 state machine.
/// Section F closes the slice: <c>PATCH /members/{actorId}</c> changes role or state through the
/// evaluator with the full actor context (GAP-11), audited with author, when and policy version.
/// </summary>
public static class ContainerEndpoints
{
    /// <summary>Page size of <c>GET /members</c> when the caller asks for none; same ceiling as the trail.</summary>
    private const int DefaultLimit = 100;

    private const int MaxLimit = 1000;

    /// <summary>
    /// The cap a moderation <c>justification</c> carries: the contract defines no limit, the
    /// task's gaps table assumes one (≤ 1000) — a longer text is a payload refusal (400).
    /// </summary>
    internal const int MaxJustificationLength = 1000;

    /// <summary>GAP-17: the two timers of section G — configuration keys with their defaults in force.</summary>
    private const string SuccessorInactivityDaysKey = "Containers:SuccessorInactivityDays";

    private const string DeleteGraceDaysKey = "Containers:DeleteGraceDays";

    private const int SuccessorInactivityDaysDefault = 90;

    private const int DeleteGraceDaysDefault = 30;

    /// <summary>Maps the container routes; runs after <c>MapSquidAuth</c>, on the session pipeline.</summary>
    public static WebApplication MapSquidContainers(this WebApplication app)
    {
        app.MapPost("/api/containers", CreateAsync);
        app.MapGet("/api/containers/{slug}", GetAsync);
        // Section H (FR-016): the literal segment outranks {slug} in the route table, so
        // /api/containers/search is discovery — the slug "search" is reserved by this route.
        app.MapGet("/api/containers/search", SearchAsync);
        app.MapPatch("/api/containers/{slug}", PatchAsync);
        app.MapGet("/api/containers/{slug}/members", MembersAsync);
        app.MapPost("/api/containers/{slug}/join", JoinAsync);
        app.MapPost("/api/containers/{slug}/invites", InviteAsync);
        app.MapPost("/api/containers/{slug}/leave", LeaveAsync);
        app.MapPut("/api/containers/{slug}/policy", PutPolicyAsync);
        // The target is a handle or a canonical actor id, and the latter carries slashes: a
        // catch-all segment takes the path as sent and HandleOf reads whichever spelling it is.
        app.MapPatch("/api/containers/{slug}/members/{*actorId}", PatchMemberAsync);
        // Section G: succession, transfer and the soft delete with its confirmation (GAP-18).
        app.MapPut("/api/containers/{slug}/successor", SuccessorAsync);
        app.MapPost("/api/containers/{slug}/claim", ClaimAsync);
        app.MapPost("/api/containers/{slug}/transfer", TransferAsync);
        app.MapDelete("/api/containers/{slug}", DeleteAsync);
        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateContainerRequest request, HttpContext context, IContainerStore store, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        // No Enum.TryParse: it would accept "1" as a kind. The contract words are the only way in.
        var kind = (request.Kind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "group" => ContainerKind.Group,
            "community" => ContainerKind.Community,
            _ => (ContainerKind?)null,
        };
        if (kind is null)
        {
            return Invalid(context, "kind inválido: use 'group' ou 'community'");
        }

        var slug = request.Slug?.Trim() ?? string.Empty;
        if (!ContainerSlug.IsValid(slug))
        {
            return Invalid(context, $"slug inválido: a regra é {ContainerSlug.Rule}");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > Container.MaxNameLength)
        {
            return Invalid(context, $"name inválido: 1 a {Container.MaxNameLength} caracteres");
        }

        var summary = request.Summary?.Trim() ?? string.Empty;
        if (summary.Length > Container.MaxSummaryLength)
        {
            return Invalid(context, $"summary inválido: máximo de {Container.MaxSummaryLength} caracteres");
        }

        // One clock for the aggregate (GAP-17): CreatedAt and the owner's claim clock come from
        // the injected TimeProvider, so a test moves 90 days without sleeping.
        var now = time.GetUtcNow();
        var container = Container.Create(
            slug, kind.Value, name, summary,
            ActorId(context, current.Handle), Origin(context), now);

        var created = await store.CreateAsync(container);
        if (created is null)
        {
            // GAP-16: a duplicated slug of the same owner refuses with the generic validation
            // code — the catalog §8 has none of its own — and the insert above wrote nothing.
            return AuthErrors.Write(context, StatusCodes.Status400BadRequest,
                "validation_failed", $"slug '{slug}' já está em uso");
        }

        // shortcut: container row first, membership second, two rows with no transaction — the
        // reverse order would append a second owner to the member row a duplicate slug already
        // has. A crash between the two leaves a container without owner; upgrade to one atomic
        // write when the storage grows a transaction (nobody has one today).
        await store.CreateMembershipAsync(new ContainerMembership
        {
            Slug = slug,
            ActorId = container.OwnerActorId,
            Handle = current.Handle,
            Role = MembershipRole.Owner,
            State = MembershipState.Active,
            JoinedAt = now,
        });

        SetETag(context, created.Version);
        return Results.Json(ContainerView.Of(container), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetAsync(
        string slug, HttpContext context, IContainerStore store, IPolicyAuditStore audit)
    {
        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit, forRead: true);
        if (seen is null)
        {
            return Hidden(context);
        }

        SetETag(context, seen.Version);
        return Results.Json(ContainerView.Of(seen.Container));
    }

    /// <summary>
    /// <c>GET /api/containers/search?q=</c> (SPEC-0008 section H, contract §4, FR-016): public —
    /// no session, no CSRF, it is a read. Only containers the search rule of the aggregate passes
    /// (listed and never deleted) leave here, matched as a case-insensitive substring of name and
    /// slug (GAP-15: no FTS, no ranking, I-07) and paged like <c>GET /members</c>. A missing or
    /// blank <c>q</c> refuses before a single row is read (400 validation_failed), and each row
    /// carries its memberCount only to a reader who can already see that container (SC-005).
    /// </summary>
    private static async Task<IResult> SearchAsync(HttpContext context, IContainerStore store)
    {
        var query = context.Request.Query["q"].ToString().Trim();
        if (query.Length == 0)
        {
            return Invalid(context, "q obrigatório: informe um termo de busca");
        }

        if (!TryCount(context.Request.Query["limit"].ToString(), DefaultLimit, 1, out var limit)
            || !TryCount(context.Request.Query["offset"].ToString(), 0, 0, out var offset))
        {
            return Invalid(context, "limit/offset inválidos: inteiros não negativos (limit ≥ 1)");
        }

        if (limit > MaxLimit) limit = MaxLimit; // the ceiling the constant promises, not a refusal

        var items = new List<object>();
        foreach (var container in await store.SearchAsync(query, limit, offset))
        {
            var seen = await CanSeeAsync(container, context, store, forRead: true);
            items.Add(ContainerView.Of(container, seen ? container.MemberCount : null));
        }

        return Results.Json(new { items });
    }

    private static async Task<IResult> PatchAsync(
        string slug, PatchContainerRequest request, HttpContext context,
        IContainerStore store, IPolicyAuditStore audit, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit);
        if (seen is null)
        {
            return Hidden(context);
        }

        var expected = ParseIfMatch(context, out var malformed);
        if (malformed)
        {
            return Invalid(context, "If-Match inválido: informe a versão de estado atual");
        }

        var name = request.Name?.Trim();
        if (name is { Length: 0 or > Container.MaxNameLength })
        {
            return Invalid(context, $"name inválido: 1 a {Container.MaxNameLength} caracteres");
        }

        var summary = request.Summary?.Trim();
        if (summary is { Length: > Container.MaxSummaryLength })
        {
            return Invalid(context, $"summary inválido: máximo de {Container.MaxSummaryLength} caracteres");
        }

        // Only these three fields are writable; slug, kind and the policy are untouched whatever
        // the body carries, and the policy keeps its version — PATCH moves the state version.
        // The claim's clock is the fourth thing this write may move, and only under its owner
        // (SPEC-0008 G: inactivity of the Owner is what the successor waits out).
        var ownerWriting = seen.Container.OwnerActorId == ActorId(context, current.Handle);
        var updated = await store.UpdateAsync(slug, expected, row => row with
        {
            Name = name ?? row.Name,
            Summary = summary ?? row.Summary,
            Listed = request.Listed ?? row.Listed,
            OwnerLastActiveAt = ownerWriting ? time.GetUtcNow() : row.OwnerLastActiveAt,
        });

        if (updated is null)
        {
            var now = await store.FindAsync(slug);
            if (now is null) return Hidden(context);
            SetETag(context, now.Version);
            return AuthErrors.Write(context, StatusCodes.Status409Conflict,
                "version_conflict", "conflito de versão: o estado atual difere do If-Match enviado");
        }

        SetETag(context, updated.Version);
        return Results.Json(ContainerView.Of(updated.Container));
    }

    /// <summary>
    /// <c>GET /api/containers/{slug}/members</c>, paginated (contract §4, SPEC-0008 section E):
    /// one stable page in insertion order, so walking <c>limit</c>/<c>offset</c> neither repeats
    /// nor loses an item (AC-05). Whoever can see the container sees the <c>Active</c> members;
    /// <c>Admin</c>+ additionally the pending states <c>Invited</c>/<c>Requested</c>; and nothing
    /// else appears at all — <c>Banned</c> never for anyone, <c>Silenced</c> and <c>Left</c> are
    /// not on the page. The reader outside gets the 404 of FR-018 first.
    /// </summary>
    private static async Task<IResult> MembersAsync(
        string slug, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, IPolicyEvaluator evaluator)
    {
        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit, forRead: true);
        if (seen is null)
        {
            return Hidden(context);
        }

        var current = AuthSessions.Current(context);
        var reader = current is null ? null : await store.FindMembershipAsync(slug, current.Handle);
        var admin = evaluator.Evaluate(
            PolicyAction.Admin, RoleOf(reader), seen.Container.Policy).Permitted;

        var query = context.Request.Query;
        if (!TryCount(query["limit"].ToString(), DefaultLimit, 1, out var limit)
            || !TryCount(query["offset"].ToString(), 0, 0, out var offset))
        {
            return Invalid(context, "limit/offset inválidos: inteiros não negativos (limit ≥ 1)");
        }

        if (limit > MaxLimit) limit = MaxLimit; // the ceiling the constant promises, not a refusal

        var items = (await store.ListMembershipsAsync(slug))
            .Where(member => Listable(member.State, admin))
            .Skip(offset)
            .Take(limit)
            .Select(ContainerView.Member);

        return Results.Json(new { items });
    }

    /// <summary>
    /// <c>POST /api/containers/{slug}/join</c> (SPEC-0008 section E, FR-014/SC-006): session,
    /// visibility — a container the actor cannot see answers the 404 of an absent slug before any
    /// entry rule runs (FR-018) — then the evaluator's <c>Join</c> verdict with the §3.3 state on
    /// top of it: <c>Open</c> enters as <c>Member</c>, <c>Request</c> lands as <c>Requested</c>, an
    /// invitation in force is the entry ticket of <c>Invite</c> for this actor alone (GAP-13), and
    /// <c>Closed</c> or a missing invitation denies with 403. Allow and Deny both reach the trail
    /// (SC-011); the success body is the membership itself.
    /// </summary>
    private static async Task<IResult> JoinAsync(
        string slug, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, IPolicyEvaluator evaluator)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var state = await store.FindAsync(slug);
        var seen = await DecideVisibilityAsync(slug, state, context, store, audit,
            // AC-07 / SPEC-0008 section E: on a container out of public sight the join route still
            // admits the actor whose entry the rules must judge — everyone else keeps the 404.
            admitted: state is not null && await CanEnterAsync(state.Container, context, store));
        if (seen is null)
        {
            return Hidden(context);
        }

        var container = seen.Container;
        var membership = await store.FindMembershipAsync(slug, current.Handle);
        var decision = evaluator.Evaluate(PolicyAction.Join, RoleOf(membership), container.Policy);
        var target = MembershipRules.JoinTarget(membership?.State, container.Policy.Join, decision.Permitted);
        if (target is null)
        {
            // The refusal is a decision too (SC-011): it reaches the trail before the 403 does.
            await audit.AppendAsync(new PolicyAuditEntry(
                DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.Join,
                container.Id, PolicyAudit.Deny, decision.PolicyVersion, decision.Reason));

            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "a política do contêiner nega esta entrada para o seu papel.");
        }

        if (membership?.State == target)
        {
            // Already in that state: the entry is allowed, nothing is written and the version stays.
            await audit.AppendAsync(new PolicyAuditEntry(
                DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.Join,
                container.Id, PolicyAudit.Allow, decision.PolicyVersion, decision.Reason));

            return Results.Json(ContainerView.Member(membership));
        }

        var written = await store.UpsertMembershipAsync(new ContainerMembership
        {
            Slug = slug,
            ActorId = membership?.ActorId ?? ActorId(context, current.Handle),
            Handle = current.Handle,
            Role = membership?.Role ?? MembershipRole.Member,
            State = target.Value,
            JoinedAt = membership?.JoinedAt ?? DateTimeOffset.UtcNow,
            InvitedBy = membership?.InvitedBy,
        });

        // Only a landing on Active moves the counter (data-model §2.2); a member who was already
        // active never reaches this write, so the count can only have grown here.
        if (target == MembershipState.Active)
        {
            await RecountAsync(slug, store);
        }

        await audit.AppendAsync(new PolicyAuditEntry(
            DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.Join,
            container.Id, PolicyAudit.Allow, decision.PolicyVersion, decision.Reason));

        return Results.Json(ContainerView.Member(written),
            statusCode: membership is null ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    /// <summary>
    /// <c>POST /api/containers/{slug}/invites</c> (SPEC-0008 section E, GAP-13): <c>Admin</c>+
    /// decided by the evaluator, never by a role <c>if</c> (data-model §3.2), and the invitation is
    /// the state <c>Invited</c> on the target actor's own row with <c>InvitedBy</c> — no shared
    /// code, no expiry. The target already linked (member, invited or pending) gets the simplest
    /// refusal of the gaps table: 400, nothing written.
    /// </summary>
    private static async Task<IResult> InviteAsync(
        CreateInviteRequest? request, string slug, HttpContext context,
        IContainerStore store, IPolicyAuditStore audit, IPolicyEvaluator evaluator)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit);
        if (seen is null)
        {
            return Hidden(context);
        }

        var container = seen.Container;
        var reader = await store.FindMembershipAsync(slug, current.Handle);
        var decision = evaluator.Evaluate(PolicyAction.Admin, RoleOf(reader), container.Policy);
        if (!decision.Permitted)
        {
            await audit.AppendAsync(new PolicyAuditEntry(
                DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.Invite,
                container.Id, PolicyAudit.Deny, decision.PolicyVersion, decision.Reason));

            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "a política do contêiner nega convites para o seu papel.");
        }

        var target = TargetHandle(request);
        if (target is null)
        {
            return Invalid(context, "alvo do convite inválido: envie handle ou actorId de um ator local");
        }

        var membership = await store.FindMembershipAsync(slug, target);
        if (!MembershipRules.CanInvite(membership?.State))
        {
            return Invalid(context, $"convite recusado: '{target}' já tem vínculo com este contêiner");
        }

        var invited = new ContainerMembership
        {
            Slug = slug,
            ActorId = membership?.ActorId ?? ActorId(context, target),
            Handle = target,
            Role = membership?.Role ?? MembershipRole.Member,
            State = MembershipState.Invited,
            JoinedAt = membership?.JoinedAt ?? DateTimeOffset.UtcNow,
            InvitedBy = ActorId(context, current.Handle),
        };
        await store.UpsertMembershipAsync(invited);

        await audit.AppendAsync(new PolicyAuditEntry(
            DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.Invite,
            container.Id, PolicyAudit.Allow, decision.PolicyVersion, decision.Reason));

        return Results.Json(ContainerView.Member(invited),
            statusCode: membership is null ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    /// <summary>
    /// <c>POST /api/containers/{slug}/leave</c> (SPEC-0008 section E): <c>Active</c> → <c>Left</c>,
    /// refused with 400 for anyone not actively in (§3.3 has no other leave edge), and the last
    /// active Owner refuses with <c>409 last_owner</c> (contract §9.11) with the state untouched —
    /// transfer the ownership first. No policy is evaluated here, so no decision row is written.
    /// </summary>
    private static async Task<IResult> LeaveAsync(
        string slug, HttpContext context, IContainerStore store, IPolicyAuditStore audit)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit);
        if (seen is null)
        {
            return Hidden(context);
        }

        var membership = await store.FindMembershipAsync(slug, current.Handle);
        if (membership is null || !MembershipRules.CanLeave(membership.State))
        {
            return Invalid(context, "somente um membro ativo pode sair do contêiner");
        }

        var members = await store.ListMembershipsAsync(slug);
        var activeOwners = members.Count(member =>
            member.Role == MembershipRole.Owner && member.State == MembershipState.Active);
        if (membership.Role == MembershipRole.Owner && activeOwners <= 1)
        {
            return AuthErrors.Write(context, StatusCodes.Status409Conflict,
                "last_owner", "o último Owner não pode sair: transfira a propriedade antes.");
        }

        var written = await store.UpsertMembershipAsync(membership with { State = MembershipState.Left });
        await RecountAsync(slug, store);
        return Results.Json(ContainerView.Member(written));
    }

    /// <summary>
    /// The capability ladder every section G owner command runs: the evaluator decides Admin+ with
    /// the full actor (data-model §3.2 — never a role <c>if</c> grants anything), and §2.2's
    /// single-owner invariant narrows the command to the active Owner. Either refusal is one Deny
    /// row with the version in force before the 403 (AC-08's reading of FR-019/SC-011); a
    /// permitted command carries its decision out.
    /// </summary>
    private static async Task<(PolicyDecision Decision, IResult? Refused)> OwnerCommandAsync(
        Container container, string handle, ContainerMembership? reader, HttpContext context,
        IPolicyAuditStore audit, IPolicyEvaluator evaluator, string action)
    {
        var decision = evaluator.Evaluate(
            PolicyAction.Admin, ActorContext(reader, container.Policy), container.Policy);
        var isOwner = reader is { Role: MembershipRole.Owner, State: MembershipState.Active };
        if (decision.Permitted && isOwner)
        {
            return (decision, null);
        }

        await audit.AppendAsync(new PolicyAuditEntry(
            DateTimeOffset.UtcNow, ActorId(context, handle), action, container.Id,
            PolicyAudit.Deny, decision.PolicyVersion,
            !decision.Permitted
                ? decision.Reason
                : "somente o Owner ativo comanda isto: a propriedade muda só por transferência (data-model §2.2)"));

        return (decision, AuthErrors.Write(context, StatusCodes.Status403Forbidden,
            "policy_denied", "a política do contêiner nega esta operação para o seu papel."));
    }

    /// <summary>
    /// <c>PUT /api/containers/{slug}/successor</c> (SPEC-0008 section G, data-model §2.2): the
    /// Owner names who takes over once the claim's inactivity clock runs out. The contract §4
    /// table gives the route no body, so the identity arrives the two ways the invite spells it.
    /// </summary>
    private static async Task<IResult> SuccessorAsync(
        string slug, SuccessorRequest? request, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, IPolicyEvaluator evaluator)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit);
        if (seen is null)
        {
            return Hidden(context);
        }

        var container = seen.Container;
        var (decision, refused) = await OwnerCommandAsync(
            container, current.Handle, await store.FindMembershipAsync(slug, current.Handle),
            context, audit, evaluator, PolicyAudit.Successor);
        if (refused is not null)
        {
            return refused;
        }

        var successor = IdentityOf(request?.Handle, request?.ActorId);
        if (successor is null)
        {
            return Invalid(context, "successor inválido: envie handle ou actorId de um ator local");
        }

        var successorActorId = ActorId(context, successor);
        var updated = await store.UpdateAsync(slug, null, row => row with
        {
            SuccessorActorId = successorActorId,
        });

        await audit.AppendAsync(new PolicyAuditEntry(
            DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.Successor,
            container.Id, PolicyAudit.Allow, decision.PolicyVersion,
            JsonSerializer.Serialize(new { successor = successorActorId })));

        return Results.Json(ContainerView.Of(updated?.Container ?? container));
    }

    /// <summary>
    /// <c>POST /api/containers/{slug}/claim</c> (SPEC-0008 section G, contract §9.12): only the
    /// named successor claims, and only after <c>Containers:SuccessorInactivityDays</c> (default
    /// 90, GAP-17) of owner inactivity — every refusal before the deadline is a Deny row on the
    /// trail and the 403. Success runs the shared ownership move: the new Owner lands
    /// <c>Active</c>, the old one moves to <c>Admin</c>, both audited under the version in force.
    /// </summary>
    private static async Task<IResult> ClaimAsync(
        string slug, HttpContext context, IContainerStore store, IPolicyAuditStore audit,
        IConfiguration configuration, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var claimer = ActorId(context, current.Handle);
        var state = await store.FindAsync(slug);
        var seen = await DecideVisibilityAsync(slug, state, context, store, audit,
            // The named successor holds the ticket to reach this route even with no membership
            // row — §G's claim is exactly the actor outsiders cannot be, so nobody else leaks in.
            admitted: state is not null
                && string.Equals(state.Container.SuccessorActorId, claimer, StringComparison.Ordinal));
        if (seen is null)
        {
            return Hidden(context);
        }

        var container = seen.Container;
        var now = time.GetUtcNow();
        var inactivityDays = Days(configuration, SuccessorInactivityDaysKey, SuccessorInactivityDaysDefault);

        async Task<IResult> DenyAsync(string message, string reason)
        {
            await audit.AppendAsync(new PolicyAuditEntry(
                now, claimer, PolicyAudit.Successor, container.Id, PolicyAudit.Deny,
                container.Policy.Version, reason));

            return AuthErrors.Write(context, StatusCodes.Status403Forbidden, "policy_denied", message);
        }

        if (container.SuccessorActorId is null)
        {
            return await DenyAsync(
                "nenhum sucessor foi nomeado neste contêiner.",
                "o claim exige um sucessor nomeado pelo Owner (SPEC-0008 G)");
        }

        if (!string.Equals(container.SuccessorActorId, claimer, StringComparison.Ordinal))
        {
            return await DenyAsync(
                "quem reivindica a propriedade não é o sucessor nomeado.",
                "só o sucessor nomeado pode reivindicar (SPEC-0008 G)");
        }

        if (!SuccessionRules.ClaimAllowed(now, container.OwnerActivityAt, inactivityDays))
        {
            return await DenyAsync(
                $"o claim só abre após {inactivityDays} dias de inatividade do Owner.",
                $"claim antes dos {inactivityDays} dias de inatividade do Owner (contract §9.12)");
        }

        var updated = await Ownership.MoveAsync(store, container, current.Handle, claimer, now);
        await audit.AppendAsync(new PolicyAuditEntry(
            now, claimer, PolicyAudit.Successor, container.Id, PolicyAudit.Allow,
            updated.Policy.Version,
            JsonSerializer.Serialize(new
            {
                successor = claimer,
                inactiveSince = container.OwnerActivityAt,
                inactivityDays,
            })));

        return Results.Json(ContainerView.Of(updated));
    }

    /// <summary>
    /// <c>POST /api/containers/{slug}/transfer</c> (SPEC-0008 section G): the Owner hands the
    /// property over — new Owner <c>Active</c>, old Owner <c>Admin</c>, one Deny row per refusal
    /// (AC-03) and one Allow row with author and version on success.
    /// </summary>
    private static async Task<IResult> TransferAsync(
        string slug, TransferRequest? request, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, IPolicyEvaluator evaluator, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit);
        if (seen is null)
        {
            return Hidden(context);
        }

        var container = seen.Container;
        var (decision, refused) = await OwnerCommandAsync(
            container, current.Handle, await store.FindMembershipAsync(slug, current.Handle),
            context, audit, evaluator, PolicyAudit.Transfer);
        if (refused is not null)
        {
            return refused;
        }

        var newOwner = IdentityOf(request?.Handle, request?.ActorId);
        if (newOwner is null)
        {
            return Invalid(context, "novo Owner inválido: envie handle ou actorId de um ator local");
        }

        var newOwnerActorId = ActorId(context, newOwner);
        var now = time.GetUtcNow();
        var updated = await Ownership.MoveAsync(store, container, newOwner, newOwnerActorId, now);

        await audit.AppendAsync(new PolicyAuditEntry(
            now, ActorId(context, current.Handle), PolicyAudit.Transfer, container.Id,
            PolicyAudit.Allow, decision.PolicyVersion,
            JsonSerializer.Serialize(new { from = container.OwnerActorId, to = newOwnerActorId })));

        return Results.Json(ContainerView.Of(updated));
    }

    /// <summary>
    /// <c>DELETE /api/containers/{slug}</c> (SPEC-0008 section G, GAP-18): the Owner's soft
    /// delete behind the exact confirmation of the query — the ladder reads session (401),
    /// visibility (404: a deleted container answers like an absent one, one delete only), the
    /// capability (403 with its Deny row), the confirmation (400, pure payload validation and so
    /// without a row, SPEC-0008 C), and only then <c>DeletedAt</c>: the row stays for the whole
    /// grace of <c>Containers:DeleteGraceDays</c> (default 30), which the answer reports so the
    /// window is observable.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        string slug, HttpContext context, IContainerStore store, IPolicyAuditStore audit,
        IPolicyEvaluator evaluator, IConfiguration configuration, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit);
        if (seen is null)
        {
            return Hidden(context);
        }

        var container = seen.Container;
        var (decision, refused) = await OwnerCommandAsync(
            container, current.Handle, await store.FindMembershipAsync(slug, current.Handle),
            context, audit, evaluator, PolicyAudit.Delete);
        if (refused is not null)
        {
            return refused;
        }

        if (!string.Equals(context.Request.Query["confirm"].ToString(), slug, StringComparison.Ordinal))
        {
            return Invalid(context, $"confirmação obrigatória: envie ?confirm={slug}");
        }

        var now = time.GetUtcNow();
        var updated = await store.UpdateAsync(slug, null, row => row with { DeletedAt = now });
        var deleted = updated?.Container ?? container;
        var until = deleted.RecoverableUntil(
            Days(configuration, DeleteGraceDaysKey, DeleteGraceDaysDefault));

        await audit.AppendAsync(new PolicyAuditEntry(
            now, ActorId(context, current.Handle), PolicyAudit.Delete, container.Id,
            PolicyAudit.Allow, decision.PolicyVersion,
            JsonSerializer.Serialize(new { deletedAt = now, recoverableUntil = until })));

        return Results.Json(new { slug, deletedAt = now, recoverableUntil = until });
    }

    /// <summary>
    /// <c>PATCH /api/containers/{slug}/members/{actorId}</c> (SPEC-0008 section F, FR-013):
    /// papéis and moderation behind one command, because the command is now the single boundary
    /// of the decision — the evaluator receives the FULL actor context (GAP-11). The ladder is
    /// §3.2's: <c>Admin</c>+ changes papéis and bans, <c>Moderator</c>+ approves, rejects and
    /// silences, decided by <see cref="IPolicyEvaluator"/> and never by a role <c>if</c>; the
    /// §3.3 machine alone says which edge a verb may take; <c>ban</c>/<c>unban</c> demand a
    /// justification (422 without one); the Owner is never banished or demoted here — transfer
    /// first. Allow and Deny both reach the trail with author, when and the policy version the
    /// decision was taken under (SC-011). The target is a handle or a canonical actor id: one
    /// identity, two spellings, the invite's own rule (GAP-13).
    /// </summary>
    private static async Task<IResult> PatchMemberAsync(
        string slug, string? actorId, PatchMemberRequest? request, HttpContext context,
        IContainerStore store, IPolicyAuditStore audit, IPolicyEvaluator evaluator)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit);
        if (seen is null)
        {
            return Hidden(context);
        }

        var handle = HandleOf(actorId);
        if (handle is null)
        {
            return Invalid(context, "actorId inválido: envie o handle ou o actorId do membro");
        }

        var target = await store.FindMembershipAsync(slug, handle);
        if (target is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status404NotFound,
                "not_found", "membro não encontrado neste contêiner.");
        }

        if (request is null)
        {
            return Invalid(context, "corpo ausente: envie role ou state");
        }

        var role = request.Role is null ? (MembershipRole?)null : ParseRole(request.Role);
        if (request.Role is not null && role is null)
        {
            return Invalid(context, "papel inválido: use admin|moderator|member — o papel Owner só muda por transferência");
        }

        var change = request.State is null ? (MembershipChange?)null : ParseChange(request.State);
        if (request.State is not null && change is null)
        {
            return Invalid(context, "state inválido: use approve|reject|silence|unsilence|ban|unban");
        }

        if (role is null && change is null)
        {
            return Invalid(context, "corpo sem mudança: envie role ou state");
        }

        var container = seen.Container;

        // §3.2: papéis and bans belong to Admin+, entry and silence to Moderator+ — one
        // evaluation with the complete actor decides it (GAP-11), for both verbs when the body
        // carries a role and a state at once.
        var capability = role is not null || change is MembershipChange.Ban or MembershipChange.Unban
            ? PolicyAction.Admin
            : PolicyAction.Moderate;

        var decision = evaluator.Evaluate(
            capability,
            ActorContext(await store.FindMembershipAsync(slug, current.Handle), container.Policy),
            container.Policy);
        if (!decision.Permitted)
        {
            // The refusal is a decision too (SC-011): it reaches the trail before the 403 does.
            await audit.AppendAsync(new PolicyAuditEntry(
                DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.ChangeRole,
                container.Id, PolicyAudit.Deny, decision.PolicyVersion, decision.Reason));

            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "a política do contêiner nega esta operação para o seu papel.");
        }

        var justification = request.Justification?.Trim();
        if ((change is MembershipChange.Ban or MembershipChange.Unban) && string.IsNullOrEmpty(justification))
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "validation_failed", $"{request.State} exige uma justificativa não vazia.");
        }

        if (justification is { Length: > MaxJustificationLength })
        {
            return Invalid(context, $"justificativa inválida: máximo de {MaxJustificationLength} caracteres");
        }

        // data-model §2.2/§3.3: the Owner is never banished and never demoted by this route —
        // transfer first (SPEC-0008 section F; §9.11's refusal for the removal half). Both are
        // refusals of a policy the moment they are reached, so both reach the trail with the
        // version in force (AC-08, FR-019/SC-011); the 422/400 validations above evaluated no
        // policy and write nothing (SPEC-0008 section C).
        if (change is MembershipChange.Ban && target.Role == MembershipRole.Owner)
        {
            await audit.AppendAsync(new PolicyAuditEntry(
                DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.ChangeRole,
                container.Id, PolicyAudit.Deny, decision.PolicyVersion,
                "o Owner não pode ser banido: transfira a propriedade antes (data-model §2.2)"));

            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "o Owner não pode ser banido: transfira a propriedade antes.");
        }

        if (role is MembershipRole.Owner)
        {
            return Invalid(context, "o papel Owner só muda por transferência");
        }

        if (target.Role == MembershipRole.Owner)
        {
            await audit.AppendAsync(new PolicyAuditEntry(
                DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.ChangeRole,
                container.Id, PolicyAudit.Deny, decision.PolicyVersion,
                "a linha do Owner não muda por aqui: transfira a propriedade antes (§9.11 last_owner)"));

            return AuthErrors.Write(context, StatusCodes.Status409Conflict,
                "last_owner", "o papel do Owner não muda por aqui: transfira a propriedade antes.");
        }

        MembershipTransition? transition = null;
        if (change is not null)
        {
            transition = MembershipRules.Apply(target.State, change.Value);
            if (transition is null)
            {
                return Invalid(context, $"transição inválida: {change} não se aplica a um membro {target.State}");
            }

            if (transition.RemovesRow && role is not null)
            {
                // reject removes the row itself: a role change beside it would have nowhere to
                // land, and silently dropping half of the body is not this route's answer.
                return Invalid(context, "reject remove a linha do membro: envie role ou reject, nunca os dois");
            }
        }

        ContainerMembership written;
        if (transition is { RemovesRow: true })
        {
            // reject: §3.3's <c>(removido)</c> — the row itself goes, and the answer names it.
            await store.DeleteMembershipAsync(slug, handle);
            written = target;
        }
        else
        {
            written = await store.UpsertMembershipAsync(target with
            {
                Role = role ?? target.Role,
                State = transition?.State ?? target.State,
            });

            // Every mapped edge touches Active on one side, so the derived counter follows the
            // write (data-model §2.2); a role change alone never moves it.
            if (transition is not null)
            {
                await RecountAsync(slug, store);
            }
        }

        // The trail row: author, when and the policy version in force (FR-013, AC-01).
        await audit.AppendAsync(new PolicyAuditEntry(
            DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.ChangeRole,
            container.Id, PolicyAudit.Allow, decision.PolicyVersion,
            JsonSerializer.Serialize(new
            {
                reason = decision.Reason,
                handle,
                before = new { role = ContainerView.Word(target.Role), state = ContainerView.Word(target.State) },
                after = transition is { RemovesRow: true }
                    ? null
                    : (object?)new { role = ContainerView.Word(written.Role), state = ContainerView.Word(written.State) },
            })));

        return Results.Json(ContainerView.Member(written));
    }

    /// <summary>
    /// data-model §2.2: <c>MemberCount</c> is derived state moved by the write that changed the
    /// membership — recomputed from the rows themselves right after that write (never at a read),
    /// so the counter and the page cannot drift apart. shortcut: a join raced by another writer
    /// can leave the count one behind until the next membership write re-derives it; upgrade to a
    /// transactional write when the storage grows one (the same limit as the create shortcut above).
    /// </summary>
    private static async Task RecountAsync(string slug, IContainerStore store)
    {
        var active = (await store.ListMembershipsAsync(slug))
            .Count(member => member.State == MembershipState.Active);
        await store.UpdateAsync(slug, null, row => row with { MemberCount = active });
    }

    /// <summary>The filter of AC-05 exactly as SPEC-0008 section E words it — Banned never, for anyone.</summary>
    private static bool Listable(MembershipState state, bool admin) =>
        state == MembershipState.Active
        || admin && state is MembershipState.Invited or MembershipState.Requested;

    /// <summary>
    /// One local actor spelled two ways — the invite's rule (GAP-13), now shared by the invite
    /// body and the moderation path: a plain handle, or a canonical actor id whose tail names it.
    /// Null when neither, so a malformed field or path segment can never address a row.
    /// </summary>
    private static string? HandleOf(string? value)
    {
        var handle = value?.Trim();
        if (string.IsNullOrEmpty(handle)) return null;

        const string Marker = "/users/";
        var index = handle.LastIndexOf(Marker, StringComparison.Ordinal);
        if (index >= 0) handle = handle[(index + Marker.Length)..].Trim();

        return ActorHandle.IsValid(handle) ? handle : null;
    }

    /// <summary>The invite target: the handle as sent, or the handle its canonical actor id ends in.</summary>
    private static string? TargetHandle(CreateInviteRequest? request) =>
        IdentityOf(request?.Handle, request?.ActorId);

    /// <summary>
    /// One local actor spelled two ways — a plain handle, or a canonical actor id whose tail
    /// names it: the invite's own rule (GAP-13), shared by every identity field of this slice
    /// (invites, succession, transfer). Null when neither, so a malformed field never addresses a row.
    /// </summary>
    internal static string? IdentityOf(string? handle, string? actorId) =>
        HandleOf(string.IsNullOrWhiteSpace(handle) ? actorId : handle);

    /// <summary>
    /// GAP-17: a section G timer from configuration — absent, malformed or non-positive keeps
    /// <paramref name="fallback"/> (the default the spec fixes) in force.
    /// </summary>
    private static int Days(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            && days > 0
            ? days
            : fallback;

    /// <summary>
    /// SPEC-0008 section B: the versioned policy write of FR-013. The order is the contract's
    /// own ladder — session (401), visibility (404, the same body as an absent slug), the
    /// authorization the hard rule of data-model §3.2 demands (403, decided by
    /// <see cref="IPolicyEvaluator"/> and never by a role <c>if</c>), the precondition (428 when
    /// <c>If-Match</c> is missing, 409 with the current policy version in the body when it is
    /// stale — nothing written either way), the payload, and only then the replacement: the
    /// policy moves to version n+1, the state version moves with it, and the trail gets the row
    /// with author, before and after (AC-01).
    /// </summary>
    private static async Task<IResult> PutPolicyAsync(
        string slug, PutPolicyRequest? request, HttpContext context,
        IContainerStore store, IPolicyAuditStore audit, IPolicyEvaluator evaluator, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var seen = await DecideVisibilityAsync(slug, await store.FindAsync(slug), context, store, audit);
        if (seen is null)
        {
            return Hidden(context);
        }

        var container = seen.Container;
        var before = container.Policy;
        var decision = evaluator.Evaluate(
            PolicyAction.Admin, RoleOf(await store.FindMembershipAsync(slug, current.Handle)), before);
        if (!decision.Permitted)
        {
            // The refusal is a decision too (SC-011): it reaches the trail before the 403 does.
            await audit.AppendAsync(new PolicyAuditEntry(
                DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.ChangePolicy,
                container.Id, PolicyAudit.Deny, decision.PolicyVersion, decision.Reason));

            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "a política do contêiner nega esta operação para o seu papel.");
        }

        if (!context.Request.Headers.TryGetValue("If-Match", out var header))
        {
            return AuthErrors.Write(context, StatusCodes.Status428PreconditionRequired,
                "precondition_required", "If-Match obrigatório: envie a versão da política vigente.");
        }

        var expected = ParseVersion(header.ToString(), out var malformed);
        if (malformed)
        {
            return Invalid(context, "If-Match inválido: informe a versão da política vigente");
        }

        if (request is null)
        {
            return Invalid(context, "corpo ausente: envie a política a aplicar");
        }

        // The precondition is the POLICY version (contract §4 tables it beside the policy body,
        // and data-model §2.3 gives that version to ContainerPolicy): a stale one changes nothing
        // — the hash-before/hash-after of AC-02 — and answers with the version now in force.
        if (expected is not null && expected.Value != before.Version)
        {
            return Conflict(context, before.Version, seen.Version);
        }

        // The body is the new policy; a field it omits keeps the value in force, and a word
        // outside the contract's vocabulary is a refusal — never a silent default (no
        // Enum.TryParse here either: it would accept "1" as a posting gate).
        var posting = request.Posting is null ? before.Posting : ParsePosting(request.Posting);
        if (posting is null)
        {
            return Invalid(context, "posting inválido: use everyone|members|moderators|admins|singlePublisher");
        }

        var visibility = request.Visibility is null ? before.Visibility : ParseVisibility(request.Visibility);
        if (visibility is null)
        {
            return Invalid(context, "visibility inválido: use public|unlisted|membersOnly|private");
        }

        var join = request.Join is null ? before.Join : ParseJoin(request.Join);
        if (join is null)
        {
            return Invalid(context, "join inválido: use open|request|invite|closed");
        }

        var interaction = before.Interaction;
        if (request.Interaction is not null)
        {
            var canComment = ParseGate(request.Interaction.CanComment, before.Interaction.CanComment);
            var canReact = ParseGate(request.Interaction.CanReact, before.Interaction.CanReact);
            var canShare = ParseGate(request.Interaction.CanShare, before.Interaction.CanShare);
            var canAttach = ParseGate(request.Interaction.CanAttach, before.Interaction.CanAttach);
            if (canComment is null || canReact is null || canShare is null || canAttach is null)
            {
                return Invalid(context, "interaction inválido: use everyone|members|moderators|admins|singlePublisher");
            }

            interaction = new InteractionPolicy(canComment.Value, canReact.Value, canShare.Value, canAttach.Value);
        }

        ContainerPolicy next;
        try
        {
            next = new ContainerPolicy(
                posting.Value, visibility.Value, join.Value, interaction,
                request.PublisherIds ?? before.PublisherIds, before.Version + 1);
        }
        catch (ArgumentException failed)
        {
            // The constructor is the authority (FR-015): SinglePublisher without a publisher id
            // is refused there, and its message is domain text — never a path or a stack.
            return Invalid(context, $"política inválida: {failed.Message}");
        }

        var updated = await store.UpdateAsync(slug, seen.Version, row => row with
        {
            Policy = next,
            // The owner's own write keeps its claim clock running (SPEC-0008 G) — anyone else's
            // leaves it alone, so a member's edit can never shield an inactive owner.
            OwnerLastActiveAt = container.OwnerActorId == ActorId(context, current.Handle)
                ? time.GetUtcNow()
                : row.OwnerLastActiveAt,
        });
        if (updated is null)
        {
            var now = await store.FindAsync(slug);
            if (now is null) return Hidden(context);
            SetETag(context, now.Version);
            return Conflict(context, now.Container.Policy.Version, now.Version);
        }

        // The Allow reaches the trail only once the replacement landed: a row that claimed a
        // change the store refused would be the one lie the trail must never tell.
        await audit.AppendAsync(new PolicyAuditEntry(
            DateTimeOffset.UtcNow, ActorId(context, current.Handle), PolicyAudit.ChangePolicy,
            container.Id, PolicyAudit.Allow, next.Version,
            JsonSerializer.Serialize(new
            {
                reason = decision.Reason,
                before = ContainerView.Policy(before),
                after = ContainerView.Policy(next),
            })));

        SetETag(context, updated.Version);
        return Results.Json(ContainerView.Of(updated.Container));
    }

    /// <summary>
    /// The visibility decision every container route executes (FR-018, SC-005), appended to the
    /// trail before the answer leaves (AC-03): <c>Allow</c> when the reader sees the container,
    /// <c>NotFound</c> when it is hidden or absent — the two are byte-identical at the wire,
    /// the trail is internal and records which one happened. Returns the state when visible and
    /// null otherwise, so the caller has one refusal to answer with. <paramref name="forRead"/>
    /// is the read half of §3.3: a write only needs the container's existence to be visible, a
    /// read also needs its reader not to be <c>Banned</c>. <paramref name="admitted"/> is a
    /// caller-proven ticket to reach that route's own rules on a container out of public sight —
    /// the join entry ticket of AC-07 and the named successor of section G — never a bypass of
    /// the soft delete, which hides the container from everyone.
    /// </summary>
    private static async Task<ContainerState?> DecideVisibilityAsync(
        string slug, ContainerState? state, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, bool forRead = false, bool admitted = false)
    {
        var container = state?.Container;
        var visible = container is not null
            && container.DeletedAt is null // soft delete: gone for every reader and every writer (SPEC-0008 G)
            && (admitted || await CanSeeAsync(container, context, store, forRead));

        await audit.AppendAsync(new PolicyAuditEntry(
            DateTimeOffset.UtcNow,
            Reader(context),
            PolicyAudit.View,
            container?.Id ?? $"{Origin(context)}/c/{slug}",
            visible ? PolicyAudit.Allow : PolicyAudit.NotFound,
            container?.Policy.Version ?? 0,
            visible
                ? "the visibility policy or an active membership admits the reader"
                : container is null
                    ? "the container does not exist (FR-018)"
                    : container.DeletedAt is not null
                        ? "the container was soft-deleted and stays hidden through its grace (SPEC-0008 G)"
                        : "the container is not visible to this reader (FR-018, SC-005)"));

        return visible ? state : null;
    }

    /// <summary>
    /// Deny by default (SPEC-0008 non-functional): only the public preset is readable without a
    /// membership; every other visibility answers the same 404 as an absent slug (FR-018, SC-005).
    /// A read additionally carries §3.3: <c>Silenced</c> reads like an active member and
    /// <c>Banned</c> reads nothing at all, whatever the preset says (AC-03, data-model §2.4) —
    /// a write keeps the pure-existence rule, so a banned actor's refusal on a public container
    /// stays the 403 the join suite already pins.
    /// </summary>
    private static async Task<bool> CanSeeAsync(
        Container container, HttpContext context, IContainerStore store, bool forRead)
    {
        var isPublic = container.Policy.Visibility == VisibilityPolicy.Public;
        var current = AuthSessions.Current(context);
        if (isPublic && !forRead) return true;

        var membership = current is null
            ? null
            : await store.FindMembershipAsync(container.Slug, current.Handle);

        if (forRead && membership?.State == MembershipState.Banned) return false;
        if (isPublic) return true;

        return membership?.State is MembershipState.Active or MembershipState.Silenced;
    }

    /// <summary>
    /// AC-07, SPEC-0008 section E: on a non-public container the join route still admits the
    /// actor whose entry the join rules must judge — an <c>Invited</c> row is the ticket of a
    /// MembersOnly group, and <c>Open</c>/<c>Request</c> create their flow for an actor with no
    /// link (or a link that ran out). <c>Invite</c> without an invitation, <c>Closed</c> and a
    /// <c>Banned</c> row answer the byte-identical 404 of an absent slug (FR-018, SC-006) — the
    /// pins of QA-0018 stay where they are.
    /// </summary>
    private static async Task<bool> CanEnterAsync(
        Container container, HttpContext context, IContainerStore store)
    {
        if (container.Policy.Visibility == VisibilityPolicy.Public) return true;

        var current = AuthSessions.Current(context);
        var membership = current is null
            ? null
            : await store.FindMembershipAsync(container.Slug, current.Handle);

        if (membership?.State is MembershipState.Active or MembershipState.Silenced or MembershipState.Invited)
        {
            return true;
        }

        return (membership?.State is null or MembershipState.Left or MembershipState.Requested)
            && container.Policy.Join is JoinPolicy.Open or JoinPolicy.Request;
    }

    /// <summary>The actor behind this request: its canonical id, or the anonymous marker.</summary>
    private static string Reader(HttpContext context)
    {
        var current = AuthSessions.Current(context);
        return current is null ? PolicyAudit.AnonymousActor : ActorId(context, current.Handle);
    }

    /// <summary>
    /// The reader's role on this container, for the evaluator (data-model §3.2): only an active
    /// membership counts — everything else arrives as <c>Guest</c>, which grants no capability.
    /// </summary>
    private static ContainerRole RoleOf(ContainerMembership? membership) =>
        membership is not { State: MembershipState.Active }
            ? ContainerRole.Guest
            : membership.Role switch
            {
                MembershipRole.Owner => ContainerRole.Owner,
                MembershipRole.Admin => ContainerRole.Admin,
                MembershipRole.Moderator => ContainerRole.Moderator,
                MembershipRole.Member => ContainerRole.Member,
                _ => ContainerRole.Guest,
            };

    /// <summary>
    /// The full actor context GAP-11 hands the evaluator (SPEC-0008 section F): role and state
    /// exactly as the row holds them, plus the publisher seat the policy gives this actor only
    /// while the membership is active — the three together, never the role alone.
    /// </summary>
    private static PolicyActor ActorContext(ContainerMembership? membership, ContainerPolicy policy) =>
        new(
            RoleOf(membership),
            membership?.State,
            membership is { State: MembershipState.Active }
                && policy.PublisherIds.Contains(membership.ActorId, StringComparer.Ordinal));

    /// <summary>
    /// The identity origin of P-02 and SPEC-0008 section A (SPEC-0011 section A): the one
    /// canonical helper — <c>Instance:Domain</c> over https when the operator published one,
    /// else scheme + Host of the very request that asked (AC-07, human gate 2026-11-08 15:57:34:
    /// one resource, one URI, whether the instance is reached over http or https).
    /// </summary>
    private static string Origin(HttpContext context) => CanonicalOrigin.Of(context);

    /// <summary>The canonical actor id of the member, shaped like the P-02 profile id.</summary>
    internal static string ActorId(HttpContext context, string handle) => $"{Origin(context)}/users/{handle}";

    /// <summary>The state version the client just proved it holds (internal ETag, decimal).</summary>
    private static void SetETag(HttpContext context, int version) =>
        context.Response.Headers.ETag = $"\"{version}\"";

    /// <summary>
    /// <c>If-Match</c> as contract §4 states it — a textual integer version — with the usual
    /// quoting tolerated. Absent means "write unconditionally"; <c>*</c> means "any existing
    /// row", which the lookup above already proved.
    /// </summary>
    private static int? ParseIfMatch(HttpContext context, out bool malformed)
    {
        malformed = false;
        if (!context.Request.Headers.TryGetValue("If-Match", out var values)) return null;
        return ParseVersion(values.ToString(), out malformed);
    }

    /// <summary>
    /// One <c>If-Match</c> value: a decimal version, quoting and <c>W/</c> tolerated. A null
    /// result with <paramref name="malformed"/> false means <c>*</c> — any version in force —
    /// which <c>PUT /policy</c> reads as "whatever the store just proved is there".
    /// </summary>
    private static int? ParseVersion(string header, out bool malformed)
    {
        malformed = false;
        var text = header.Trim();
        if (text == "*") return null;
        if (text.StartsWith("W/", StringComparison.Ordinal)) text = text[2..];
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            malformed = true;
            return null;
        }

        return version;
    }

    /// <summary>Paging parameters: absent keeps <paramref name="fallback"/>, present must parse (the trail's rule).</summary>
    private static bool TryCount(string text, int fallback, int minimum, out int value)
    {
        if (text.Length == 0)
        {
            value = fallback;
            return true;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value >= minimum;
    }

    /// <summary>409 with the policy version now in force in the body (AC-02); nothing was written.</summary>
    private static IResult Conflict(HttpContext context, int policyVersion, int stateVersion)
    {
        SetETag(context, stateVersion);
        var traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;
        return Results.Json(new
        {
            error = new
            {
                code = "version_conflict",
                message = "conflito de versão: a política vigente difere do If-Match enviado",
                traceId,
            },
            // The current version travels beside the envelope (contract §4: 409 answers "com a
            // versão atual"), so the client can retry without a second read.
            version = policyVersion,
        }, statusCode: StatusCodes.Status409Conflict);
    }

    /// <summary>The contract's posting word, or null when it is not one of the five.</summary>
    private static PostingPolicy? ParsePosting(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "everyone" => PostingPolicy.Everyone,
        "members" => PostingPolicy.Members,
        "moderators" => PostingPolicy.Moderators,
        "admins" => PostingPolicy.Admins,
        "singlepublisher" => PostingPolicy.SinglePublisher,
        _ => (PostingPolicy?)null,
    };

    /// <summary>The contract's visibility word, or null when it is not one of the four.</summary>
    private static VisibilityPolicy? ParseVisibility(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "public" => VisibilityPolicy.Public,
        "unlisted" => VisibilityPolicy.Unlisted,
        "membersonly" => VisibilityPolicy.MembersOnly,
        "private" => VisibilityPolicy.Private,
        _ => (VisibilityPolicy?)null,
    };

    /// <summary>The contract's join word, or null when it is not one of the four.</summary>
    private static JoinPolicy? ParseJoin(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "open" => JoinPolicy.Open,
        "request" => JoinPolicy.Request,
        "invite" => JoinPolicy.Invite,
        "closed" => JoinPolicy.Closed,
        _ => (JoinPolicy?)null,
    };

    /// <summary>One interaction gate: absent keeps <paramref name="fallback"/>, a word parses.</summary>
    private static PostingPolicy? ParseGate(string? value, PostingPolicy fallback) =>
        value is null ? fallback : ParsePosting(value);

    /// <summary>The contract's moderation verb (SPEC-0008 section F), or null when it is not one of the six.</summary>
    private static MembershipChange? ParseChange(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "approve" => MembershipChange.Approve,
        "reject" => MembershipChange.Reject,
        "silence" => MembershipChange.Silence,
        "unsilence" => MembershipChange.Unsilence,
        "ban" => MembershipChange.Ban,
        "unban" => MembershipChange.Unban,
        _ => (MembershipChange?)null,
    };

    /// <summary>
    /// The role word this route may write (data-model §2.4): Owner is deliberately absent —
    /// it only moves by transfer (SPEC-0008 section G), and Guest is not a member's seat.
    /// </summary>
    private static MembershipRole? ParseRole(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "admin" => MembershipRole.Admin,
        "moderator" => MembershipRole.Moderator,
        "member" => MembershipRole.Member,
        _ => null,
    };

    /// <summary>FR-018/SC-005: invisible and absent share one body, so existence never leaks.</summary>
    internal static IResult Hidden(HttpContext context) =>
        AuthErrors.Write(context, StatusCodes.Status404NotFound,
            "not_found", "contêiner não encontrado.", withTraceId: false);

    private static IResult Invalid(HttpContext context, string message) =>
        AuthErrors.Write(context, StatusCodes.Status400BadRequest, "validation_failed", message);
}
