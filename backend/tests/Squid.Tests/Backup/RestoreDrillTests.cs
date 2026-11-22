using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Squid.Api.Crypto;
using Squid.Api.Posts;
using Squid.Grains.Backup;
using Squid.Persistence;
using Squid.ReadModels.Projections;
using Squid.ReadModels.Quota;
using Squid.Service;
using Squid.Tests.Posts;
using Squid.Tests.Projections;
using Squid.Worker.Backup;
using Squid.Worker.Quota;

namespace Squid.Tests.Backup;

/// <summary>
/// The five blocking drills of contract backup-recovery section 5 that belong to TASK-0056
/// (SPEC-0012 section D), each one an end-to-end run of the real pieces: the coordinator and the
/// pipeline of TASK-0054/0055 back up through <c>SqliteBackupStateSource</c>, the database the
/// backup covered is deleted, and <see cref="RestorePipeline"/> brings it back — 100% of the
/// aggregate hashes identical (SC-009), the interrupted run resumed from the intact <c>seqTo</c>
/// with no Corrupt envelope ever accepted, an older <c>format</c> migrated and an unknown one
/// refused out loud, a <c>keyEpoch</c> without a key refusing before anything is written, and the
/// delta-only chain pinned as it works today. AC-06 then checks the read side confers and the
/// status route publishes the drill the restore recorded.
/// </summary>
[Trait("Category", "RestoreDrill")]
[Collection("ServiceHost")] // one fact starts a host; hosts never overlap (localhost clustering)
public sealed class RestoreDrillTests
{
    [Fact] // AC-01
    public async Task Ac01_Roundtrip_restores_100_percent_of_the_aggregate_hashes()
    {
        await using var scenario = await Scenario.CreateAsync();
        var seeded = await scenario.SeedAsync(6);
        var before = AggregateDigests.Of(seeded);
        var envelope = await scenario.BackupAsync();
        Assert.Equal(0, envelope.SeqFrom); // the first run of a destination is the full window (§2)

        // "apagar o diretório": the database the backup covered is gone.
        scenario.DeleteDatabase();

        var restore = await scenario.RestoreAsync();
        Assert.True(restore.Succeeded, restore.Error);

        // SC-009: every aggregate of the destination carries the count and the hash it had.
        var after = await scenario.DigestsAsync();
        Assert.Equal(before.Count, after.Count);
        Assert.True(AggregateDigests.Matches(before, after),
            "SC-009 exige 100% dos hashes idênticos por agregado após o restore");
        Assert.Equal(seeded.Length, restore.Report!.Events);
        Assert.Equal(before.Count, restore.Report.Aggregates);
    }

    [Fact] // AC-02
    public async Task Ac02_Interrupted_backup_resumes_from_the_intact_seqTo_and_corrupt_is_never_accepted()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.SeedAsync(4);
        var first = await scenario.BackupAsync();
        Assert.Equal(0, first.SeqFrom);
        Assert.Equal(4, first.SeqTo);

