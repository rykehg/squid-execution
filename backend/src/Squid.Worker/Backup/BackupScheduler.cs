using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Squid.Api.Admin;
using Squid.Grains.Backup;

namespace Squid.Worker.Backup;

/// <summary>
/// The trigger of SPEC-0012 section C: a hosted loop that waits <c>Backup:IntervalMinutes</c>
/// (default 1440) and then runs <see cref="BackupCoordinatorGrain"/> on its own — no request, no
/// operator, no reminder. Each tick also raises the section 6 alerts against the registry, so an
/// instance nobody is looking at still says out loud when the last verified backup goes stale.
/// <para>
/// The loop never lets a run escape: a failed backup is a Warning and the next tick still comes
/// (AC-06 — the service keeps serving), and stopping cancels the wait instead of blocking shutdown.
/// </para>
/// </summary>
public sealed class BackupScheduler : IHostedService, IDisposable
{
    private readonly BackupCoordinatorGrain _coordinator;
    private readonly IBackupStatusSource _status;
    private readonly BackupAlerter _alerts;
    private readonly BackupOptions _options;
    private readonly ILogger<BackupScheduler> _log;
    private readonly CancellationTokenSource _stop = new();

    private Task? _loop;

    /// <summary>Binds the scheduler to the coordinator it drives and the status it alerts on.</summary>
    public BackupScheduler(
        BackupCoordinatorGrain coordinator,
        IBackupStatusSource status,
        BackupAlerter alerts,
        BackupOptions options,
        ILogger<BackupScheduler> log)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Starts the loop; a non-positive interval keeps it registered but idle.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Enabled)
        {
            _loop = Task.Run(() => LoopAsync(_stop.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <summary>Cancels the wait and lets the in-flight tick settle.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stop.CancelAsync();
        if (_loop is null)
        {
            return;
        }

        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // a cancelled wait is the shutdown path, not a failure
        }
    }

    /// <summary>One tick, the seam a test drives by hand: run a backup, then raise the alerts.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _coordinator.RunAsync(_options.InstanceId, cancellationToken);
            if (!result.Succeeded)
            {
                _log.LogWarning("backup run failed: {BackupError}", result.Error);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception failure)
        {
            _log.LogWarning(failure, "backup run failed");
            return;
        }

        await RaiseAlertsAsync(cancellationToken);
    }

    /// <summary>The section 6 alerts of the current registry, emitted as Warnings.</summary>
    public async Task RaiseAlertsAsync(CancellationToken cancellationToken)
    {
        var status = await _status.ReadAsync(cancellationToken);
        _alerts.Raise(status);
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.Interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunOnceAsync(cancellationToken);
        }
    }

    /// <summary>Releases the cancellation source the loop waits on.</summary>
    public void Dispose() => _stop.Dispose();
}
