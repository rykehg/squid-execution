using Squid.Api.Posts;
using Squid.Grains.Backup;
using Squid.Worker.Backup;

namespace Squid.Service;

/// <summary>
/// The instance state a backup covers, behind <see cref="IBackupStateSource"/>: the canonical
/// activity log of SPEC-0009 section A, windowed by the global sequence R-15 fixes ("delta é
/// <c>WHERE seq &gt; @lastBackedUpSeq</c>" with a global record of the last consolidated seq) —
/// the same number the envelope's <c>seqFrom/seqTo</c> claim, so the interval on the envelope and
/// the interval in the payload are the same one by construction. It lives in the composition root
/// because only this layer may reach <c>IActivityStore</c> and open the database (ADR-35); the
/// snapshot record itself lives with the Worker that serializes and restores it (TASK-0056).
/// </summary>
public sealed class SqliteBackupStateSource(IActivityStore activities) : IBackupStateSource
{
    /// <inheritdoc />
    public async Task<long> LastConsolidatedSeqAsync(string instanceId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // shortcut: the port reads the whole document and keeps its last number — a dedicated
        // "current seq" read lands with the windowed incremental policy of R-15 (TASK-0056+).
        var events = await activities.ReadAsync(0);
        return events.Count == 0 ? 0 : events[^1].Seq;
    }

    /// <inheritdoc />
    public async Task<object> SnapshotAsync(string instanceId, long seqFrom, long seqTo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // The port's delta read is exclusive (Seq > afterSeq) while the envelope claims the
        // INCLUSIVE interval [seqFrom..seqTo] (contract section 2), so the window reads one below
        // its own floor: seqFrom itself is the first event of the delta the runner derived as
        // previous seqTo + 1 — without it every incremental backup would drop that boundary event
        // and a restored chain would lose one event per run (TASK-0056's drills caught exactly
        // that). The upper bound still closes the window, so a run never serializes beyond its
        // own seqTo.
        var events = await activities.ReadAsync(seqFrom - 1);
        var window = events
            .Where(activity => activity.Seq <= seqTo)
            .OrderBy(activity => activity.Seq)
            .ToList();
        return new BackupActivitySnapshot(instanceId, seqFrom, seqTo, window);
    }
}