        // A death between the ciphertext and its header leaves a .bin no discovery may see (§1),
        // and a run that dies at Put registers nothing — the two halves of "matar no meio".
        await File.WriteAllBytesAsync(Path.Combine(scenario.Volume, "bk-torn.bin"), [0x01, 0x02]);
        var died = await scenario.CoordinatorOver(new DyingTarget(scenario.Target)).RunAsync("default");
        Assert.False(died.Succeeded);
        Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(scenario.Db,
            "SELECT COUNT(*) FROM backup_envelope"));

        // Two more writes, then the rerun: seqFrom starts exactly one past the INTACT seqTo —
        // the torn file contributed nothing to the discovery.
        await scenario.SeedAsync(2);
        var resumed = await scenario.BackupAsync();
        Assert.Equal(first.SeqTo + 1, resumed.SeqFrom);
        Assert.Equal(5, resumed.SeqFrom);
        Assert.Equal(6, resumed.SeqTo);

        // Restore straight through the interruption: complete chain, complete state.
        scenario.DeleteDatabase();
        var restored = await scenario.RestoreAsync();
        Assert.True(restored.Succeeded, restored.Error);
        Assert.Equal(6, restored.Report!.Events);

        // A divergent re-read leaves a Corrupt envelope on the destination (§3): the restore
        // refuses everything instead of walking over the hole — nothing applied, nothing claimed.
        await scenario.SeedAsync(2);
        var corrupt = await scenario.CoordinatorOver(new FlippingTarget(scenario.Target)).RunAsync("default");
        Assert.Equal("Corrupt", corrupt.Envelope!.Status);
        var intact = await scenario.DigestsAsync();

        var refused = await scenario.RestoreAsync();
        Assert.False(refused.Succeeded);
        Assert.Contains("corrompido", refused.Error!, StringComparison.Ordinal);
        Assert.True(AggregateDigests.Matches(intact, await scenario.DigestsAsync()),
            "uma recusa não pode tocar no destino");

        // The registered purge of §7 takes the corrupt envelope out; the intact chain restores.
        await scenario.Target.DeleteAsync(corrupt.Envelope.Id, CancellationToken.None);
        var recovered = await scenario.RestoreAsync();
        Assert.True(recovered.Succeeded, recovered.Error);
        Assert.Equal(6, recovered.Report!.Events);
    }

    [Fact] // AC-03
    public async Task Ac03_An_older_format_is_migrated_and_an_unknown_one_fails_explicitly()
    {
        await using var scenario = await Scenario.CreateAsync();
        var seeded = await scenario.SeedAsync(3);
        var envelope = await scenario.BackupAsync();

        // (a) the migration itself: the pre-window payload carries no seqFrom/seqTo, so the only
        // place its window can come from is the envelope — proven with an interval the payload
        // does not carry (a direct deserialize would answer 0/0).
        var legacy = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { instanceId = "i-1", events = seeded }, BackupEnvelope.JsonOptions));
        var delta = new BackupEnvelope { Id = "bk-delta", SeqFrom = 5, SeqTo = 8 };
        var migrated = BackupFormatMigrations.Migrate(0, legacy, delta);
        Assert.Equal(5, migrated.SeqFrom);
        Assert.Equal(8, migrated.SeqTo);
        Assert.Equal("i-1", migrated.InstanceId);
        Assert.Equal(seeded.Length, migrated.Events.Count);

        // (b) the flow accepts an envelope whose header carries the older format and restores it.
        await scenario.RewriteHeaderAsync(envelope.Id, format: 0);
        scenario.DeleteDatabase();
        var restored = await scenario.RestoreAsync();
        Assert.True(restored.Succeeded, restored.Error);
        Assert.Equal(seeded.Length, (await scenario.DigestsAsync()).Values.Sum(digest => digest.Count));

        // (c) a format this version does not know is an explicit failure, never a silent read.
        await scenario.RewriteHeaderAsync(envelope.Id, format: 99);
        var refused = await scenario.RestoreAsync();
        Assert.False(refused.Succeeded);
        Assert.Contains("formato", refused.Error!, StringComparison.Ordinal);
        Assert.Contains("99", refused.Error!, StringComparison.Ordinal);
    }

    [Fact] // AC-04
    public async Task Ac04_A_keyEpoch_without_a_key_in_the_kit_fails_and_destroys_nothing()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.SeedAsync(3);
        var envelope = await scenario.BackupAsync();
        await scenario.RewriteHeaderAsync(envelope.Id, keyEpoch: 99);

        // The destination is intact and must stay byte-identical: same state, same file.
        var before = await scenario.DigestsAsync();
        var fileBefore = await File.ReadAllBytesAsync(scenario.Db);
        var hashBefore = SHA256.HashData(fileBefore);

        var refused = await scenario.RestoreAsync();
        Assert.False(refused.Succeeded);
        Assert.Contains("sem chave no kit", refused.Error!, StringComparison.Ordinal);
        Assert.Contains("99", refused.Error!, StringComparison.Ordinal);

        Assert.True(AggregateDigests.Matches(before, await scenario.DigestsAsync()),
            "a recusa não pode alterar o estado do destino");
        var fileAfter = await File.ReadAllBytesAsync(scenario.Db);
        Assert.True(hashBefore.SequenceEqual(SHA256.HashData(fileAfter)),
            "o arquivo de destino deve ficar byte-idêntico");
    }

    [Fact] // AC-05
    public async Task Ac05_The_second_backup_carries_only_the_delta_and_the_chain_still_restores()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.SeedAsync(4);
        var first = await scenario.BackupAsync();

        var more = await scenario.SeedAsync(3); // N escritas
        var second = await scenario.BackupAsync();

        // The delta semantics as they work TODAY (R-15's weekly full is not implemented): the
        // second run starts one past the previous seqTo and its payload holds only the new events.
        Assert.Equal(first.SeqTo + 1, second.SeqFrom);
        var payload = await scenario.OpenPayloadAsync(second.Id);
        Assert.Equal(second.SeqFrom, payload.SeqFrom);
        Assert.Equal(second.SeqTo, payload.SeqTo);
        Assert.Equal(more.Length, payload.Events.Count);
        Assert.All(payload.Events, activity => Assert.True(activity.Seq > first.SeqTo,
            "o segundo backup deve conter só o delta"));

        // And the delta-only chain restores end to end — restore walks it from seqFrom 0.
        scenario.DeleteDatabase();
        var restored = await scenario.RestoreAsync();
        Assert.True(restored.Succeeded, restored.Error);
        Assert.Equal(4 + more.Length, restored.Report!.Events);
        Assert.Equal(2, restored.Report.Envelopes);
    }

    [Fact] // AC-06
    public async Task Ac06_After_the_restore_projections_quota_and_readiness_all_confer()
    {
        await using var scenario = await Scenario.CreateAsync();

        // The blob volume is the quota ledger's truth and stays OUT of the envelope (GAP-55).
        await File.WriteAllBytesAsync(Path.Combine(scenario.Blobs, "a.bin"), new byte[1000]);
        await File.WriteAllBytesAsync(Path.Combine(scenario.Blobs, "b.bin"), new byte[2500]);

        await scenario.SeedAsync(5);
        await scenario.BackupAsync();
        await scenario.ProjectOnceAsync(); // the pass the pump runs
        await new QuotaReconciler(scenario.Quota, scenario.Blobs).ReconcileAsync();
        var projectionsBefore = await scenario.ProjectionsAsync();
        var quotaBefore = await ProjectionDb.ScalarAsync<long>(scenario.Db,
            "SELECT used_bytes FROM quota_ledger");
        Assert.Equal(3500, quotaBefore);

        scenario.DeleteDatabase();
        var restore = await scenario.RestoreAsync();
        Assert.True(restore.Succeeded, restore.Error);

        // Projections re-applied: counts and hashes per aggregate (container) identical.
        Assert.Equal(projectionsBefore, await scenario.ProjectionsAsync());

        // The ledger confers with the volume the restore never touched.
        Assert.Equal(quotaBefore, await ProjectionDb.ScalarAsync<long>(scenario.Db,
            "SELECT used_bytes FROM quota_ledger"));

        // "/ready responderia verde": exactly the predicate the route maps to (SPEC-0002).
        Assert.True(new StorageReadiness(ProjectionDb.ConnectionOf(scenario.Db)).IsAccessible());
    }

    [Fact] // the drill ledger of contract §6, wired into GET /api/admin/backup/status
    public async Task The_drill_a_restore_records_stops_the_status_route_answering_null()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.SeedAsync(2);
        await scenario.BackupAsync();
        scenario.DeleteDatabase();
        var restore = await scenario.RestoreAsync(); // records ("restore", "passed") on success only
        Assert.True(restore.Succeeded, restore.Error);

        var handle = PostRoutes.Handle("resto");
        var recoveryKey = Convert.ToBase64String(scenario.Keys.Key!.AsSpan());
        await using var host = await BackupHost.StartAsync(
        [
            "--Storage:Path=" + scenario.Db,
            "--Logging:LogLevel:Default=Warning",
            "--Instance:Operators=" + handle,
            "--Backup:Local:Path=" + scenario.Volume,
            "--Backup:IntervalMinutes=0", // the scheduler has no business in this fact
            "--Squid:RecoveryKey=" + recoveryKey,
        ]);

        var client = host.NewClient();
        Assert.Equal(HttpStatusCode.Created,
            (await client.RegisterAsync(handle, PostRoutes.Password)).StatusCode);

        var status = await PostRoutes.SendAsync(client, HttpMethod.Get, "/api/admin/backup/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using (var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
        {
            var restoreNode = document.RootElement.GetProperty("restore");
            Assert.NotEqual(JsonValueKind.Null, restoreNode.GetProperty("last_drill_at").ValueKind);
            Assert.False(string.IsNullOrEmpty(restoreNode.GetProperty("last_drill_at").GetString()));
            Assert.Equal("restore: passed", restoreNode.GetProperty("last_drill_result").GetString());
        }

        // The restored database answers readiness over HTTP too — AC-06's "/ready verde".
        var ready = await client.SendAsync(HttpMethod.Get, "/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    /// <summary>
    /// One scenario per fact: the real pieces wired the way <c>SquidServiceHost</c> wires them —
    /// the state source, the coordinator, the registry, the local volume, the drill ledger and the
    /// restore pipeline — over a temp database, a temp volume and a temp blob root.
    /// </summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private int _seeded;

        private Scenario()
        {
            Db = Path.Combine(Path.GetTempPath(), $"squid-restore-{Guid.NewGuid():N}.db");
            Volume = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), $"squid-restore-{Guid.NewGuid():N}")).FullName;
            Blobs = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), $"squid-restore-blobs-{Guid.NewGuid():N}")).FullName;
            Connection = ProjectionDb.ConnectionOf(Db);

            var storage = new SqliteGrainStorage(Connection);
            Activities = new SqliteActivityStore(storage, Connection);
            Envelopes = new SqliteBackupEnvelopeStore(Connection);
            Target = new LocalDiskTarget(Volume);
            Keys = new TestKeySource();
            Drills = new SqliteRestoreDrillLedger(storage);
            Quota = new SqliteQuotaLedger(Connection);
            Log = new SqliteActivityLog(Connection);
            Projector = new ActivityProjector(Connection);
            Coordinator = CoordinatorOver(Target);
        }

        public string Db { get; }
        public string Volume { get; }
        public string Blobs { get; }
        public string Connection { get; }
        public SqliteActivityStore Activities { get; }
        public SqliteBackupEnvelopeStore Envelopes { get; }
        public LocalDiskTarget Target { get; }
        public TestKeySource Keys { get; }
        public SqliteRestoreDrillLedger Drills { get; }
        public IQuotaLedger Quota { get; }
        public SqliteActivityLog Log { get; }
        public IActivityProjector Projector { get; }
        public BackupCoordinatorGrain Coordinator { get; }

        public static async Task<Scenario> CreateAsync()
        {
            var scenario = new Scenario();
            await SchemaMigrator.ApplyAsync(
                ProjectionDb.ConnectionOf(scenario.Db), SchemaMigrations.Load());
            return scenario;
        }

        /// <summary>The coordinator over any destination — a drill swaps the target to kill a run.</summary>
        public BackupCoordinatorGrain CoordinatorOver(IBackupTarget target) =>
            new(new SqliteBackupStateSource(Activities),
                new BackupRunner(target, Envelopes, new BackupPipeline(target, Envelopes), Keys));

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

        public Task<RestoreResult> RestoreAsync() => NewRestore().RunAsync();

        public RestorePipeline NewRestore() => new(
            Target, Envelopes, Keys,
            new SqliteRestoreDestination(Connection, Activities, Log, Projector, Quota, Blobs),
            Drills);

        /// <summary>"Apagar o diretório": the covered database is deleted before the restore.</summary>
        public void DeleteDatabase() => File.Delete(Db);

        public async Task<IReadOnlyDictionary<string, AggregateDigest>> DigestsAsync() =>
            AggregateDigests.Of(await Activities.ReadAsync(0));

        /// <summary>The projection pass exactly as the pump runs it (SPEC-0009 section B).</summary>
        public async Task ProjectOnceAsync()
        {
            var since = await Projector.LastProjectedSeqAsync();
            foreach (var entry in await Log.ReadAsync(since))
            {
                await Projector.ProjectAsync(entry);
            }
        }

        /// <summary>Counts and hashes per aggregate (container) of the read side, plus the home rows.</summary>
        public async Task<List<string>> ProjectionsAsync()
        {
            var rows = await ProjectionDb.QueryAsync<ProjectionRow>(Db,
                "SELECT container_id AS ContainerId, seq AS Seq, post_id AS PostId, content AS Content "
                + "FROM timeline_item ORDER BY container_id, seq");
            var home = await ProjectionDb.ScalarAsync<long>(Db, "SELECT COUNT(*) FROM home_timeline");

            var digest = rows
                .GroupBy(row => row.ContainerId, StringComparer.Ordinal)
                .Select(group =>
                {
                    var canonical = string.Join('\n',
                        group.Select(row => $"{row.Seq}|{row.PostId}|{row.Content}"));
                    var hash = Convert.ToHexStringLower(
                        SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
                    return $"{group.Key}|{group.Count()}|{hash}";
                })
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToList();
            digest.Add($"home_timeline|{home}|");
            return digest;
        }

        /// <summary>Reads one stored backup the way restore will: header → unwrap DK → decrypt → gunzip.</summary>
        public async Task<BackupActivitySnapshot> OpenPayloadAsync(string envelopeId)
        {
            var header = JsonSerializer.Deserialize<BackupEnvelope>(
                await File.ReadAllTextAsync(Path.Combine(Volume, envelopeId + ".json")),
                BackupEnvelope.JsonOptions)!;
            var dataKey = KeyDerivation.UnwrapBackupKey(
                Keys.Key!, Convert.FromBase64String(header.WrappedDek));
            try
            {
                var blob = await File.ReadAllBytesAsync(Path.Combine(Volume, envelopeId + ".bin"));
                var plaintext = new byte[blob.Length - 16];
                using (var cipher = new ChaCha20Poly1305(dataKey))
                {
                    cipher.Decrypt(
                        Convert.FromBase64String(header.Nonce),
                        blob.AsSpan(0, plaintext.Length),
                        blob.AsSpan(plaintext.Length),
                        plaintext);
                }

                await using var gzip = new GZipStream(new MemoryStream(plaintext), CompressionMode.Decompress);
                await using var buffer = new MemoryStream();
                await gzip.CopyToAsync(buffer);
                return BackupFormatMigrations.Migrate(header.Format, buffer.ToArray(), header);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }

        /// <summary>Rewrites fields of a stored header — how an older writer or a wrong epoch is forged.</summary>
        public async Task RewriteHeaderAsync(string envelopeId, int? format = null, int? keyEpoch = null)
        {
            var path = Path.Combine(Volume, envelopeId + ".json");
            var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            if (format is not null)
            {
                node["format"] = format.Value;
            }

            if (keyEpoch is not null)
            {
                node["keyEpoch"] = keyEpoch.Value;
            }

            await File.WriteAllTextAsync(path, node.ToJsonString());
        }

        public ValueTask DisposeAsync()
        {
            ProjectionDb.TryDelete(Db);
            try
            {
                Directory.Delete(Volume, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is not a product failure; the OS cleans it up.
            }

            try
            {
                Directory.Delete(Blobs, recursive: true);
            }
            catch (IOException)
            {
                // Same: a leftover temp folder is not a product failure.
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>One <c>timeline_item</c> row, bound the way Dapper binds a constructor.</summary>
        private sealed record ProjectionRow(string ContainerId, long Seq, string PostId, string Content);
    }

    /// <summary>A destination whose Put dies mid-run: nothing is registered, nothing half-sent counts.</summary>
    private sealed class DyingTarget(IBackupTarget inner) : IBackupTarget
    {
        public string TargetId => inner.TargetId;

        public Task PutAsync(BackupEnvelope envelope, System.IO.Stream ciphertext, CancellationToken ct) =>
            throw new IOException("o processo morreu no meio do backup");

        public Task<System.IO.Stream> GetAsync(string envelopeId, CancellationToken ct) =>
            inner.GetAsync(envelopeId, ct);

        public Task<IReadOnlyList<BackupEnvelope>> ListAsync(long fromSeq, CancellationToken ct) =>
            inner.ListAsync(fromSeq, ct);

        public Task<bool> ExistsAsync(string envelopeId, CancellationToken ct) =>
            inner.ExistsAsync(envelopeId, ct);

        public Task DeleteAsync(string envelopeId, CancellationToken ct) =>
            inner.DeleteAsync(envelopeId, ct);
    }
}
