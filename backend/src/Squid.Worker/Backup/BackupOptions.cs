using System.Globalization;
using Microsoft.Extensions.Configuration;
using Squid.Grains.Backup;

namespace Squid.Worker.Backup;

/// <summary>
/// The two knobs of the trigger SPEC-0012 section C fixes: the instance a run is keyed by and the
/// interval the scheduler fires on (<c>Backup:IntervalMinutes</c>, default 1440 — the default the
/// human accepted 2026-11-20 21:07:26, GAP-53). The interval is an int of MINUTES because that is
/// what the specification's input table fixes; a value of 0 (or less) keeps the scheduler
/// registered but idle, the same rule the delivery pump follows for its seconds.
/// </summary>
public sealed record BackupOptions(string InstanceId, TimeSpan Interval)
{
    /// <summary>What an instance gets when <c>Backup:IntervalMinutes</c> names no number.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1440);

    /// <summary>True when the scheduler may fire — a non-positive interval disables it.</summary>
    public bool Enabled => Interval > TimeSpan.Zero;

    /// <summary>Reads the configuration; blank or malformed minutes keep the 1440 default.</summary>
    public static BackupOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var interval = DefaultInterval;
        if (int.TryParse(configuration["Backup:IntervalMinutes"], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var minutes))
        {
            interval = TimeSpan.FromMinutes(minutes);
        }

        return new BackupOptions(
            BackupInstance.Resolve(configuration[BackupInstance.IdKey]), interval);
    }
}
