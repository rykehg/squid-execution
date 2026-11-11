using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Api.Posts;

namespace Squid.Api.Admin;

/// <summary>The resolution body of section I: which of the three actions and why (contract §7).</summary>
public sealed record ResolveRequest(string? Action, string? Justification);

/// <summary>The suspension body: §7 fixes no fields, FR-008 owes the justification.</summary>
public sealed record SuspendRequest(string? Justification);

/// <summary>
/// The administration surface SPEC-0008 section D opens in this phase: the operator gate every
/// <c>/api/admin/*</c> route of the phase must pass, the audit query of contracts/http-api.md
/// §7 that the gate protects, and — since SPEC-0009 section I (TASK-0031, FR-028/FR-008) — the
/// denunciation queue, its <c>keep|remove|ban</c> resolution and the direct suspension of §7.
/// The remaining admin routes (peers, backup) belong to the phases that own them — this file
/// grows only what the phase's tasks table it.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Page size when the caller asks for none; a hard ceiling so one query cannot dump the trail.</summary>
    private const int DefaultLimit = 100;

    private const int MaxLimit = 1000;

    /// <summary>The three resolutions contract §7 accepts on a report.</summary>
    private const string ActionKeep = "keep";

    private const string ActionRemove = "remove";

    private const string ActionBan = "ban";

    /// <summary>
    /// The trail word of a resolution (SPEC-0009 section I, GAP-27): new in the §2.11 family,
    /// kept local exactly like <c>PostEndpoints.RemoveContentDecision</c> — the shared constants
    /// of <c>IPolicyAuditStore</c> live in the container files this task may not edit.
    /// </summary>
    private const string ResolveDecision = "Resolve";

    /// <summary>The suffix of the one route under <c>/api/admin/actors</c> (contract §7).</summary>
    private const string SuspendSuffix = "/suspend";

    /// <summary>Maps the admin routes; runs after <c>MapSquidAuth</c>, on the session pipeline.</summary>
    public static WebApplication MapSquidAdmin(this WebApplication app)
    {
        app.MapGet("/api/admin/audit", AuditAsync);
        // SPEC-0008 section G: the operator's override of the ownership — the second transfer
        // route of the phase, behind this file's own gate.
        app.MapPost("/api/admin/containers/{slug}/transfer", TransferAsync);
        // SPEC-0009 section I (FR-028): the denunciation queue and its resolution, behind the
        // same gate — the queue is the operator's, never a reader's (contract §7).
        app.MapGet("/api/admin/reports", ReportsAsync);
        app.MapPost("/api/admin/reports/{id}/resolve", ResolveAsync);
        // The suspension of §7: the actor id may be the whole canonical URI, so the /suspend
        // suffix rides a catch-all exactly like the post routes' id (contract §7, GAP-27 family).
        app.MapPost("/api/admin/actors/{*target}", ActorTargetAsync);
        return app;
    }

    /// <summary>
    /// <c>GET /api/admin/audit?actor=&amp;action=&amp;from=</c> (contract §7, FR-019/SC-011): the
    /// append-only trail, newest first. Not a session but an OPERATOR sees it — no session is 401,
    /// a session of a handle outside <c>Instance:Operators</c> is 403 <c>policy_denied</c>
    /// (SPEC-0008 section D), and the filters are the contract's own three plus the pagination
    /// section C asks for.
    /// </summary>
    private static async Task<IResult> AuditAsync(
        HttpContext context, IPolicyAuditStore store, IConfiguration configuration)
    {
        var (_, refusal) = Gate(context, configuration);
        if (refusal is not null)
        {
            return refusal;
        }

        var query = context.Request.Query;
        if (!TryInstant(query["from"].ToString(), out var from))
        {
            return Invalid(context, "from inválido: informe um instante ISO 8601");
        }

        if (!TryCount(query["limit"].ToString(), DefaultLimit, 1, out var limit)
            || !TryCount(query["offset"].ToString(), 0, 0, out var offset))
        {
            return Invalid(context, "limit/offset inválidos: inteiros não negativos (limit ≥ 1)");
        }

        if (limit > MaxLimit) limit = MaxLimit; // the ceiling the constant promises, not a refusal

        var items = await store.QueryAsync(new PolicyAuditFilter(
            Text(query["actor"]), Text(query["action"]), from, limit, offset));

        return Results.Json(new { items = items.Select(Row) });
    }

    /// <summary>
    /// <c>POST /api/admin/containers/{slug}/transfer</c> (SPEC-0008 section G, contract §7/§9.12):
    /// the operator forces the ownership move — new Owner <c>Active</c>, old Owner <c>Admin</c>.
    /// The ladder is section D's gate first (no session 401, non-operator 403 <c>policy_denied</c>,
    /// the gate itself writes no row — the precedent of <c>/api/admin/audit</c>), then the
    /// container (404, deleted or absent alike), then payload: empty <c>justification</c> is the
    /// 422 of §9.12 and a bad target the 400 — validations evaluate no policy and add no row
    /// (SPEC-0008 C). The Allow row carries the justification §7 demands beside author and version.
    /// </summary>
    private static async Task<IResult> TransferAsync(
        string slug, TransferRequest? request, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, IConfiguration configuration, TimeProvider time)
    {
        var (current, refusal) = Gate(context, configuration);
        if (refusal is not null)
        {
            return refusal;
        }

        var state = await store.FindAsync(slug);
        if (state is null || state.Container.DeletedAt is not null)
        {
            return ContainerEndpoints.Hidden(context);
        }

        var container = state.Container;
        var justification = request?.Justification?.Trim();
        if (string.IsNullOrEmpty(justification))
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "validation_failed", "justification exige uma justificativa não vazia.");
        }

        if (justification.Length > ContainerEndpoints.MaxJustificationLength)
        {
            return Invalid(context, $"justificativa inválida: máximo de {ContainerEndpoints.MaxJustificationLength} caracteres");
        }

        var newOwner = ContainerEndpoints.IdentityOf(request?.Handle, request?.ActorId);
        if (newOwner is null)
        {
            return Invalid(context, "novo Owner inválido: envie handle ou actorId de um ator local");
        }

        var operatorActorId = ContainerEndpoints.ActorId(context, current!.Handle);
        var newOwnerActorId = ContainerEndpoints.ActorId(context, newOwner);
        var now = time.GetUtcNow();
        var updated = await Ownership.MoveAsync(store, container, newOwner, newOwnerActorId, now);

        await audit.AppendAsync(new PolicyAuditEntry(
            now, operatorActorId, PolicyAudit.Transfer, container.Id, PolicyAudit.Allow,
            container.Policy.Version,
            JsonSerializer.Serialize(new
            {
                justification,
                by = operatorActorId,
                from = container.OwnerActorId,
                to = newOwnerActorId,
            })));

        return Results.Json(ContainerView.Of(updated));
    }

    /// <summary>
    /// <c>GET /api/admin/reports</c> (contract §7, FR-028): the operator's denunciation queue,
    /// newest first. The ladder is section D's gate first (no session 401, non-operator 403
    /// <c>policy_denied</c>), then the paging the audit query already answers and the two
    /// filters §7 names — <c>post</c> (the canonical id or its tail, like every id on this wire)
    /// and <c>status</c> (<c>open</c>/<c>resolved</c>) — invalid input a 400, never a silent
    /// match-all. The gate writes no row (the precedent of <c>/api/admin/audit</c>).
    /// </summary>
    private static async Task<IResult> ReportsAsync(
        HttpContext context, IReportStore reports, IConfiguration configuration)
    {
        var (_, refusal) = Gate(context, configuration);
        if (refusal is not null)
        {
            return refusal;
        }

        var query = context.Request.Query;
        if (!TryCount(query["limit"].ToString(), DefaultLimit, 1, out var limit)
            || !TryCount(query["offset"].ToString(), 0, 0, out var offset))
        {
            return Invalid(context, "limit/offset inválidos: inteiros não negativos (limit ≥ 1)");
        }

        if (limit > MaxLimit) limit = MaxLimit; // the ceiling the constant promises, not a refusal

        var status = Text(query["status"].ToString());
        if (status is not null
            && !string.Equals(status, ReportStatus.Open, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, ReportStatus.Resolved, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(context, "status inválido: informe open ou resolved");
        }

        // The contract fixes no spelling for the post filter, so the id and its opaque tail both
        // answer — the same two spellings every post route already accepts.
        var post = Text(query["post"].ToString()) ?? Text(query["postId"].ToString());

        var items = await reports.QueryAsync(new ReportFilter(post, status, limit, offset));
        return Results.Json(new { items = items.Select(ReportView.Of) });
    }

    /// <summary>
    /// <c>POST /api/admin/reports/{id}/resolve</c> (contract §7, SPEC-0009 section I): gate,
    /// then the report (404 when the queue has no such row), then the payload — empty
    /// <c>justification</c> is the 422 of §9.12's family and an unknown <c>action</c> the 400,
    /// both pure validation with no trail row (SPEC-0008 C). <c>keep</c> closes the report
    /// audit-only; <c>remove</c> executes the very removal of section H in the OPERATOR's name
    /// through <c>PostEndpoints.RemoveContentAsync</c> (tombstone + <c>Activity: Delete</c> +
    /// <c>RemoveContent</c> row — never a fork of that logic); <c>ban</c> suspends the report's
    /// author through <c>IActorStatusStore.SuspendAsync</c>, the same §3.1 state the publish
    /// ladder refuses. Every resolution appends one <c>Resolve</c> row with author, target and
    /// justification (FR-008, GAP-27), and resolving an already resolved report answers it as
    /// it stands — the first resolution wins, a repeat never writes a second row.
    /// </summary>
    private static async Task<IResult> ResolveAsync(
        string id, ResolveRequest? request, HttpContext context, IReportStore reports,
        IObjectStore objects, IActivityStore activities, IActorStatusStore statuses,
        IPolicyAuditStore audit, IConfiguration configuration, TimeProvider time)
    {
        var (current, refusal) = Gate(context, configuration);
        if (refusal is not null)
        {
            return refusal;
        }

        var report = await reports.FindAsync(id);
        if (report is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status404NotFound,
                "not_found", "denúncia não encontrada.");
        }

        if (report.Status == ReportStatus.Resolved)
        {
            return Results.Json(ReportView.Of(report));
        }

        var justification = request?.Justification?.Trim();
        if (string.IsNullOrEmpty(justification))
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "validation_failed", "justification exige uma justificativa não vazia.");
        }

        if (justification.Length > ContainerEndpoints.MaxJustificationLength)
        {
            return Invalid(context, $"justificativa inválida: máximo de {ContainerEndpoints.MaxJustificationLength} caracteres");
        }

        var action = request?.Action?.Trim().ToLowerInvariant();
        if (action is not (ActionKeep or ActionRemove or ActionBan))
        {
            return Invalid(context, "action inválido: informe keep, remove ou ban");
        }

        var now = time.GetUtcNow();
        var operatorId = ContainerEndpoints.ActorId(context, current!.Handle);

        if (action == ActionRemove)
        {
            // The post may be gone already (its author removed it first): the content is out of
            // every read either way, so the report still closes with nothing left to execute.
            var post = await objects.FindAsync(report.PostId);
            if (post is not null)
            {
                await PostEndpoints.RemoveContentAsync(
                    post, operatorId, justification, report.PolicyVersion, now,
                    objects, activities, audit);
            }
        }
        else if (action == ActionBan)
        {
            var authorHandle = ContainerEndpoints.IdentityOf(null, report.AuthorActorId)
                ?? throw new IOException($"the author of report {report.Id} is not a local handle");
            await statuses.SuspendAsync(authorHandle);
        }

        var resolved = report with
        {
            Status = ReportStatus.Resolved,
            Action = action,
            Justification = justification,
            ResolvedAt = now,
        };
        await reports.UpdateAsync(resolved.Id, _ => resolved);

        await audit.AppendAsync(new PolicyAuditEntry(
            now, operatorId, ResolveDecision, report.Id, PolicyAudit.Allow, report.PolicyVersion,
            JsonSerializer.Serialize(new
            {
                action,
                justification,
                postId = report.PostId,
                reporter = report.ReporterActorId,
                author = report.AuthorActorId,
            })));

        return Results.Json(ReportView.Of(resolved));
    }

    /// <summary>
    /// <c>POST /api/admin/actors/{id}/suspend</c> (contract §7, data-model §3.1): the direct
    /// suspension the <c>ban</c> resolution was born from — same ladder, same store, same
    /// §9.12 justification rule (empty → 422). The id is a handle or the canonical actor id
    /// (the invite's GAP-13 spelling, <c>IdentityOf</c>), an unknown actor the 404 of §8, and
    /// the write is <c>IActorStatusStore.SuspendAsync</c> itself: repeating it changes nothing
    /// (§3.1 has no row to double). The trail row is <c>ChangeRole</c> — the data-model §2.11
    /// word for state changes — with author, target and justification (FR-008).
    /// </summary>
    private static async Task<IResult> SuspendAsync(
        string id, SuspendRequest? request, HttpContext context, IConfiguration configuration,
        IAuthStore auth, IActorStatusStore statuses, IPolicyAuditStore audit, TimeProvider time)
    {
        var (current, refusal) = Gate(context, configuration);
        if (refusal is not null)
        {
            return refusal;
        }

        var handle = ContainerEndpoints.IdentityOf(null, id);
        if (handle is null)
        {
            return Invalid(context, "identificador de ator inválido: envie handle ou actorId local");
        }

        var account = await auth.FindAsync(handle);
        if (account is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status404NotFound,
                "not_found", "ator não encontrado.");
        }

        var justification = request?.Justification?.Trim();
        if (string.IsNullOrEmpty(justification))
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "validation_failed", "justification exige uma justificativa não vazia.");
        }

        if (justification.Length > ContainerEndpoints.MaxJustificationLength)
        {
            return Invalid(context, $"justificativa inválida: máximo de {ContainerEndpoints.MaxJustificationLength} caracteres");
        }

        var actorId = ContainerEndpoints.ActorId(context, handle);
        await statuses.SuspendAsync(handle);
        await audit.AppendAsync(new PolicyAuditEntry(
            time.GetUtcNow(), ContainerEndpoints.ActorId(context, current!.Handle),
            PolicyAudit.ChangeRole, actorId, PolicyAudit.Allow, 0,
            JsonSerializer.Serialize(new { justification, status = "suspended" })));

        return Results.Json(new { id = actorId, status = "suspended" });
    }

    /// <summary>
    /// The one POST route under <c>/api/admin/actors</c> (contract §7): the suffix decides —
    /// <c>/suspend</c> lands on the handler, anything else answers like nothing is there. The
    /// suffix rides a catch-all because an actor id is a whole canonical URI, plain or
    /// percent-encoded, exactly as the post routes read it off their tail.
    /// </summary>
    private static async Task<IResult> ActorTargetAsync(
        string target, SuspendRequest? request, HttpContext context, IConfiguration configuration,
        IAuthStore auth, IActorStatusStore statuses, IPolicyAuditStore audit, TimeProvider time)
    {
        var path = Uri.UnescapeDataString(target ?? string.Empty).Trim().TrimEnd('/');
        if (!path.EndsWith(SuspendSuffix, StringComparison.Ordinal))
        {
            return AuthErrors.Write(context, StatusCodes.Status404NotFound,
                "not_found", "rota não encontrada.");
        }

        return await SuspendAsync(
            path[..^SuspendSuffix.Length], request, context, configuration, auth, statuses, audit, time);
    }

    /// <summary>
    /// The gate of every <c>/api/admin/*</c> route (contract §7, SPEC-0008 section D): no
    /// session is 401, a session of a handle outside <c>Instance:Operators</c> is 403
    /// <c>policy_denied</c>, and an operator gets the caller back with no refusal. The gate
    /// itself writes no row — the precedent of <c>/api/admin/audit</c>.
    /// </summary>
    private static (AuthSessions.Resolved? Current, IResult? Refusal) Gate(
        HttpContext context, IConfiguration configuration)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return (null, AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada."));
        }

        if (!IsOperator(configuration, current.Handle))
        {
            return (null, AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "a rota exige o papel de operador da instância."));
        }

        return (current, null);
    }

    /// <summary>
    /// FR-008's minimum (GAP-14): an operator is a handle named by the <c>Instance:Operators</c>
    /// list of the configuration — comma separated, case-insensitive, no management route (that
    /// is a later phase's job). Section D words the list "handles/actor ids", so a candidate that
    /// is this handle's actor id under any origin names the same operator. An instance with no
    /// list has no operator, so the gate fails closed.
    /// </summary>
    private static bool IsOperator(IConfiguration configuration, string handle)
    {
        var configured = configuration["Instance:Operators"];
        if (string.IsNullOrWhiteSpace(configured)) return false;

        foreach (var candidate in configured.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(candidate, handle, StringComparison.OrdinalIgnoreCase)) return true;
            if (candidate.EndsWith("/users/" + handle, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>One trail row on the wire: data-model §2.11's fields, camelCased like the rest.</summary>
    private static object Row(PolicyAuditEntry entry) => new
    {
        at = entry.At,
        actorId = entry.ActorId,
        action = entry.Action,
        resourceId = entry.ResourceId,
        result = entry.Result,
        policyVersion = entry.PolicyVersion,
        reason = entry.Reason,
    };

    /// <summary>An absent or empty query parameter is "no filter", never a match on "".</summary>
    private static string? Text(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static bool TryInstant(string text, out DateTimeOffset? value)
    {
        value = null;
        if (text.Length == 0) return true;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Paging parameters: absent keeps <paramref name="fallback"/>, present must parse.</summary>
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

    private static IResult Invalid(HttpContext context, string message) =>
        AuthErrors.Write(context, StatusCodes.Status400BadRequest, "validation_failed", message);
}
