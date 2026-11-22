using Microsoft.Extensions.Configuration;
using Squid.Api.Crypto;
using Squid.Grains.Backup;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-02 of TASK-0055: the scheduler fires a backup ALL BY ITSELF after
/// <c>Backup:IntervalMinutes</c> — no request, no operator, no route — and the option resolves to
/// the 1440 default the specification fixes when no configuration names an interval.
/// </summary>
[Collection("ServiceHost")]
public sealed class BackupSchedulerTests
{
    [Fact]
    public void Ac02_The_interval_defaults_to_1440_minutes_and_honours_the_configuration()
    {
        var empty = BackupOptions.From(new ConfigurationBuilder().Build());
        Assert.Equal(TimeSpan.FromMinutes(1440), empty.Interval);
        Assert.Equal(BackupInstance.DefaultId, empty.InstanceId);
        Assert.True(empty.Enabled);

        var configured = BackupOptions.From(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Backup:IntervalMinutes"] = "1",
                ["Backup:InstanceId"] = "squid-a",
            }).Build());
        Assert.Equal(TimeSpan.FromMinutes(1), configured.Interval);
        Assert.Equal("squid-a", configured.InstanceId);

        // 0 keeps the scheduler registered but idle — the rule the delivery pump follows.
        var idle = BackupOptions.From(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Backup:IntervalMinutes"] = "0" }).Build());
        Assert.False(idle.Enabled);
    }

    [Fact] // AC-02
    public async Task Ac02_The_scheduler_runs_a_backup_on_its_own_after_the_interval()
    {
        var storage = Path.Combine(Path.GetTempPath(), $"squid-scheduler-{Guid.NewGuid():N}.db");
        var volume = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"squid-scheduler-{Guid.NewGuid():N}")).FullName;
        var recoveryKey = Convert.ToBase64String(RootKey.Create().AsSpan());
        try
        {
            await using var host = await BackupHost.StartAsync(
            [
                "--Storage:Path=" + storage,
                "--Logging:LogLevel:Default=Warning",
                "--Backup:Local:Path=" + volume,
                "--Backup:IntervalMinutes=1",   // the low value AC-02 asks the test to use
                "--Squid:RecoveryKey=" + recoveryKey,
            ]);

            // No request of any kind was made: right after boot nothing has been backed up yet,
            // because the scheduler waits its whole interval before the first run.
            Assert.Equal(0, await CountAsync(storage));

            // One interval later the backup exists — produced without a single HTTP call.
            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (await CountAsync(storage) == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            Assert.Equal(1, await CountAsync(storage));
        }
        finally
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
    }

    private static async Task<long> CountAsync(string storage) =>
        await ProjectionDb.ScalarAsync<long>(storage, "SELECT COUNT(*) FROM backup_envelope");
}
