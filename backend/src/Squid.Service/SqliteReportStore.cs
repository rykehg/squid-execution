using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Posts;

namespace Squid.Service;

/// <summary>
/// <see cref="IReportStore"/> over the same SQLite <c>IGrainStorage</c> the trail and the status
/// rows use (pattern <c>SqlitePolicyAuditStore</c>): no new table and no migration — one keyed
/// document holds the queue, the create is the same read-modify-write under the ETag, and the
/// (post, reporter) check of GAP-28 lives inside that critical section, so two racing denunciations
/// of the same pair land one row.
/// </summary>
public sealed class SqliteReportStore : IReportStore
{
    /// <summary>Store name of the queue rows (one document with every report).</summary>
    public const string StoreName = "reports";

    /// <summary>Re-reads before giving up when the write races another writer.</summary>
    private const int Attempts = 3;

    private readonly IGrainStorage _storage;

    /// <summary>Binds the queue to the storage the host already created (schema exists with it).</summary>
    public SqliteReportStore(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    private static GrainId Row() => GrainId.Create("report", "queue");

    /// <inheritdoc />
    public async Task<(Report Report, bool Created)> CreateAsync(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<QueueDocument>();
            await _storage.ReadStateAsync(StoreName, Row(), read);

            // shortcut: the whole queue rides in one document, so a write rewrites it and the
            // query sorts in memory — enough for this phase's volume and the only shape the
            // storage offers (IGrainStorage has no scan); shard the queue by period when the
            // document outgrows a comfortable rewrite, the interface will not change.
            var document = read.State ?? new QueueDocument([]);

            // GAP-28: the pair (post, reporter) is the identity of a denunciation — the repeat
            // answers the row already stored, never a second one.
            var existing = document.Reports.FirstOrDefault(candidate =>
                string.Equals(candidate.PostId, report.PostId, StringComparison.Ordinal)
                && string.Equals(candidate.ReporterActorId, report.ReporterActorId, StringComparison.Ordinal));
            if (existing is not null)
            {
                return (existing, false);
            }

            document.Reports.Add(report);
            try
            {
                await _storage.WriteStateAsync(
                    StoreName, Row(), new GrainState<QueueDocument>(document, read.ETag));
                return (report, true);
            }
            catch (InconsistentStateException)
            {
                // Another writer landed in between: re-read and look for the pair again — the
                // concurrent denunciation may BE the existing report this call must answer.
            }
        }

        throw new IOException($"the report queue kept conflicting after {Attempts} attempts");
    }

    /// <inheritdoc />
    public async Task<Report?> FindAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var state = new GrainState<QueueDocument>();
        await _storage.ReadStateAsync(StoreName, Row(), state);
        return state.State?.Reports.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(string id, Func<Report, Report> mutate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(mutate);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<QueueDocument>();
            await _storage.ReadStateAsync(StoreName, Row(), read);
            var document = read.State;
            if (document is null) return false;

            var index = document.Reports.FindIndex(candidate =>
                string.Equals(candidate.Id, id, StringComparison.Ordinal));
            if (index < 0) return false;

            document.Reports[index] = mutate(document.Reports[index]);
            try
            {
                await _storage.WriteStateAsync(
                    StoreName, Row(), new GrainState<QueueDocument>(document, read.ETag));
                return true;
            }
            catch (InconsistentStateException)
            {
                // Another writer moved the row in between: re-read it and apply again.
            }
        }

        throw new IOException($"the report '{id}' kept conflicting after {Attempts} attempts");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Report>> QueryAsync(ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var state = new GrainState<QueueDocument>();
        await _storage.ReadStateAsync(StoreName, Row(), state);
        var reports = state.State?.Reports ?? [];

        // Newest first, insertion line as the stable tiebreak (the trail's rule): two reports in
        // the same instant keep the order they were written in, so paging never skips nor repeats.
        return reports
            .Select((report, line) => (report, line))
            .Where(match => Matches(match.report, filter))
            .OrderByDescending(match => match.report.CreatedAt)
            .ThenByDescending(match => match.line)
            .Skip(filter.Offset)
            .Take(filter.Limit)
            .Select(match => match.report)
            .ToList();
    }

    /// <summary>
    /// The queue's two filters: <c>post</c> matches the canonical id or its opaque tail (the
    /// caller has either spelling in hand, like every id on this wire), <c>status</c> exact
    /// under any casing the wire spelled it.
    /// </summary>
    private static bool Matches(Report report, ReportFilter filter)
    {
        if (filter.Post is not null
            && !string.Equals(report.PostId, filter.Post, StringComparison.OrdinalIgnoreCase)
            && !report.PostId.EndsWith("/" + filter.Post, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filter.Status is not null
            && !string.Equals(report.Status, filter.Status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>The one row of the queue: a growing list under a single ETag (see the shortcut).</summary>
    private sealed record QueueDocument(List<Report> Reports);
}
