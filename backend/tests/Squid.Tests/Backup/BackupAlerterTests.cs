using Squid.Api.Admin;
using Squid.Grains.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-05 of TASK-0055 at the decision itself: contract backup-recovery section 6 alerts when
/// <c>backup.last_success_at</c> is older than 24 h and immediately when
/// <c>backup.verify_failures</c> counts an unverified envelope — and stays silent otherwise.
/// </summary>
public sealed class BackupAlerterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ac05_A_last_success_older_than_24_h_alerts_and_a_fresh_one_does_not()
    {
        var fresh = Status(Now.AddHours(-23), verifyFailures: 0);
        Assert.Empty(BackupAlerter.Evaluate(fresh, Now));

        // Exactly 24 h is not OLDER than 24 h — the boundary the section fixes.
        Assert.Empty(BackupAlerter.Evaluate(Status(Now.AddHours(-24), 0), Now));

        var stale = Status(Now.AddHours(-25), 0);
        Assert.Equal(new[] { BackupAlerter.StaleAlert }, BackupAlerter.Evaluate(stale, Now));

        // No verified backup ever is stale too: an instance that never backed up is what the
        // alert exists to catch.
        Assert.Equal(new[] { BackupAlerter.StaleAlert }, BackupAlerter.Evaluate(Status(null, 0), Now));
    }

    [Fact]
    public void Ac05_Any_verify_failure_alerts_on_its_own()
    {
        var corrupted = Status(Now.AddHours(-1), verifyFailures: 1);
        Assert.Equal(new[] { BackupAlerter.VerifyAlert }, BackupAlerter.Evaluate(corrupted, Now));

        // Stale AND corrupt are both said — neither hides the other.
        Assert.Equal(
            new[] { BackupAlerter.StaleAlert, BackupAlerter.VerifyAlert },
            BackupAlerter.Evaluate(Status(null, 3), Now));
    }

    [Fact]
    public void Ac05_The_alert_is_emitted_as_a_warning()
    {
        var log = new CapturingLogger<BackupAlerter>();
        var alerter = new BackupAlerter(log, new FixedClock(Now));

        var raised = alerter.Raise(Status(Now.AddHours(-30), 2));

        Assert.Equal(new[] { BackupAlerter.StaleAlert, BackupAlerter.VerifyAlert }, raised);
        Assert.Equal(2, log.Lines.Count);
        Assert.Contains(BackupAlerter.StaleAlert, log.Lines[0], StringComparison.Ordinal);
        Assert.Contains(BackupAlerter.VerifyAlert, log.Lines[1], StringComparison.Ordinal);
    }

    private static BackupStatus Status(DateTimeOffset? lastSuccessAt, long verifyFailures) =>
        new(lastSuccessAt, 1, 100, verifyFailures, null, null);

    /// <summary>A clock pinned to one instant, so the alert window never moves under the test.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
