using System.IO.Compression;
using System.Security.Cryptography;
using Squid.Api.Admin;
using Squid.Api.Crypto;
using Squid.Api.Posts;

namespace Squid.Worker.Backup;

/// <summary>What one restore ended as — the <c>BackupRunResult</c> shape of the other half of the flow.</summary>
/// <param name="Succeeded">Every envelope of the chain verified and the destination matched it.</param>
/// <param name="Report">The facts of a successful run; null when it failed.</param>
/// <param name="Error">A message for the operator and the log — never a path, table or stack (FR-053).</param>
public sealed record RestoreResult(bool Succeeded, RestoreReport? Report, string? Error);

/// <summary>What a successful restore reports: contract backup-recovery section 4, steps 3..7.</summary>
/// <param name="Envelopes">Envelopes of the chain that were applied.</param>
/// <param name="Events">Activity events reapplied.</param>
/// <param name="Aggregates">Aggregates compared count-and-hash (step 7).</param>
/// <param name="SeqTo">Last sequence restored — the state the destination now holds.</param>
public sealed record RestoreReport(int Envelopes, long Events, long Aggregates, long SeqTo);

/// <summary>An explicit restore refusal: one sentence, no internal detail (FR-053).</summary>
public sealed class RestoreFailure(string message) : Exception(message);

/// <summary>
/// The restore flow of contract backup-recovery section 4 and SPEC-0012 section D, in the order
/// the contract fixes: the kit's <c>RK</c> (step 1) → the key epoch available (step 2) → the
/// chain downloaded in <c>seqTo</c> order (step 3) → hash validated, decrypted and format-migrated
/// for every payload (steps 4-5) → the state reapplied to the destination (step 6) → counts and
/// hashes per aggregate compared against the read-back (step 7). Two hard rules it never bends:
/// a <c>Corrupt</c> envelope is never accepted (section 3), and a chain with a hole is refused
/// loudly instead of restored short (principle VI — silent divergence is the defect).
/// </summary>
public sealed class RestorePipeline
{
    private const int TagSize = 16;

    private readonly IBackupTarget _target;
    private readonly SqliteBackupEnvelopeStore _store;
    private readonly IBackupKeySource _keys;
    private readonly IRestoreDestination _destination;
    private readonly SqliteRestoreDrillLedger _drills;

