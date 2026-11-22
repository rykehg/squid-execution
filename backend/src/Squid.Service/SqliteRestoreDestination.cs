using Squid.Api.Posts;
using Squid.Persistence;
using Squid.ReadModels.Projections;
using Squid.ReadModels.Quota;
using Squid.Worker.Backup;
using Squid.Worker.Quota;

namespace Squid.Service;

/// <summary>
/// <see cref="IRestoreDestination"/> over this composition root's own pieces (contract
/// backup-recovery section 4): <see cref="PrepareAsync"/> brings a lost database back with its
/// state table and its versioned schema — the same two DDL paths every host boot already runs —
/// and <see cref="ReapplyAsync"/> is step 6 in full: the restored log (sequences preserved by
/// <c>SqliteActivityStore.RestoreAsync</c>), the projection pass the pump would run over what
/// actually landed, the quota ledger reconciled from the blob volume the restore never touches,
/// and the log read back so step 7 compares against reality instead of against intent. It lives
/// here because only this layer may open the database (ADR-35).
/// </summary>
public sealed class SqliteRestoreDestination : IRestoreDestination
{
    private readonly string _connectionString;
    private readonly SqliteActivityStore _activities;
    private readonly SqliteActivityLog _log;
    private readonly IActivityProjector _projector;
    private readonly IQuotaLedger _quota;
    private readonly string _blobRoot;
    private readonly TimeProvider _time;

    /// <summary>Wires the destination to the file, the log, the read side and the blob volume it reconciles.</summary>
    public SqliteRestoreDestination(
        string connectionString,
        SqliteActivityStore activities,
        SqliteActivityLog log,
        IActivityProjector projector,
        IQuotaLedger quota,
        string blobRoot,
        TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _quota = quota ?? throw new ArgumentNullException(nameof(quota));
        ArgumentException.ThrowIfNullOrWhiteSpace(blobRoot);
        _blobRoot = blobRoot;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task PrepareAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A lost file comes back through the two DDL paths the host itself owns: the state table
        // (SqliteGrainStorage's constructor) and the versioned schema (the same idempotent Apply
        // SquidServiceHost.Build runs) — nothing hand-written, nothing dropped on an intact file.
        _ = new SqliteGrainStorage(_connectionString);
        SchemaMigrations.Apply(_connectionString);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActivityEvent>> ReapplyAsync(
        BackupActivitySnapshot state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _activities.RestoreAsync(state.Events);

        // The projection pass exactly as the pump runs it (SPEC-0009 section B): start at the
        // read side's own high-water mark and consume what landed — a fresh destination projects
        // the whole restored log, an intact one only what it did not have yet (AC-04 of TASK-0024).
        var since = await _projector.LastProjectedSeqAsync();
        foreach (var entry in await _log.ReadAsync(since))
        {
            ct.ThrowIfCancellationRequested();
            await _projector.ProjectAsync(entry);
        }

        // The quota ledger's source of truth is the blob volume (SPEC-0009 section F), which the
        // envelope deliberately does not cover (GAP-55) and the restore never touches: one sweep
        // makes the ledger confer with it again (contract section 4 step 6).
        await new QuotaReconciler(_quota, _blobRoot, _time).ReconcileAsync(ct);

        return await _activities.ReadAsync(0);
    }
}
