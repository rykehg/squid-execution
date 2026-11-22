using Microsoft.Extensions.Logging;
using Squid.Api.Admin;
using Squid.Api.Crypto;
using Squid.Grains.Backup;

namespace Squid.Worker.Backup;

/// <summary>
/// The <see cref="IBackupRunner"/> over the TASK-0054 core: it discovers what a destination already
/// holds through <c>IBackupTarget.ListAsync</c> (contract section 1 — the coordinator keeps no
/// parallel state), reads the current key epoch from the ledger, resolves the instance `RK` and
/// hands everything to <see cref="BackupPipeline"/>, which owns the fixed order serialize →
/// compress → encrypt → hash → send → re-read → register. TASK-0057 adds the off-site destination
/// of SPEC-0012 section B as a second, optional pipeline over the SAME window: the local target
/// runs first with the semantics above, and an off-site failure (500, timeout) DEGRADES instead of
/// failing the run — the local backup stays intact and the registry records the state per
/// destination (the prepared envelope with a NULL <c>verified_at</c>, which section 6 counts and
/// alerts on, FR-050). Discovery stays on the primary target: an unreachable off-site destination
/// must never stop the local one from writing.
/// </summary>
public sealed class BackupRunner : IBackupRunner
{
    private readonly IBackupTarget _target;
    private readonly SqliteBackupEnvelopeStore _store;
    private readonly BackupPipeline _pipeline;
    private readonly IBackupKeySource _keys;
    private readonly IBackupTarget? _offsite;
    private readonly BackupPipeline? _offsitePipeline;
    private readonly ILogger<BackupRunner>? _log;

    /// <summary>
    /// Binds the runner to the primary destination, the registry, its pipeline and the instance
    /// key; <paramref name="offsite"/> (when the composition root wires <c>Backup:S3:*</c>) gets
    /// its own pipeline over the same registry, and <paramref name="log"/> reports its failures —
    /// both optional, so a local-only host is wired exactly as before.
    /// </summary>
    public BackupRunner(
        IBackupTarget target,
        SqliteBackupEnvelopeStore store,
        BackupPipeline pipeline,
        IBackupKeySource keys,
        IBackupTarget? offsite = null,
        ILogger<BackupRunner>? log = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _offsite = offsite;
        _offsitePipeline = offsite is null ? null : new BackupPipeline(offsite, store);
        _log = log;
    }

    /// <inheritdoc />
    public async Task<long> NextSeqFromAsync(CancellationToken ct)
    {
        // The primary destination drives the window (see the class summary): what it already
        // holds is what the next run extends, and the off-site failure cannot reach this read.
        var sent = await _target.ListAsync(0, ct);
        if (sent.Count == 0)
        {
            return 0; // nothing sent yet: the first run is the full snapshot (contract section 2)
        }

        return sent.Max(envelope => envelope.SeqTo) + 1;
    }

    /// <inheritdoc />
    public async Task<BackupRunResult> RunAsync(object snapshot, long seqFrom, long seqTo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rootKey = _keys.Resolve();
        if (rootKey is null)
        {
            // No key, no backup: encrypting under a random RK would produce bytes no kit can open
            // (contract section 4 restores nothing from a key the instance never had).
            return new BackupRunResult(false, null,
                "backup key unavailable: Squid:RecoveryKey is missing or malformed");
        }

        var keyEpoch = await _store.CurrentEpochAsync(ct);
        var envelope = await _pipeline.RunAsync(snapshot, seqFrom, seqTo, keyEpoch, rootKey, ct);
        await RunOffsiteAsync(snapshot, seqFrom, seqTo, keyEpoch, rootKey, ct);
        return new BackupRunResult(true, Of(envelope), null);
    }

    /// <summary>
    /// The off-site half of SPEC-0012 section B: the local destination already succeeded, so a
    /// 500 or a timeout here degrades — nothing is retried, nothing local is touched, and the
    /// prepared envelope is registered with a NULL <c>verified_at</c> so the registry answers
    /// "what was verified at each destination" (contract section 1) and section 6 alerts on it.
    /// A shutdown still propagates: it is not a backup failure.
    /// </summary>
    private async Task RunOffsiteAsync(
        object snapshot, long seqFrom, long seqTo, int keyEpoch, RootKey rootKey, CancellationToken ct)
    {
        if (_offsitePipeline is null || _offsite is null)
        {
            return;
        }

        BackupEnvelope? prepared = null;
        try
        {
            await _offsitePipeline.RunAsync(
                snapshot, seqFrom, seqTo, keyEpoch, rootKey, ct,
                onPrepared: envelope => prepared = envelope);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // a shutdown is not a backup failure
        }
        catch (Exception failure)
        {
            // The message travels for the log only (FR-053): it never reaches a route result.
            _log?.LogWarning(
                "off-site target {TargetId} failed for envelope {EnvelopeId}: {Reason}",
                _offsite.TargetId, prepared?.Id ?? "n/a", failure.Message);
            if (prepared is not null)
            {
                await _store.RegisterAsync(prepared, ct);
            }
        }
    }

    /// <summary>One registered envelope on the wire — contract section 2's fields.</summary>
    private static BackupEnvelopeView Of(BackupEnvelope envelope) => new(
        envelope.Id,
        envelope.KeyEpoch,
        envelope.SeqFrom,
        envelope.SeqTo,
        envelope.ContentHash,
        envelope.SizeBytes,
        envelope.TargetId,
        envelope.CreatedAt,
        envelope.VerifiedAt,
        envelope.Format,
        envelope.Status.ToString());
}
