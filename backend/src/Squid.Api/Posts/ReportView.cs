namespace Squid.Api.Posts;

/// <summary>
/// One report on the wire: the fields of data-model's report row camelCased like every other
/// response (SPEC-0009 section I) — the queue page and the single-report answers share this
/// shape, so a caller never has to tell which call it is looking at.
/// </summary>
internal static class ReportView
{
    /// <summary>The report as the routes answer it; <c>action</c>/<c>justification</c>/<c>resolvedAt</c> are null while it is open.</summary>
    public static object Of(Report report) => new
    {
        id = report.Id,
        postId = report.PostId,
        reporter = report.ReporterActorId,
        author = report.AuthorActorId,
        reason = report.Reason,
        status = report.Status,
        createdAt = report.CreatedAt,
        action = report.Action,
        justification = report.Justification,
        resolvedAt = report.ResolvedAt,
    };
}
