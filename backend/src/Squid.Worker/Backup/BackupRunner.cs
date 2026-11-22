using Squid.Api.Admin;
using Squid.Grains.Backup;

namespace Squid.Worker.Backup;

/// <summary>
/// <see cref="IBackupRunner"/> over the TASK-0054 core: it discovers what a destination already
/// holds through <c>IBackupTarget.ListAsync</c> (contract section 1 — the coordinator keeps no
/// parallel state), reads the current key epoch from the ledger, resolves the instance `RK` and
/// hands everything to <see cref="BackupPipeline"/>, which owns the fixed order serialize →
/// compress → encrypt → hash → send → re-read → register. The envelope comes back as the section
/// 2 shape the operator route answers with.
/// </summary>
public sealed class BackupRunner : IBackupRunner
{
    private readonly IBackupTarget _target;
    private readonly SqliteBackupEnvelopeStore _store;
    private readonly BackupPipeline _pipeline;
    private readonly IBackupKeySource _keys;

    /// <summary>Binds the runner to the destination, the registry, the pipeline and the instance key.</summary>
    public BackupRunner(
        IBackupTarget target,
        SqliteBackupEnvelopeStore store,
        BackupPipeline pipeline,
        IBackupKeySource keys)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    /// <inheritdoc />
    public async Task<long> NextSeqFromAsync(CancellationToken ct)
    {
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
        return new BackupRunResult(true, Of(envelope), null);
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
