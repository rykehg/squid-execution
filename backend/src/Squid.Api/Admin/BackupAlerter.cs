using Microsoft.Extensions.Logging;
using Squid.Grains.Backup;

namespace Squid.Api.Admin;

/// <summary>
/// The two alerts contract backup-recovery section 6 and SPEC-0012 section C demand: a
/// <c>backup.last_success_at</c> older than 24 h, and any <c>backup.verify_failures</c> — a
/// divergence that may mean corruption and therefore alerts immediately. The decision is a pure
/// function so a test can assert it, and the emission is one structured Warning per alert: this
/// phase has no metrics exporter (the same alert-over-log rule the delivery queue follows).
/// </summary>
public sealed class BackupAlerter(ILogger<BackupAlerter> log, TimeProvider clock)
{
    /// <summary>The staleness alert: no verified backup in the last 24 hours (section 6).</summary>
    public const string StaleAlert = "backup.last_success_at is older than 24 h";

    /// <summary>The immediate alert: an envelope was registered without a confirmed re-read (section 6).</summary>
    public const string VerifyAlert = "backup.verify_failures counted an unverified envelope";

    /// <summary>The window section 6 fixes for <c>backup.last_success_at</c>.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    /// <summary>
    /// The alerts <paramref name="status"/> currently warrants, each emitted as a Warning.
    /// A missing <c>last_success_at</c> is stale too: an instance that never verified a backup is
    /// exactly what section 6 exists to catch.
    /// </summary>
    public IReadOnlyList<string> Raise(BackupStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var alerts = Evaluate(status, clock.GetUtcNow());
        foreach (var alert in alerts)
        {
            log.LogWarning("{BackupAlert}", alert);
        }

        return alerts;
    }

    /// <summary>The alerts of <paramref name="status"/> at <paramref name="now"/>, without emitting them.</summary>
    public static IReadOnlyList<string> Evaluate(BackupStatus status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(status);
        var alerts = new List<string>(2);
        if (status.LastSuccessAt is null || now - status.LastSuccessAt > StaleAfter)
        {
            alerts.Add(StaleAlert);
        }

        if (status.VerifyFailures > 0)
        {
            alerts.Add(VerifyAlert);
        }

        return alerts;
    }
}
