using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Squid.Api.Crypto;

namespace Squid.Worker.Backup;

/// <summary>
/// The origin-side backup of SPEC-0012 section A and contract backup-recovery section 3, in the
/// order that is not a choice: serialize (in this Worker — the scheduler never does) → compress →
/// encrypt with ChaCha20-Poly1305 under a fresh random `DK` per backup wrapped by
/// <c>HKDF(RK, "fed/backup-kek")</c> → <c>contentHash</c> = SHA-256 of the ciphertext →
/// <c>PutAsync</c> → re-read from the target and compare the hash → <c>verifiedAt</c> → register
/// the envelope. Never send before the hash exists; never mark verifiedAt without the re-read: a
/// divergence leaves <see cref="BackupEnvelopeStatus.Corrupt"/> with <c>VerifiedAt</c> null, and a
/// Corrupt envelope is never used by restore.
/// </summary>
public sealed class BackupPipeline
{
    private const int DataKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IBackupTarget _target;
    private readonly SqliteBackupEnvelopeStore _store;
    private readonly TimeProvider _clock;

    /// <summary>One pipeline per destination and registry; <paramref name="clock"/> defaults to the system clock (GAP-17 seam).</summary>
    public BackupPipeline(IBackupTarget target, SqliteBackupEnvelopeStore store, TimeProvider? clock = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs one full backup of <paramref name="snapshot"/> covering
    /// <paramref name="seqFrom"/>..<paramref name="seqTo"/> at key epoch <paramref name="keyEpoch"/>
    /// and returns the envelope as it was registered: `Verified` only when the re-read confirmed
    /// the hash, `Corrupt` when it did not. A target failure propagates — nothing half-verified is
    /// registered, and the local backup is never corrupted by the destination.
    /// <paramref name="onPrepared"/> receives the envelope the moment it is complete (id, hash,
    /// size) and before the send: the fanout of SPEC-0012 section B records that prepared state
    /// when an off-site destination fails, so the registry keeps the state per destination
    /// without this pipeline ever registering a failure itself.
    /// </summary>
    public async Task<BackupEnvelope> RunAsync<TSnapshot>(
        TSnapshot snapshot,
        long seqFrom,
        long seqTo,
        int keyEpoch,
        RootKey rootKey,
        CancellationToken ct,
        Action<BackupEnvelope>? onPrepared = null)
        where TSnapshot : notnull
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rootKey);

        // serializa + comprime — both steps live here, in the Worker (contract section 3).
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, BackupEnvelope.JsonOptions);
        byte[] compressed;
        await using (var buffer = new MemoryStream())
        {
            await using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                await gzip.WriteAsync(payload, ct);
            }

            compressed = buffer.ToArray();
        }

        // cifra: a fresh DK and nonce per backup (AC-05 — same state, different ciphertext), the
        // DK wrapped for the header the destination stores and restore re-reads.
        var dataKey = RandomNumberGenerator.GetBytes(DataKeySize);
        try
        {
            var wrappedDek = KeyDerivation.WrapBackupKey(rootKey, dataKey);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);

            // The AEAD output — ciphertext followed by its tag — is what lands on the target, so
            // contentHash below covers exactly the bytes the re-read compares.
            var blob = new byte[compressed.Length + TagSize];
            using (var cipher = new ChaCha20Poly1305(dataKey))
            {
                cipher.Encrypt(
                    nonce, compressed, blob.AsSpan(0, compressed.Length), blob.AsSpan(compressed.Length));
            }

            // hash BEFORE sending — contract section 3: nunca enviar antes de calcular hash.
            var contentHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(blob));
            var createdAt = _clock.GetUtcNow();

            var envelope = new BackupEnvelope
            {
                Id = NewId(createdAt),
                KeyEpoch = keyEpoch,
                SeqFrom = seqFrom,
                SeqTo = seqTo,
                ContentHash = contentHash,
                SizeBytes = blob.LongLength,
                TargetId = _target.TargetId,
                CreatedAt = createdAt,
                WrappedDek = Convert.ToBase64String(wrappedDek),
                Nonce = Convert.ToBase64String(nonce),
            };

            // enviar → releitura e conferência do hash → verifiedAt (contract section 3).
            onPrepared?.Invoke(envelope);
            await using (var source = new MemoryStream(blob, writable: false))
            {
                await _target.PutAsync(envelope, source, ct);
            }

            await using (var reRead = await _target.GetAsync(envelope.Id, ct))
            {
                var rereadHash = "sha256:" + Convert.ToHexStringLower(await SHA256.HashDataAsync(reRead, ct));
                if (string.Equals(rereadHash, envelope.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    envelope.Status = BackupEnvelopeStatus.Verified;
                    envelope.VerifiedAt = _clock.GetUtcNow();
                }
                else
                {
                    envelope.Status = BackupEnvelopeStatus.Corrupt;
                    envelope.VerifiedAt = null;
                }
            }

            // S-47: the header on the destination was serialized BEFORE this re-read could know
            // the result, so the decided state (Verified + verifiedAt, or Corrupt) is written back
            // now — metadata only, the ciphertext is never sent twice. Targets that do not offer
            // the seam keep the pre-verification header; the registry below is the state of record.
            if (_target is IEnvelopeStateWriter writer)
            {
                await writer.WriteEnvelopeStateAsync(envelope, ct);
            }

            // registra — Corrupt included: the row keeps verified_at NULL, restore never uses it.
            await _store.RegisterAsync(envelope, ct);
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary><c>bk-&lt;utc&gt;-&lt;random&gt;</c> — the contract section 2 shape, unique per run.</summary>
    private static string NewId(DateTimeOffset at) =>
        $"bk-{at:yyyy-MM-dd'T'HHmm'Z'}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";
}
