using System.Text;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-02 of TASK-0054: <c>IBackupTarget</c>'s five operations over <see cref="LocalDiskTarget"/>
/// on the configured volume — Put lands ciphertext + envelope, Get hands the bytes back, ListAsync
/// is the discovery of what was sent (ordered by seqTo, honouring fromSeq), Exists and Delete
/// close the contract of backup-recovery section 1.
/// </summary>
public sealed class LocalDiskTargetTests
{
    [Fact]
    public async Task Ac02_The_five_operations_run_over_the_configured_volume()
    {
        var volume = NewVolume();
        try
        {
            var target = new LocalDiskTarget(volume);
            Assert.Equal("local-disk", target.TargetId);

            var ciphertext = Encoding.UTF8.GetBytes("ciphertext-only-0123456789");
            var first = Envelope("bk-2026-11-21T1000Z-aaaa1111", seqFrom: 10, seqTo: 20);
            await using (var source = new MemoryStream(ciphertext))
            {
                await target.PutAsync(first, source, CancellationToken.None);
            }

            // Both halves landed under the id — the destination sees ciphertext + envelope only.
            Assert.True(File.Exists(Path.Combine(volume, first.Id + ".bin")));
            Assert.True(File.Exists(Path.Combine(volume, first.Id + ".json")));

            Assert.True(await target.ExistsAsync(first.Id, CancellationToken.None));
            await using (var reRead = await target.GetAsync(first.Id, CancellationToken.None))
            {
                using var buffer = new MemoryStream();
                await reRead.CopyToAsync(buffer);
                Assert.Equal(ciphertext, buffer.ToArray());
            }

            // A second, older envelope: ListAsync is ordered by seqTo and filters by fromSeq.
            var second = Envelope("bk-2026-11-21T1001Z-bbbb2222", seqFrom: 0, seqTo: 5);
            await using (var source = new MemoryStream(ciphertext))
            {
                await target.PutAsync(second, source, CancellationToken.None);
            }

            var listed = await target.ListAsync(0, CancellationToken.None);
            Assert.Equal(new[] { second.Id, first.Id }, listed.Select(envelope => envelope.Id));

            var found = listed[1];
            Assert.Equal(first.KeyEpoch, found.KeyEpoch);
            Assert.Equal(first.SeqFrom, found.SeqFrom);
            Assert.Equal(first.SeqTo, found.SeqTo);
            Assert.Equal(first.ContentHash, found.ContentHash);
            Assert.Equal(first.SizeBytes, found.SizeBytes);
            Assert.Equal(first.TargetId, found.TargetId);
            Assert.Equal(first.CreatedAt, found.CreatedAt);

            // fromSeq: an envelope whose coverage ends before fromSeq is not offered (20 yes, 21 no).
            Assert.Single(await target.ListAsync(first.SeqTo, CancellationToken.None));
            Assert.Empty(await target.ListAsync(first.SeqTo + 1, CancellationToken.None));

            await target.DeleteAsync(first.Id, CancellationToken.None);
            Assert.False(await target.ExistsAsync(first.Id, CancellationToken.None));
            Assert.Single(await target.ListAsync(0, CancellationToken.None));

            // Delete is idempotent: rotation/purge may repeat it (contract section 7).
            await target.DeleteAsync(first.Id, CancellationToken.None);
        }
        finally
        {
            DeleteVolume(volume);
        }
    }

    private static BackupEnvelope Envelope(string id, long seqFrom, long seqTo) => new()
    {
        Id = id,
        KeyEpoch = 1,
        SeqFrom = seqFrom,
        SeqTo = seqTo,
        ContentHash = "sha256:001122",
        SizeBytes = 30,
        TargetId = "local-disk",
        CreatedAt = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero),
    };

    private static string NewVolume() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"squid-backup-target-{Guid.NewGuid():N}")).FullName;

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
