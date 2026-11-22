using Squid.Api.Posts;
using Squid.Grains.Backup;

namespace Squid.Service;

/// <summary>
/// The instance state a backup covers, behind <see cref="IBackupStateSource"/>: the canonical
/// activity log of SPEC-0009 section A, windowed by the global sequence R-15 fixes ("delta é
/// <c>WHERE seq &gt; @lastBackedUpSeq</c>" with a global record of the last consolidated seq) —
/// the same number the envelope's <c>seqFrom/seqTo</c> claim, so the interval on the envelope and
/// the interval in the payload are the same one by construction. It lives in the composition root
/// because only this layer may reach <c>IActivityStore</c> and open the database (ADR-35).
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
        // ReadAsync is already the delta read (Seq > seqFrom); the upper bound closes the window
        // the envelope will claim, so a run never serializes beyond its own seqTo.
        var events = await activities.ReadAsync(seqFrom);
        var window = events
            .Where(activity => activity.Seq <= seqTo)
            .OrderBy(activity => activity.Seq)
            .ToList();
        return new BackupActivitySnapshot(instanceId, seqFrom, seqTo, window);
    }
}

/// <summary>
/// What one backup run serializes: the instance it belongs to and the activity window the
/// envelope covers. The Worker compresses and ciphers it; no key and no plaintext ever leaves.
/// </summary>
public sealed record BackupActivitySnapshot(
    string InstanceId,
    long SeqFrom,
    long SeqTo,
    IReadOnlyList<ActivityEvent> Events);
