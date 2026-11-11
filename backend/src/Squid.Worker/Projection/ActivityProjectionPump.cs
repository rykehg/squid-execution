using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Squid.ReadModels.Projections;

namespace Squid.Worker.Projection;

/// <summary>
/// The projection pump of SPEC-0009 section B: a hosted service of the <c>Squid.Worker</c>
/// composition root that drains the activity log into <c>Squid.ReadModels</c>' tables, strictly in
/// <c>Seq</c> order and strictly off the write path (R-18). It keeps no position of its own: every
/// pass starts at <see cref="IActivityProjector.LastProjectedSeqAsync"/>, the high-water mark the
/// projection tables themselves hold, so a restart after a failure continues at the first event
/// that has no row yet (AC-04) and a re-run over already projected events writes nothing twice.
/// One failed event stops that pass — the rows before it are committed, the rows after it are not —
/// and the next pass picks the same point up; a hosted run logs the failure and keeps polling.
/// </summary>
public sealed class ActivityProjectionPump : BackgroundService
{
    /// <summary>How long a hosted run waits between passes (tests call the pass directly).</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(1);

    private readonly SqliteActivityLog _log;
    private readonly IActivityProjector _projector;
    private readonly ILogger<ActivityProjectionPump> _logger;

    /// <summary>Wires the pump to the log it reads and the projector it drives (tests pass a null logger's instance).</summary>
    public ActivityProjectionPump(
        SqliteActivityLog log,
        IActivityProjector projector,
        ILogger<ActivityProjectionPump>? logger = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// One pass: every event after the projection's own high-water mark, projected in order.
    /// Returns how many events were consumed (0 when caught up) and throws when an event fails —
    /// the caller decides what a failure means. This is the function the tests invoke.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var since = await _projector.LastProjectedSeqAsync();
        var events = await _log.ReadAsync(since);

        foreach (var activity in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _projector.ProjectAsync(activity);
        }

        return events.Count;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                // One bad event must not kill the worker: the pass is atomic per event, so the
                // next poll resumes exactly where this one stopped (the log names the event).
                _logger.LogWarning(failure, "projection pass failed; it will resume from the last projected seq");
            }

            try
            {
                await Task.Delay(Pause, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
