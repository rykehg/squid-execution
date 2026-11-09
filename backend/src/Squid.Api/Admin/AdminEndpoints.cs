using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Squid.Api.Auth;
using Squid.Api.Containers;

namespace Squid.Api.Admin;

/// <summary>
/// The administration surface SPEC-0008 section D opens in this phase: the operator gate every
/// <c>/api/admin/*</c> route of the phase must pass, and the audit query of contracts/http-api.md
/// §7 that the gate protects. The remaining admin routes (reports, suspend, peers, backup) belong
/// to the phases that own them — this file grows only what the phase's tasks table it.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Page size when the caller asks for none; a hard ceiling so one query cannot dump the trail.</summary>
    private const int DefaultLimit = 100;

    private const int MaxLimit = 1000;

    /// <summary>Maps the admin routes; runs after <c>MapSquidAuth</c>, on the session pipeline.</summary>
    public static WebApplication MapSquidAdmin(this WebApplication app)
    {
        app.MapGet("/api/admin/audit", AuditAsync);
        // SPEC-0008 section G: the operator's override of the ownership — the second transfer
        // route of the phase, behind this file's own gate.
        app.MapPost("/api/admin/containers/{slug}/transfer", TransferAsync);
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
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        if (!IsOperator(configuration, current.Handle))
        {
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "a rota exige o papel de operador da instância.");
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
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        if (!IsOperator(configuration, current.Handle))
        {
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "a rota exige o papel de operador da instância.");
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

        var operatorActorId = ContainerEndpoints.ActorId(context, current.Handle);
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
