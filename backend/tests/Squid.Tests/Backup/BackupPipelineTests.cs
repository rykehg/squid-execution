using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Squid.Api.Crypto;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-03..AC-06 of TASK-0054: the origin-side pipeline — a byte scan proves no plaintext or key
/// reaches the destination, the fixed order hash → send → re-read leaves a divergent re-read
/// `Corrupt` (and a re-read that never survives registers nothing), two backups of the same state
/// produce different ciphertexts that both decrypt with the same RK, and the registered row carries
/// the section 2.10 columns with a contentHash that is the SHA-256 of the bytes on disk.
/// </summary>
public sealed class BackupPipelineTests
{
    private const string Marker = "PLAINTEXT-SENTINEL-TASK0054-0123456789";
    private const int TagSize = 16;

    private sealed record Snapshot(string Marker, int[] Numbers);

    [Fact]
    public async Task Ac03_The_destination_gets_ciphertext_and_header_never_a_plaintext_or_key_byte()
    {
        var volume = NewVolume();
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var rk = RootKey.Create();
            var pipeline = new BackupPipeline(new LocalDiskTarget(volume), StoreOf(db));

            var envelope = await pipeline.RunAsync(
                new Snapshot(Marker, [1, 2, 3]), 0, 3, 1, rk, CancellationToken.None);

            // The backup really happened: ciphertext + envelope header on the volume.
            Assert.True(File.Exists(Path.Combine(volume, envelope.Id + ".bin")));
            Assert.True(File.Exists(Path.Combine(volume, envelope.Id + ".json")));

            // The scan AC-03 asks for: EVERY byte this run wrote — volume AND registry — must
            // contain neither the plaintext sentinel nor the RK.
            var sentinel = Encoding.UTF8.GetBytes(Marker);
            var rkBytes = rk.AsSpan().ToArray();
            foreach (var file in Directory.EnumerateFiles(volume, "*", SearchOption.AllDirectories).Append(db))
            {
                var bytes = await File.ReadAllBytesAsync(file);
                Assert.True(bytes.AsSpan().IndexOf(sentinel) < 0,
                    $"plaintext sentinel inside {Path.GetFileName(file)}");
                Assert.True(bytes.AsSpan().IndexOf(rkBytes) < 0,
                    $"RK bytes inside {Path.GetFileName(file)}");
            }

            // What the destination stores as header is the WRAPPED DK, never a key in clear.
            var header = await File.ReadAllTextAsync(Path.Combine(volume, envelope.Id + ".json"));
            Assert.Contains("wrappedDek", header, StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, header, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    [Fact]
    public async Task Ac04_The_order_is_hash_then_send_then_reread_and_a_divergence_is_corrupt()
    {
        var volume = NewVolume();
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var rk = RootKey.Create();
            var target = new FlippingTarget(new LocalDiskTarget(volume));
            var pipeline = new BackupPipeline(target, StoreOf(db));

            var envelope = await pipeline.RunAsync(
                new Snapshot(Marker, [7]), 10, 20, 2, rk, CancellationToken.None);

            // Order: the hash already existed AT the send, and the re-read came after it.
            Assert.Equal(new[] { "put", "get" }, target.Calls);
            Assert.StartsWith("sha256:", target.HashSeenAtPut, StringComparison.Ordinal);

            // The flipped byte diverges the re-read: Corrupt, no verifiedAt (contract section 3).
            Assert.Equal(BackupEnvelopeStatus.Corrupt, envelope.Status);
            Assert.Null(envelope.VerifiedAt);

            // The registry keeps it unverified — a Corrupt envelope is never used by restore.
            Assert.Null(await ProjectionDb.ScalarAsync<string>(db,
                "SELECT verified_at FROM backup_envelope WHERE id = @Id", new { Id = envelope.Id }));
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope"));
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    [Fact]
    public async Task Ac04_Without_a_surviving_reread_nothing_is_verified_or_registered()
    {
        var volume = NewVolume();
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var rk = RootKey.Create();
            var pipeline = new BackupPipeline(new FailingGetTarget(new LocalDiskTarget(volume)), StoreOf(db));

            // verifiedAt never exists without a re-read that survived: the run fails loudly and
            // the registry stays empty — no half-verified envelope for restore to find.
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RunAsync(
                new Snapshot(Marker, [9]), 0, 1, 1, rk, CancellationToken.None));
            Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope"));
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    [Fact]
    public async Task Ac04_ac06_Verified_run_sets_verifiedAt_and_registers_the_section_2_10_row()
    {
        var volume = NewVolume();
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var rk = RootKey.Create();
            var pipeline = new BackupPipeline(new LocalDiskTarget(volume), StoreOf(db));

            var envelope = await pipeline.RunAsync(
                new Snapshot(Marker, [1, 2]), 5, 9, 3, rk, CancellationToken.None);

            Assert.Equal(BackupEnvelopeStatus.Verified, envelope.Status);
            Assert.NotNull(envelope.VerifiedAt);

            // AC-06: the persisted envelope carries exactly the columns of data-model section 2.10.
            // Aliases: Dapper matches constructor names to columns without ignoring underscores.
            var row = Assert.Single(await ProjectionDb.QueryAsync<EnvelopeRow>(db,
                "SELECT id AS Id, key_epoch AS KeyEpoch, seq_from AS SeqFrom, seq_to AS SeqTo, "
                + "content_hash AS ContentHash, size_bytes AS SizeBytes, target_id AS TargetId, "
                + "created_at AS CreatedAt, verified_at AS VerifiedAt FROM backup_envelope"));
            Assert.Equal(envelope.Id, row.Id);
            Assert.Equal(3, row.KeyEpoch);
            Assert.Equal(5, row.SeqFrom);
            Assert.Equal(9, row.SeqTo);
            Assert.Equal(envelope.ContentHash, row.ContentHash);
            Assert.Equal(envelope.SizeBytes, row.SizeBytes);
            Assert.Equal("local-disk", row.TargetId);
            Assert.Equal(envelope.CreatedAt.ToString("O", CultureInfo.InvariantCulture), row.CreatedAt);
            Assert.Equal(
                envelope.VerifiedAt!.Value.ToString("O", CultureInfo.InvariantCulture), row.VerifiedAt);

            // AC-04: contentHash IS the SHA-256 of the ciphertext bytes that landed on disk.
            var stored = await File.ReadAllBytesAsync(Path.Combine(volume, envelope.Id + ".bin"));
            Assert.Equal(envelope.SizeBytes, stored.LongLength);
            Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(stored)), envelope.ContentHash);
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    [Fact]
    public async Task Ac05_Each_backup_wraps_its_own_dk_and_both_ciphertexts_decrypt_with_the_same_rk()
    {
        var volume = NewVolume();
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var rk = RootKey.Create();
            var pipeline = new BackupPipeline(new LocalDiskTarget(volume), StoreOf(db));
            var snapshot = new Snapshot(Marker, [42]);

            var first = await pipeline.RunAsync(snapshot, 0, 1, 1, rk, CancellationToken.None);
            var second = await pipeline.RunAsync(snapshot, 0, 1, 1, rk, CancellationToken.None);

            var one = await File.ReadAllBytesAsync(Path.Combine(volume, first.Id + ".bin"));
            var two = await File.ReadAllBytesAsync(Path.Combine(volume, second.Id + ".bin"));
            Assert.False(one.AsSpan().SequenceEqual(two),
                "the same state produced the same ciphertext — IV/DK are not per-backup");

            // Both open with the SAME RK: unwrap the per-backup DK from the stored header,
            // decrypt, gunzip — the payload comes back identical to what was serialized.
            var expected = JsonSerializer.Serialize(snapshot, BackupEnvelope.JsonOptions);
            Assert.Equal(expected, await OpenAsync(volume, first, rk));
            Assert.Equal(expected, await OpenAsync(volume, second, rk));
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    /// <summary>Reads one stored backup the way restore will: header → unwrap DK → decrypt → gunzip.</summary>
    private static async Task<string> OpenAsync(string volume, BackupEnvelope envelope, RootKey rk)
    {
        var header = JsonSerializer.Deserialize<BackupEnvelope>(
            await File.ReadAllTextAsync(Path.Combine(volume, envelope.Id + ".json")),
            BackupEnvelope.JsonOptions)!;

        var dataKey = KeyDerivation.UnwrapBackupKey(rk, Convert.FromBase64String(header.WrappedDek));
        try
        {
            var blob = await File.ReadAllBytesAsync(Path.Combine(volume, envelope.Id + ".bin"));
            var plaintext = new byte[blob.Length - TagSize];
            using var cipher = new ChaCha20Poly1305(dataKey);
            cipher.Decrypt(
                Convert.FromBase64String(header.Nonce),
                blob.AsSpan(0, plaintext.Length),
                blob.AsSpan(plaintext.Length),
                plaintext);

            await using var gzip = new GZipStream(new MemoryStream(plaintext), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            return await reader.ReadToEndAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>Records the call order and flips one byte of the re-read — AC-04's divergent hash.</summary>
    private sealed class FlippingTarget : IBackupTarget
    {
        private readonly IBackupTarget _inner;

        public FlippingTarget(IBackupTarget inner) => _inner = inner;

        public List<string> Calls { get; } = [];

        public string HashSeenAtPut { get; private set; } = string.Empty;

        public string TargetId => _inner.TargetId;

        public Task PutAsync(BackupEnvelope envelope, System.IO.Stream ciphertext, CancellationToken ct)
        {
            Calls.Add("put");
            HashSeenAtPut = envelope.ContentHash;
            return _inner.PutAsync(envelope, ciphertext, ct);
        }

        public async Task<System.IO.Stream> GetAsync(string envelopeId, CancellationToken ct)
        {
            Calls.Add("get");
            await using var stored = await _inner.GetAsync(envelopeId, ct);
            using var buffer = new MemoryStream();
            await stored.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            bytes[0] ^= 0xFF; // tamper lands between the send and the re-read
            return new MemoryStream(bytes, writable: false);
        }

        public Task<IReadOnlyList<BackupEnvelope>> ListAsync(long fromSeq, CancellationToken ct) =>
            _inner.ListAsync(fromSeq, ct);

        public Task<bool> ExistsAsync(string envelopeId, CancellationToken ct) =>
            _inner.ExistsAsync(envelopeId, ct);

        public Task DeleteAsync(string envelopeId, CancellationToken ct) =>
            _inner.DeleteAsync(envelopeId, ct);
    }

    /// <summary>The destination is down exactly at re-read time: nothing may be verified or registered.</summary>
    private sealed class FailingGetTarget : IBackupTarget
    {
        private readonly IBackupTarget _inner;

        public FailingGetTarget(IBackupTarget inner) => _inner = inner;

        public string TargetId => _inner.TargetId;

        public Task PutAsync(BackupEnvelope envelope, System.IO.Stream ciphertext, CancellationToken ct) =>
            _inner.PutAsync(envelope, ciphertext, ct);

        public Task<System.IO.Stream> GetAsync(string envelopeId, CancellationToken ct) =>
            throw new InvalidOperationException("destination unavailable at re-read");

        public Task<IReadOnlyList<BackupEnvelope>> ListAsync(long fromSeq, CancellationToken ct) =>
            _inner.ListAsync(fromSeq, ct);

        public Task<bool> ExistsAsync(string envelopeId, CancellationToken ct) =>
            _inner.ExistsAsync(envelopeId, ct);

        public Task DeleteAsync(string envelopeId, CancellationToken ct) =>
            _inner.DeleteAsync(envelopeId, ct);
    }

    private static string NewVolume() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"squid-backup-{Guid.NewGuid():N}")).FullName;

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"squid-backup-{Guid.NewGuid():N}.db");

    private static async Task MigrateAsync(string db) =>
        await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(db), SchemaMigrations.Load());

    private static SqliteBackupEnvelopeStore StoreOf(string db) => new(ProjectionDb.ConnectionOf(db));

    private static void Cleanup(string volume, string db)
    {
        try
        {
            Directory.Delete(volume, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a product failure; the OS cleans it up.
        }

        ProjectionDb.TryDelete(db);
    }

    /// <summary>One <c>backup_envelope</c> row, bound the way Dapper binds a constructor.</summary>
    private sealed record EnvelopeRow(
        string Id,
        long KeyEpoch,
        long SeqFrom,
        long SeqTo,
        string ContentHash,
        long SizeBytes,
        string TargetId,
        string CreatedAt,
        string? VerifiedAt);
}