    /// <summary>Binds the restore to the destination volume, the registry, the kit and the database it restores into.</summary>
    public RestorePipeline(
        IBackupTarget target,
        SqliteBackupEnvelopeStore store,
        IBackupKeySource keys,
        IRestoreDestination destination,
        SqliteRestoreDrillLedger drills)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
        _drills = drills ?? throw new ArgumentNullException(nameof(drills));
    }

    /// <summary>
    /// Runs the section 4 flow over what the destination holds. Every refusal comes back as a
    /// <see cref="RestoreResult"/> instead of escaping — a failed restore never takes the process
    /// with it, and nothing is applied before every payload has proven itself (steps 4-5 precede
    /// step 6), so a refusal leaves the destination exactly as it was.
    /// </summary>
    public async Task<RestoreResult> RunAsync(CancellationToken ct = default)
    {
        try
        {
            // Step 1: only the kit's RK opens anything — no key, no restore (section 4 rule).
            var rootKey = _keys.Resolve()
                ?? throw new RestoreFailure("a chave de recuperação (Squid:RecoveryKey) não está disponível");

            // The destination file with its schema exists BEFORE anything reads it: a lost database
            // is recreated by the same idempotent Apply every host boot runs, an intact one is not
            // touched beyond that no-op — which is what keeps a refusal byte-identical (AC-04).
            await _destination.PrepareAsync(ct);

            // Step 2: the epochs this kit can open (1..current; rotation extends the keyring in
            // TASK-0058) — an envelope beyond them has no key here and says so before a download.
            var currentEpoch = await _store.CurrentEpochAsync(ct);
            var chain = await SelectChainAsync(ct);
            foreach (var envelope in chain)
            {
                if (envelope.KeyEpoch > currentEpoch)
                {
                    throw new RestoreFailure(
                        $"época de chave {envelope.KeyEpoch} sem chave no kit (época disponível {currentEpoch})");
                }
            }

            // Steps 3-5: download, validate the hash of each payload, open it and migrate its format.
            var events = new List<ActivityEvent>();
            string? instanceId = null;
            var seqTo = 0L;
            foreach (var envelope in chain)
            {
                var state = await OpenAsync(envelope, rootKey, ct);
                instanceId ??= state.InstanceId;
                events.AddRange(state.Events);
                seqTo = Math.Max(seqTo, state.SeqTo);
            }

            events.Sort((left, right) => left.Seq.CompareTo(right.Seq));

            // Step 6: reapplied into the destination (log, projections, quota) — then read back.
            var applied = await _destination.ReapplyAsync(
                new BackupActivitySnapshot(instanceId ?? string.Empty, 0, seqTo, events), ct);

            // Step 7: counts and hashes per aggregate must match exactly (SC-009).
            var expected = AggregateDigests.Of(events);
            var actual = AggregateDigests.Of(applied);
            if (!AggregateDigests.Matches(expected, actual))
            {
                throw new RestoreFailure(
                    "o estado restaurado diverge do backup (contagem/hash por agregado)");
            }

            // The public proof of section 6, recorded only NOW: a refused restore claims nothing.
            await _drills.RecordAsync("restore", "passed", ct);
            return new RestoreResult(true, new RestoreReport(chain.Count, events.Count, expected.Count, seqTo), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // a shutdown is not a restore failure
        }
        catch (RestoreFailure failure)
        {
            return new RestoreResult(false, null, failure.Message);
        }
        catch (CryptographicException)
        {
            return new RestoreResult(false, null,
                "o backup não pôde ser aberto com a chave do kit");
        }
        catch (Exception failure)
        {
            // Reported like the coordinator reports a backup failure (section C AC-06): the message
            // travels for the operator's log, never as a client-facing detail.
            return new RestoreResult(false, null, failure.Message);
        }
    }

    /// <summary>
    /// The chain of section 4 step 3, with section 3's hard rule folded in: envelopes in
    /// <c>seqFrom/seqTo</c> order starting at 0 with no hole between them, and never a
    /// <c>Corrupt</c> one — which the destination may hold beside the intact chain, so a corrupt
    /// envelope anywhere the chain needs is a refusal, not a gap walked over in silence.
    /// </summary>
    private async Task<IReadOnlyList<BackupEnvelope>> SelectChainAsync(CancellationToken ct)
    {
        var all = await _target.ListAsync(0, ct);
        if (all.Count == 0)
        {
            throw new RestoreFailure("nenhum backup encontrado no destino");
        }

        var accepted = all
            .Where(envelope => envelope.Status != BackupEnvelopeStatus.Corrupt)
            .OrderBy(envelope => envelope.SeqFrom)
            .ThenBy(envelope => envelope.SeqTo)
            .ThenBy(envelope => envelope.Id, StringComparer.Ordinal)
            .ToList();
        if (accepted.Count == 0)
        {
            throw new RestoreFailure("o destino só tem envelopes corrompidos — nenhum aceito no restore");
        }

        if (accepted[0].SeqFrom != 0)
        {
            throw new RestoreFailure("cadeia de backup incompleta: não começa em seqFrom 0");
        }

        for (var index = 1; index < accepted.Count; index++)
        {
            if (accepted[index].SeqFrom != accepted[index - 1].SeqTo + 1)
            {
                throw new RestoreFailure("cadeia de backup incompleta: falta um intervalo entre envelopes");
            }
        }

        if (accepted[^1].SeqTo != all[^1].SeqTo)
        {
            throw new RestoreFailure(
                "o destino tem envelope corrompido fora da cadeia íntegra — restore recusado");
        }

        return accepted;
    }

    /// <summary>Steps 4-5 of one envelope: hash first (no key needed), then open and migrate.</summary>
    private async Task<BackupActivitySnapshot> OpenAsync(
        BackupEnvelope envelope, RootKey rootKey, CancellationToken ct)
    {
        byte[] blob;
        await using (var stream = await _target.GetAsync(envelope.Id, ct))
        await using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, ct);
            blob = buffer.ToArray();
        }

        // Step 4: the ciphertext that arrives must be the one the header committed to — a
        // divergent hash is the corruption section 3 names, refused here before any key turns.
        var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(blob));
        if (!string.Equals(hash, envelope.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new RestoreFailure(
                $"hash do envelope {envelope.Id} diverge — backup corrompido, restore recusado");
        }

        byte[] dataKey;
        try
        {
            dataKey = KeyDerivation.UnwrapBackupKey(
                rootKey, Convert.FromBase64String(envelope.WrappedDek));
        }
        catch (FormatException)
        {
            throw new RestoreFailure($"o envelope {envelope.Id} tem cabeçalho ilegível");
        }

        try
        {
            var plaintext = new byte[blob.Length - TagSize];
            using (var cipher = new ChaCha20Poly1305(dataKey))
            {
                cipher.Decrypt(
                    Convert.FromBase64String(envelope.Nonce),
                    blob.AsSpan(0, plaintext.Length),
                    blob.AsSpan(plaintext.Length),
                    plaintext);
            }

            byte[] payload;
            await using (var gzip = new GZipStream(new MemoryStream(plaintext), CompressionMode.Decompress))
            await using (var buffer = new MemoryStream())
            {
                await gzip.CopyToAsync(buffer, ct);
                payload = buffer.ToArray();
            }

            return BackupFormatMigrations.Migrate(envelope.Format, payload, envelope);
        }
        catch (FormatException)
        {
            throw new RestoreFailure($"o envelope {envelope.Id} tem cabeçalho ilegível");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }
}
