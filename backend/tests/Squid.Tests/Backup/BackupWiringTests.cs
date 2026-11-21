using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Squid.Service;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-02 of TASK-0054 at the composition root: <c>Backup:Local:Path</c> is the volume the
/// registered target operates on. The host is built by <see cref="SquidServiceHost.Build"/> — the
/// same call dotnet run makes — but never started: resolving the target needs no silo, so this
/// test opens no port and cannot fight another host for one.
/// </summary>
public sealed class BackupWiringTests
{
    [Fact]
    public async Task Ac02_The_registered_target_operates_over_the_configured_backup_local_path()
    {
        var volume = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"squid-backup-volume-{Guid.NewGuid():N}")).FullName;
        var storage = Path.Combine(Path.GetTempPath(), $"squid-backup-host-{Guid.NewGuid():N}.db");
        try
        {
            await using var app = SquidServiceHost.Build(
            [
                "--Storage:Path=" + storage,
                "--Logging:LogLevel:Default=Warning",
                "--Backup:Local:Path=" + volume,
            ]);

            var target = app.Services.GetRequiredService<IBackupTarget>();
            Assert.IsType<LocalDiskTarget>(target);

            var envelope = new BackupEnvelope
            {
                Id = "bk-2026-11-21T1000Z-w1red00",
                KeyEpoch = 1,
                SeqFrom = 0,
                SeqTo = 1,
                ContentHash = "sha256:ab",
                SizeBytes = 4,
                TargetId = target.TargetId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await using (var source = new MemoryStream(Encoding.UTF8.GetBytes("data")))
            {
                await target.PutAsync(envelope, source, CancellationToken.None);
            }

            // The write landed in the CONFIGURED volume — the key flowed all the way to the target.
            Assert.True(File.Exists(Path.Combine(volume, envelope.Id + ".bin")));
            Assert.True(File.Exists(Path.Combine(volume, envelope.Id + ".json")));
        }
        finally
        {
            try
            {
                Directory.Delete(volume, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is not a product failure.
            }

            ProjectionDb.TryDelete(storage);
        }
    }
}
