using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Squid.Api.Crypto;
using Squid.Grains.Backup;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-04 and AC-05 of TASK-0057 at the composition seam: an off-site destination answering 500
/// (and a hung one, for the timeout branch) degrades to the local target exactly as
/// SPEC-0012 section B words it — the local backup keeps being written, the run succeeds, and
/// the registry records the state per destination (verified locally, a NULL verified_at for the
/// off-site row, which section 6 counts and FR-050 alerts on) while discovery keeps running on
/// the primary; and the S3 credential comes from <c>Backup:S3:*</c> only — it reaches no grain
/// state file, no log line and no route response (contract section 7).
/// </summary>
[Collection("ServiceHost")] // the AC-05 fact starts a host; hosts never overlap
public sealed class BackupOffsiteTests
{
    private const string AccessKey = "AKIA0TASK0057S3B";
    private const string SecretKey = "task-0057-offsite-secret-key";

    [Fact] // AC-04 (500 half)
    public async Task Ac04_A_500_off_site_degrades_and_the_registry_records_the_state_per_destination()
    {
        var volume = NewVolume();
        var db = NewDb();
        await using var s3 = await FakeS3.StartAsync("squid-backup", AccessKey, SecretKey);
        s3.FailNext(StatusCodes.Status500InternalServerError, times: int.MaxValue);
        var log = new CapturingLogger<BackupRunner>();
        try
        {
            await MigrateAsync(db);
            var local = new LocalDiskTarget(volume);
            var store = new SqliteBackupEnvelopeStore(ProjectionDb.ConnectionOf(db));
            using var offsite = new S3CompatibleTarget(s3.Options);
            var state = new FakeStateSource(new StateSnapshot("offsite", 3)) { SeqTo = 4 };
            var coordinator = new BackupCoordinatorGrain(
                state,
                new BackupRunner(
                    local, store, new BackupPipeline(local, store), new TestKeySource(), offsite, log));

            // The local destination answers: the run succeeds degraded, with ITS envelope.
            var first = await coordinator.RunAsync("i-1");
            Assert.True(first.Succeeded, first.Error);
            Assert.Equal("Verified", first.Envelope!.Status);
            Assert.Equal("local-disk", first.Envelope.TargetId);
            Assert.True(await local.ExistsAsync(first.Envelope.Id, CancellationToken.None));

            // Estado por destino: the local row verified, the off-site row registered without a
            // confirmed re-read — exactly what section 6 counts as a verify failure and alerts on.
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope WHERE target_id = 'local-disk' AND verified_at IS NOT NULL"));
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope WHERE target_id = 's3-offsite' AND verified_at IS NULL"));
            Assert.Contains(log.Lines,
                line => line.Contains("s3-offsite", StringComparison.Ordinal));

            // Discovery never reaches the broken destination: the second window keeps running on
            // the local one and the local chain stays contiguous (não corrompe o backup local).
            state.SeqTo = 9;
            var second = await coordinator.RunAsync("i-1");
            Assert.True(second.Succeeded, second.Error);
            Assert.Equal(5, second.Envelope!.SeqFrom);
            var chain = await local.ListAsync(0, CancellationToken.None);
            Assert.Equal(2, chain.Count);
            Assert.Equal(new long[] { 0, 5 }, chain.Select(envelope => envelope.SeqFrom));

            // The off-site destination truly never received anything (heal first: the scripted
            // 500 would otherwise answer the discovery too).
            s3.Heal();
            Assert.Empty(await offsite.ListAsync(0, CancellationToken.None));
            Assert.Equal(2, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope WHERE target_id = 's3-offsite'"));
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    [Fact] // AC-04 (timeout half)
    public async Task Ac04_A_hung_off_site_times_out_into_the_same_degradation()
    {
        var volume = NewVolume();
        var db = NewDb();
        await using var s3 = await FakeS3.StartAsync("squid-backup", AccessKey, SecretKey);
        s3.HangNext(TimeSpan.FromSeconds(2)); // the client gives up long before the fake answers
        try
        {
            await MigrateAsync(db);
            var local = new LocalDiskTarget(volume);
            var store = new SqliteBackupEnvelopeStore(ProjectionDb.ConnectionOf(db));
            using var offsite = new S3CompatibleTarget(
                s3.Options, timeout: TimeSpan.FromMilliseconds(150));
            var state = new FakeStateSource(new StateSnapshot("timeout", 1)) { SeqTo = 2 };
            var coordinator = new BackupCoordinatorGrain(
                state,
                new BackupRunner(
                    local, store, new BackupPipeline(local, store), new TestKeySource(), offsite));

            var result = await coordinator.RunAsync("i-1");
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("Verified", result.Envelope!.Status);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope WHERE target_id = 'local-disk' AND verified_at IS NOT NULL"));
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope WHERE target_id = 's3-offsite' AND verified_at IS NULL"));
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    [Fact] // AC-05
    public async Task Ac05_The_s3_credential_comes_only_from_config_and_reaches_no_state_log_or_route()
    {
        var handle = PostRoutes.Handle("s3cred");
        var secret = "S3SECRET-" + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var accessKey = "AKIA" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..16];
        var storage = NewDb();
        var volume = NewVolume();
        await using var s3 = await FakeS3.StartAsync("squid-backup", accessKey, secret);
        s3.FailNext(StatusCodes.Status500InternalServerError, times: 1); // the risky path: the failure log

        var output = new CapturedOutput();
        var previous = Console.Out;
        Console.SetOut(output); // Serilog's console sink is created by the Build below
        try
        {
            var recoveryKey = Convert.ToBase64String(RootKey.Create().AsSpan());
            await using var host = await BackupHost.StartAsync(
            [
                "--Storage:Path=" + storage,
                "--Logging:LogLevel:Default=Warning",
                "--Instance:Operators=" + handle,
                "--Backup:Local:Path=" + volume,
                "--Backup:IntervalMinutes=0",
                "--Squid:RecoveryKey=" + recoveryKey,
                "--Backup:S3:Endpoint=" + s3.Endpoint,
                "--Backup:S3:Region=" + s3.Options.Region,
                "--Backup:S3:Bucket=" + s3.Bucket,
                "--Backup:S3:AccessKey=" + accessKey,
                "--Backup:S3:SecretKey=" + secret,
            ]);

            var client = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await client.RegisterAsync(handle, PostRoutes.Password)).StatusCode);

            var run = await PostRoutes.SendAsync(
                client, HttpMethod.Post, "/api/admin/backup/run", null, client.Csrf);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode); // degrading never breaks the route
            var runBody = await run.Content.ReadAsStringAsync();

            // The configuration really coupled the destination: a signed request reached the fake,
            // and the degraded run was logged as the section B warning (the path that could leak).
            Assert.True(s3.RequestCount > 0, "Backup:S3:* must wire S3CompatibleTarget into the runner");
            var logs = output.Snapshot();
            Assert.Contains("s3-offsite", logs, StringComparison.Ordinal);

            // A second run, this time with the off-site healthy: the destination now holds real
            // ciphertext + headers, which is what the scan below has to look at.
            var healthy = await PostRoutes.SendAsync(
                client, HttpMethod.Post, "/api/admin/backup/run", null, client.Csrf);
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            var healthyBody = await healthy.Content.ReadAsStringAsync();
            Assert.NotEmpty(s3.Keys);

            var status = await PostRoutes.SendAsync(client, HttpMethod.Get, "/api/admin/backup/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            var statusBody = await status.Content.ReadAsStringAsync();

            // The credential appears in NONE of: a route response, the log, the grain-state files
            // or a single byte the destination stores (contract section 1: ciphertext + metadata).
            Assert.DoesNotContain(secret, runBody, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, healthyBody, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, statusBody, StringComparison.Ordinal);
            Assert.DoesNotContain(accessKey, runBody, StringComparison.Ordinal);
            Assert.DoesNotContain(accessKey, healthyBody, StringComparison.Ordinal);
            Assert.DoesNotContain(accessKey, statusBody, StringComparison.Ordinal);
            logs = output.Snapshot(); // the second run's lines are in too
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(accessKey, logs, StringComparison.Ordinal);
            foreach (var file in StorageFiles(storage))
            {
                var bytes = await File.ReadAllBytesAsync(file);
                Assert.False(Contains(bytes, secret), $"{Path.GetFileName(file)} carries the S3 secret");
                Assert.False(Contains(bytes, accessKey), $"{Path.GetFileName(file)} carries the S3 access key");
            }

            foreach (var key in s3.Keys)
            {
                var stored = s3.Read(key)!;
                Assert.False(Contains(stored, secret), $"{key} carries the S3 secret");
                Assert.False(Contains(stored, accessKey), $"{key} carries the S3 access key");
            }

            // Estado por destino over both runs: 2 verified local rows, the failed off-site row
            // without verified_at and the healthy one verified — four rows, no credential.
            Assert.Equal(4, await ProjectionDb.ScalarAsync<long>(storage,
                "SELECT COUNT(*) FROM backup_envelope"));
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(storage,
                "SELECT COUNT(*) FROM backup_envelope WHERE target_id = 's3-offsite' AND verified_at IS NULL"));
        }
        finally
        {
            Console.SetOut(previous);
            Cleanup(volume, storage);
        }
    }

    private static async Task MigrateAsync(string db) =>
        await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(db), SchemaMigrations.Load());

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"squid-offsite-{Guid.NewGuid():N}.db");

    private static string NewVolume() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"squid-offsite-{Guid.NewGuid():N}")).FullName;

    private static void Cleanup(string volume, string storage)
    {
        ProjectionDb.TryDelete(storage);
        try
        {
            Directory.Delete(volume, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a product failure; the OS cleans it up.
        }
    }

    /// <summary>The persisted grain state: the database plus whatever journal SQLite left.</summary>
    private static IEnumerable<string> StorageFiles(string storage) =>
        new[] { storage, storage + "-wal", storage + "-journal" }.Where(File.Exists);

    private static bool Contains(byte[] haystack, string needle)
    {
        var marker = Encoding.UTF8.GetBytes(needle);
        return haystack.AsSpan().IndexOf(marker) >= 0;
    }

    /// <summary>
    /// Console capture a live host logs into: every write and every read share one lock, so the
    /// snapshot cannot race a background log line the way a bare StringWriter would.
    /// </summary>
    private sealed class CapturedOutput : TextWriter
    {
        private readonly object _gate = new();
        private readonly StringBuilder _buffer = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Snapshot()
        {
            lock (_gate) return _buffer.ToString();
        }

        public override void Write(char value)
        {
            lock (_gate) _buffer.Append(value);
        }

        public override void Write(string? value)
        {
            lock (_gate) _buffer.Append(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_gate) _buffer.Append(buffer, index, count);
        }
    }
}
