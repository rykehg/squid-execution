using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Squid.Api.Admin;
using Squid.Api.Crypto;
using Squid.Api.Posts;
using Squid.Grains.Backup;
using Squid.Persistence;
using Squid.ReadModels.Projections;
using Squid.ReadModels.Quota;
using Squid.Service;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Recovery;

/// <summary>
/// AC-02, AC-03 and AC-05 of TASK-0058 over the real pieces wired the way the composition root
/// wires them (the drill harness's shape): a backup of the first epoch, then a rotation, then the
/// proofs the spec fixes — no destination byte moved (FR-047, R-20), the new chain's derived
/// material opens what the rotation re-wrapped and nothing else, the ledger stamped the second
/// epoch onto the new envelope, and the old-epoch chain still restores with the reissued keyring.
/// No host runs here — the pieces are wired directly over temp files, exactly like the drills.
/// </summary>
public sealed class KeyRotationChainTests
{
    [Fact] // AC-02 + AC-03 + AC-05
    public async Task Ac02_ac03_ac05_rotation_leaves_old_backups_identical_and_readable_while_the_new_chain_works()
    {
        await using var scenario = await Scenario.CreateAsync();
        var seeded = await scenario.SeedAsync(4);
        var first = await scenario.BackupAsync();
        Assert.Equal(1, first.KeyEpoch);

        // The double wrap registration writes (SPEC-0011 section B): a copy under the current key,
        // the row the rotation has to re-wrap for signing to keep working (AC-05).
        using var actorRsa = RSA.Create(2048);
        var oldKey = scenario.Keys.Resolve()!;
        await scenario.Copies.SaveAsync(
            "alice", Convert.ToBase64String(KeyDerivation.WrapRsaPrivateKey(oldKey, actorRsa)));

        // AC-03's baseline: every byte the destination holds before the rotation runs.
        var before = await scenario.VolumeHashesAsync();

        var kit = await scenario.Rotation.RotateAsync();

        // AC-03: no existing backup file was rewritten — same names, same bytes, same hashes.
        Assert.Equal(before, await scenario.VolumeHashesAsync());

        // AC-01's ledger half at the row level: the first epoch is retired with a fingerprint and
        // the epoch the response names is the open one.
        var epochs = await ProjectionDb.QueryAsync<EpochRow>(scenario.Db,
            "SELECT epoch AS Epoch, retired_at AS RetiredAt FROM key_epoch ORDER BY epoch");
        Assert.Equal(2, epochs.Count);
        Assert.Equal(1, epochs[0].Epoch);
        Assert.NotNull(epochs[0].RetiredAt);
        Assert.Equal(2, epochs[1].Epoch);
        Assert.Null(epochs[1].RetiredAt);
        Assert.Equal(kit.KeyEpoch, epochs[1].Epoch);

        // The new material, taken the way an operator's configuration takes it — from the kit's
        // keyring, the last epoch being the current key.
        var newKey = new ConfiguredBackupKeySource(ConfigWith(kit.Keyring[^1].Key)).Resolve()!;

        // AC-05: the derived chain is coherent with the NEW key — Ed25519 differs from the old
        // derivation, the rsa-wrap opens the re-wrapped copy only with the new key, and the ring
        // swapped in place so outbound signing goes on without a restart.
        Assert.NotEqual(
            KeyDerivation.DeriveEd25519PublicKey(oldKey),
            KeyDerivation.DeriveEd25519PublicKey(newKey));
        var row = await scenario.Copies.FindAsync("alice");
        Assert.NotNull(row);
        using var opened = KeyDerivation.UnwrapRsaPrivateKey(newKey, Convert.FromBase64String(row!));
        Assert.Equal(actorRsa.ExportRSAPrivateKey(), opened.ExportRSAPrivateKey());
        Assert.ThrowsAny<CryptographicException>(() => KeyDerivation.UnwrapRsaPrivateKey(
            oldKey, Convert.FromBase64String(row!)));
        Assert.True(scenario.Ring.OutputEnabled);

        // AC-02, first half: the DK of a backup made AFTER the rotation wraps under the new
        // backup-kek and only that key opens it — the old one is now just as wrong as before.
        await scenario.SeedAsync(3);
        var second = await scenario.BackupAsync();
        Assert.Equal(2, second.KeyEpoch); // stamped from the ledger the rotation advanced
        var wrappedDek = await scenario.WrappedDekAsync(second.Id);
        var dataKey = KeyDerivation.UnwrapBackupKey(newKey, Convert.FromBase64String(wrappedDek));
        Assert.Equal(32, dataKey.Length);
        Assert.ThrowsAny<CryptographicException>(() =>
            KeyDerivation.UnwrapBackupKey(oldKey, Convert.FromBase64String(wrappedDek)));

        // AC-02's named test: the old-epoch backup restores AFTER the rotation. The chain holds
        // the epoch-1 envelope — untouched on disk and open only because the reissued keyring
        // still carries that epoch (R-20: never rewritten, still readable).
        var restored = await scenario.RestoreAsync();
        Assert.True(restored.Succeeded, restored.Error);
        Assert.Equal(seeded.Length + 3, restored.Report!.Events);
        Assert.Equal(2, restored.Report.Envelopes);
    }

