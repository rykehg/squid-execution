namespace Squid.Grains.Backup;

/// <summary>
/// The orchestrator of SPEC-0012 section A and contract backup-recovery section 3, keyed by
/// <c>instanceId</c>: read the last consolidated sequence → ask the aggregate for the snapshot →
/// hand both to the Worker, which is the only serializer, cipher and sender of the flow.
/// <para>
/// It is a plain class on purpose, exactly like <c>ActorGrain</c>/<c>PeerGrain</c>:
/// <c>Squid.Grains</c> carries no Orleans package and no storage reference (ADR-35), so the
/// composition root constructs it and the silo, if ever needed, wraps it without this module
/// knowing anything about a silo.
/// </para>
/// <para>
/// Every failure is reported as a <see cref="BackupRunResult"/> instead of escaping: a target or
/// storage failure must never take the process down (SPEC-0012 section C reliability, AC-06), and
/// the message travels for the log only — the callers decide what a client may see (FR-053).
/// </para>
/// </summary>
public sealed class BackupCoordinatorGrain
{
    private readonly IBackupStateSource _state;
    private readonly IBackupRunner _runner;

    /// <summary>Binds the coordinator to the aggregate side and to the Worker side of the flow.</summary>
    public BackupCoordinatorGrain(IBackupStateSource state, IBackupRunner runner)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <summary>
    /// Runs one full cycle of contract section 3 for <paramref name="instanceId"/> and returns the
    /// registered envelope, or the failure reported without letting it propagate.
    /// </summary>
    public async Task<BackupRunResult> RunAsync(string instanceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        try
        {
            var seqTo = await _state.LastConsolidatedSeqAsync(instanceId, ct);
            // Discovery of what was already sent goes through the runner (contract section 1 —
            // ListAsync is the single source, never state kept here).
            var seqFrom = await _runner.NextSeqFromAsync(ct);
            var snapshot = await _state.SnapshotAsync(instanceId, seqFrom, seqTo, ct);
            return await _runner.RunAsync(snapshot, seqFrom, seqTo, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // a shutdown is not a backup failure
        }
        catch (Exception failure)
        {
            return new BackupRunResult(false, null, failure.Message);
        }
    }
}
