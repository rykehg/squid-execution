using Squid.Grains.Backup;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-01 and AC-06 of TASK-0055 at the coordinator: the contract backup-recovery section 3 flow
/// runs per <c>instanceId</c> in its fixed order — last consolidated sequence → snapshot from the
/// aggregate → the Worker serializes, ciphers, sends, re-reads and registers — with the interval
/// of the next run discovered through the destination and never through state kept here; and a
/// destination that is down is REPORTED instead of escaping, with nothing registered.
/// </summary>
public sealed class BackupCoordinatorTests
{
    [Fact] // AC-01
    public async Task Ac01_The_coordinator_runs_the_section_3_flow_and_registers_the_envelope()
    {
        var volume = NewVolume();
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var target = new LocalDiskTarget(volume);
            var store = StoreOf(db);
            var state = new FakeStateSource(new StateSnapshot("coordinator", 3)) { SeqTo = 7 };
            var coordinator = new BackupCoordinatorGrain(
                state, new BackupRunner(target, store, new BackupPipeline(target, store), new TestKeySource()));

            var first = await coordinator.RunAsync("i-1");

            // The flow, in order: sequence of the instance, then the snapshot of that window,
            // then the Worker — which registered a verified envelope.
            Assert.True(first.Succeeded, first.Error);
            Assert.Equal(new[] { "seq:i-1:7", "snapshot:i-1:0..7" }, state.Calls);
            Assert.Equal("Verified", first.Envelope!.Status);
            Assert.Equal(0, first.Envelope.SeqFrom);   // nothing was sent yet: the full snapshot
            Assert.Equal(7, first.Envelope.SeqTo);
            Assert.Equal("local-disk", first.Envelope.TargetId);
            Assert.StartsWith("sha256:", first.Envelope.ContentHash, StringComparison.Ordinal);
            Assert.NotNull(first.Envelope.VerifiedAt);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope"));

            // Second run: seqFrom comes from what the destination already holds (contract §1 —
            // ListAsync is the single discovery), so the payload is exactly the new interval.
            state.SeqTo = 12;
            state.Calls.Clear();
            var second = await coordinator.RunAsync("i-1");

            Assert.True(second.Succeeded, second.Error);
            Assert.Equal(new[] { "seq:i-1:12", "snapshot:i-1:8..12" }, state.Calls);
            Assert.Equal(8, second.Envelope!.SeqFrom);
            Assert.Equal(12, second.Envelope.SeqTo);
            Assert.Equal(2, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope"));
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    [Fact] // AC-06
    public async Task Ac06_A_destination_failure_is_reported_and_registers_nothing()
    {
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var target = new BrokenTarget();
            var store = StoreOf(db);
            var state = new FakeStateSource(new StateSnapshot("coordinator", 1)) { SeqTo = 4 };
            var coordinator = new BackupCoordinatorGrain(
                state, new BackupRunner(target, store, new BackupPipeline(target, store), new TestKeySource()));

            // The failure is REPORTED as a result — it never escapes the coordinator to take the
            // caller (scheduler or route, hence the process) down with it.
            var result = await coordinator.RunAsync("i-1");
            Assert.False(result.Succeeded);
            Assert.Null(result.Envelope);
            Assert.Contains("unavailable", result.Error, StringComparison.OrdinalIgnoreCase);

            // And nothing half-written is registered: the registry stays exactly as it was.
            Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope"));
        }
        finally
        {
            ProjectionDb.TryDelete(db);
        }
    }

    [Fact] // AC-06, the aggregate side
    public async Task Ac06_A_state_source_failure_is_reported_instead_of_throwing()
    {
        var volume = NewVolume();
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var target = new LocalDiskTarget(volume);
            var store = StoreOf(db);
            var state = new FakeStateSource(new StateSnapshot("coordinator", 1))
            {
                SeqTo = 1,
                Failure = new InvalidOperationException("the aggregate is unavailable"),
            };
            var coordinator = new BackupCoordinatorGrain(
                state, new BackupRunner(target, store, new BackupPipeline(target, store), new TestKeySource()));

            var result = await coordinator.RunAsync("i-1");
            Assert.False(result.Succeeded);
            Assert.Null(result.Envelope);
            Assert.Contains("unavailable", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(db,
                "SELECT COUNT(*) FROM backup_envelope"));
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    private static string NewVolume() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"squid-coordinator-{Guid.NewGuid():N}")).FullName;

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"squid-coordinator-{Guid.NewGuid():N}.db");

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
}
