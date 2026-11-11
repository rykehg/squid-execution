namespace Squid.Api.Posts;

/// <summary>The queue states of a report (SPEC-0009 section I): open until an operator resolves it.</summary>
public static class ReportStatus
{
    /// <summary>The report waits in the operator's queue.</summary>
    public const string Open = "open";

    /// <summary>An operator resolved it — <c>keep</c>, <c>remove</c> or <c>ban</c> (contract §7).</summary>
    public const string Resolved = "resolved";
}

/// <summary>
/// One denunciation of FR-028: who reported which post and why, plus the resolution when an
/// operator closes it. The identity of the row is the pair <c>(PostId, ReporterActorId)</c> —
/// GAP-28's idempotency key — while <see cref="Id"/> is only the opaque handle the admin routes
/// address it by.
/// </summary>
public sealed record Report(
    string Id,
    string PostId,
    string ReporterActorId,
    string AuthorActorId,
    string Reason,
    string Status,
    int PolicyVersion,
    DateTimeOffset CreatedAt,
    string? Action = null,
    string? Justification = null,
    DateTimeOffset? ResolvedAt = null);

/// <summary>The queue query of <c>GET /api/admin/reports</c> (contract §7): the two filters §7 names plus the pagination.</summary>
public sealed record ReportFilter(
    string? Post = null,
    string? Status = null,
    int Limit = 100,
    int Offset = 0);

/// <summary>
/// The denunciation queue (SPEC-0009 section I, ADR-35: the edge declares it, <c>Squid.Service</c>
/// implements it over SQLite). <see cref="CreateAsync"/> is the idempotent gate GAP-28 asks for —
/// the same reporter on the same post never gets a second row — and the interface offers no
/// delete: a resolved report stays in the queue, exactly like the trail it was decided on.
/// </summary>
public interface IReportStore
{
    /// <summary>
    /// Inserts <paramref name="report"/> unless the pair (post, reporter) already has one, in
    /// which case the stored report answers with <c>Created = false</c> — the repeat of AC-01
    /// returns THE report, never a duplicate and never a second one.
    /// </summary>
    Task<(Report Report, bool Created)> CreateAsync(Report report);

    /// <summary>The report of <paramref name="id"/>, or null when the queue has no such row.</summary>
    Task<Report?> FindAsync(string id);

    /// <summary>
    /// Applies <paramref name="mutate"/> to the report of <paramref name="id"/> and persists it
    /// with compare-and-swap, re-reading on conflict. False when no such report exists —
    /// nothing is created on the side (the rule <c>IAuthStore.UpdateAsync</c> follows).
    /// </summary>
    Task<bool> UpdateAsync(string id, Func<Report, Report> mutate);

    /// <summary>The matching reports, newest first, paged — never reordered between calls.</summary>
    Task<IReadOnlyList<Report>> QueryAsync(ReportFilter filter);
}
