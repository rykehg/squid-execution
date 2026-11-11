using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Squid.Api.Auth;
using Squid.Api.Containers;

namespace Squid.Api.Posts;

/// <summary>The denunciation body of SPEC-0009 section I: the reason the reporter gives (FR-028).</summary>
public sealed record ReportRequest(string? Reason);

/// <summary>
/// <c>POST /api/posts/{id}/report</c> (contract §5, SPEC-0009 section I, FR-028): the session
/// ladder every post route walks first (401), then the byte-identical 404 for an absent or
/// invisible post — the very same <c>MissingAsync</c>/<c>HiddenPost</c> pair the read route
/// answers with, so existence never leaks (SC-005, AC-01) — then the required non-empty
/// <c>reason</c> (§9.12's family: 422 as pure payload validation, no trail row), and only then
/// the write: one row per (post, reporter), idempotent by GAP-28 — the first call is 201 with
/// the new report, the repeat answers the SAME report with 200 and the queue never grows a
/// duplicate (AC-01). The route itself is mapped by <see cref="PostEndpoints"/>, whose single
/// POST catch-all reads the <c>/report</c> suffix off the tail and lands here.
/// </summary>
internal static class ReportEndpoints
{
    /// <summary>The handler — the ladder of the class summary, in order.</summary>
    internal static async Task<IResult> ReportAsync(
        string id, ReportRequest? request, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, IObjectStore objects, IReportStore reports, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var post = await objects.FindAsync(id);
        if (post is null)
        {
            return await PostEndpoints.MissingAsync(context, audit, id);
        }

        // The reader must see the post before they may denounce it — the read half of §3.3 the
        // GET walks, so a stranger behind a closed container meets the byte-identical 404 (SC-005).
        if (await PostEndpoints.VisibleAsync(
                (await store.FindAsync(post.ContainerSlug))?.Container, post.ContainerId,
                context, store, audit, forRead: true) is null)
        {
            return PostEndpoints.HiddenPost(context);
        }

        var reason = request?.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "validation_failed", "reason obrigatório e não vazio na denúncia (FR-028).");
        }

        var reporter = ContainerEndpoints.ActorId(context, current.Handle);
        var (report, created) = await reports.CreateAsync(new Report(
            NewToken(), post.Id, reporter, post.AuthorActorId, reason,
            ReportStatus.Open, post.PolicyVersion, time.GetUtcNow()));

        return created
            ? Results.Json(ReportView.Of(report), statusCode: StatusCodes.Status201Created)
            : Results.Json(ReportView.Of(report)); // GAP-28: the repeat answers the report that already exists
    }

    /// <summary>The opaque id of a report: 128 random bits, hex, never guessable from content.</summary>
    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