    /// <summary>The kit's keyring value as the configuration the operator pasts it into.</summary>
    private static IConfiguration ConfigWith(string currentKey) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Squid:RecoveryKey"] = currentKey,
        }).Build();

    /// <summary>One <c>key_epoch</c> row as Dapper binds it (SQLite INTEGER lands as long — the house rule).</summary>
    private sealed record EpochRow(long Epoch, string RetiredAt);

    /// <summary>
    /// One scenario per fact: the real pieces over a temp database, a temp volume and a temp blob
    /// root — the drill harness's shape, plus the rotation service and the ports it writes.
    /// </summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private int _seeded;

        private Scenario()
        {
            Db = Path.Combine(Path.GetTempPath(), $"squid-rotate-chain-{Guid.NewGuid():N}.db");
            Volume = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), $"squid-rotate-chain-{Guid.NewGuid():N}")).FullName;
            Blobs = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), $"squid-rotate-chain-blobs-{Guid.NewGuid():N}")).FullName;
            Connection = ProjectionDb.ConnectionOf(Db);

            Storage = new SqliteGrainStorage(Connection);
            Activities = new SqliteActivityStore(Storage, Connection);
            Envelopes = new SqliteBackupEnvelopeStore(Connection);
            Target = new LocalDiskTarget(Volume);
            Copies = new SqliteInstanceSigningKeyStore(Connection);
            Epochs = new SqliteKeyEpochStore(Connection);
            Kits = new SqliteInstanceRecoveryKitStore(Storage);
            Drills = new SqliteRestoreDrillLedger(Storage);
            Quota = new SqliteQuotaLedger(Connection);
            Log = new SqliteActivityLog(Connection);
            Projector = new ActivityProjector(Connection);

            CurrentKey = Convert.ToBase64String(RootKey.Create().AsSpan());
            Keys = new ConfiguredBackupKeySource(ConfigWith(CurrentKey));
            Ring = RuntimeSigningKeyRing.FromConfiguration(CurrentKey, Copies);
            Rotation = new KeyRotationService(Ring, Keys, Epochs, Copies, Kits);
            Runner = new BackupRunner(
                Target, Envelopes, new BackupPipeline(Target, Envelopes), Keys);
            Coordinator = new BackupCoordinatorGrain(new SqliteBackupStateSource(Activities), Runner);
        }

        public string Db { get; }
        public string Volume { get; }
        public string Blobs { get; }
        public string Connection { get; }
        public string CurrentKey { get; }
        public SqliteGrainStorage Storage { get; }
        public SqliteActivityStore Activities { get; }
        public SqliteBackupEnvelopeStore Envelopes { get; }
        public LocalDiskTarget Target { get; }
        public SqliteInstanceSigningKeyStore Copies { get; }
        public SqliteKeyEpochStore Epochs { get; }
        public SqliteInstanceRecoveryKitStore Kits { get; }
        public SqliteRestoreDrillLedger Drills { get; }
        public IQuotaLedger Quota { get; }
        public SqliteActivityLog Log { get; }
        public IActivityProjector Projector { get; }
        public ConfiguredBackupKeySource Keys { get; }
        public RuntimeSigningKeyRing Ring { get; }
        public KeyRotationService Rotation { get; }
        public BackupRunner Runner { get; }
        public BackupCoordinatorGrain Coordinator { get; }

        /// <summary>Applies the same migration family the host applies at init.</summary>
        public static async Task<Scenario> CreateAsync()
        {
            var scenario = new Scenario();
            await SchemaMigrator.ApplyAsync(
                ProjectionDb.ConnectionOf(scenario.Db), SchemaMigrations.Load());
            return scenario;
        }

        /// <summary>Appends <paramref name="count"/> realistic activity events (valid post summaries).</summary>
        public async Task<ActivityEvent[]> SeedAsync(int count)
        {
            var seeded = new List<ActivityEvent>();
            for (var index = 0; index < count; index++)
            {
                var number = ++_seeded;
                var container = number % 2 == 0
                    ? "http://squid.test/c/alpha"
                    : "http://squid.test/c/beta";
                var objectId = $"urn:squid:object:p{(number - 1) % 3 + 1}";
                var createdAt = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)
                    .AddSeconds(number);
                var payload = JsonSerializer.Serialize(new
                {
                    containerId = container,
                    id = objectId,
                    author = "urn:squid:actor:alice",
                    content = $"conteudo {number}",
                    seq = number,
                    depth = 0,
                    visibilityAtPublish = "public",
                    policyVersion = 1,
                    createdAt,
                }, BackupEnvelope.JsonOptions);

                seeded.Add(await Activities.AppendAsync(new ActivityEvent(
                    $"urn:squid:activity:t{number}", "Create", "urn:squid:actor:alice",
                    objectId, 0, payload, createdAt)));
            }

            return seeded.ToArray();
        }

        public async Task<BackupEnvelopeView> BackupAsync()
        {
            var result = await Coordinator.RunAsync("default");
            Assert.True(result.Succeeded, result.Error);
            return result.Envelope!;
        }

        public Task<RestoreResult> RestoreAsync() => new RestorePipeline(
            Target,
            Envelopes,
            Keys,
            new SqliteRestoreDestination(Connection, Activities, Log, Projector, Quota, Blobs),
            Drills).RunAsync();

        /// <summary>The wrapped-DK header of one envelope, read straight from the destination.</summary>
        public async Task<string> WrappedDekAsync(string envelopeId)
        {
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(Volume, envelopeId + ".json")));
            return document.RootElement.GetProperty("wrappedDek").GetString()!;
        }

        /// <summary>SHA-256 of every file the destination holds, by name — AC-03's baseline.</summary>
        public async Task<IReadOnlyDictionary<string, string>> VolumeHashesAsync()
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(Volume))
            {
                hashes[Path.GetFileName(file)] = Convert.ToHexStringLower(
                    SHA256.HashData(await File.ReadAllBytesAsync(file)));
            }

            return hashes;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            ProjectionDb.TryDelete(Db);
            try
            {
                Directory.Delete(Volume, recursive: true);
                Directory.Delete(Blobs, recursive: true);
            }
            catch (IOException)
            {
                // Leftover temp folders are not a product failure; the OS cleans them up.
            }

            await Task.CompletedTask;
        }
    }
}
