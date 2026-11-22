using System.Text.Json;
using Squid.Api.Crypto;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// The two QA-0054 findings the backlog parked for this task: S-47 — the destination header used
/// to be serialized BEFORE the re-read could decide, so a verified backup always read back
/// `Pending`/`verifiedAt: null`; S-48 — the header write was not atomic, so a torn file made
/// <c>ListAsync</c> throw instead of skipping, breaking the only discovery contract §1 allows.
/// </summary>
public sealed class BackupHeaderTests
{
    [Fact]
    public async Task S47_The_destination_header_carries_the_state_the_reread_decided()
    {
        var volume = NewVolume();
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var target = new LocalDiskTarget(volume);
            var pipeline = new BackupPipeline(target, StoreOf(db));

            var envelope = await pipeline.RunAsync(
                new StateSnapshot("header", 2), 0, 2, 1, RootKey.Create(), CancellationToken.None);

            Assert.Equal(BackupEnvelopeStatus.Verified, envelope.Status);

            // The FILE, not the object the pipeline returned: what a consumer of the destination
            // reads back must be the post-verification state (S-47).
            var header = JsonSerializer.Deserialize<BackupEnvelope>(
                await File.ReadAllTextAsync(Path.Combine(volume, envelope.Id + ".json")),
                BackupEnvelope.JsonOptions);
            Assert.NotNull(header);
            Assert.Equal(BackupEnvelopeStatus.Verified, header!.Status);
            Assert.Equal(envelope.VerifiedAt, header.VerifiedAt);

            // And the write left no temp file behind: only the two halves of the envelope.
            var files = Directory.EnumerateFiles(volume).ToList();
            Assert.Equal(2, files.Count);
            Assert.DoesNotContain(files, file => file.EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(volume, db);
        }
    }

    [Fact]
    public async Task S48_A_header_it_cannot_read_is_skipped_instead_of_breaking_discovery()
    {
        var volume = NewVolume();
        try
        {
            var target = new LocalDiskTarget(volume);
            var good = new BackupEnvelope
            {
                Id = "bk-2026-11-21T1100Z-gggg1111",
                KeyEpoch = 1,
                SeqFrom = 0,
                SeqTo = 4,
                ContentHash = "sha256:001122",
                SizeBytes = 30,
                TargetId = "local-disk",
                CreatedAt = new DateTimeOffset(2026, 11, 21, 11, 0, 0, TimeSpan.Zero),
            };
            await using (var source = new MemoryStream("ciphertext-only-0123456789"u8.ToArray()))
            {
                await target.PutAsync(good, source, CancellationToken.None);
            }

            // A torn header (the S-48 scenario) and a temp file from an interrupted write: both
            // sit in the volume beside a perfectly good envelope.
            await File.WriteAllTextAsync(Path.Combine(volume, "bk-2026-11-21T1101Z-torn0001.json"), "{\"id\":\"bk-");
            await File.WriteAllTextAsync(Path.Combine(volume, "bk-2026-11-21T1102Z-pend0001.tmp"), "{\"id\":\"x");

            var listed = await target.ListAsync(0, CancellationToken.None);

            var found = Assert.Single(listed);
            Assert.Equal(good.Id, found.Id);
        }
        finally
        {
            DeleteVolume(volume);
        }
    }

    private static string NewVolume() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"squid-header-{Guid.NewGuid():N}")).FullName;

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"squid-header-{Guid.NewGuid():N}.db");

    private static async Task MigrateAsync(string db) =>
        await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(db), SchemaMigrations.Load());

    private static SqliteBackupEnvelopeStore StoreOf(string db) => new(ProjectionDb.ConnectionOf(db));

    private static void Cleanup(string volume, string db)
    {
        DeleteVolume(volume);
        ProjectionDb.TryDelete(db);
    }

    private static void DeleteVolume(string volume)
    {
        try
        {
            Directory.Delete(volume, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a product failure; the OS cleans it up.
        }
    }
}
