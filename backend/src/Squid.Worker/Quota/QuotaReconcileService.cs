using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Squid.Worker.Quota;

/// <summary>
/// The daily hosted sweep of SPEC-0009 section F (AC-04): one pass as soon as the worker starts
/// and then every <c>Quota:ReconcileHours</c> (default 24). A failing sweep logs and keeps the
/// schedule — the ledger keeps the last good value instead of a half-written one. Tests call
/// <see cref="RunOnceAsync"/> directly, which is the same pass the loop runs.
/// </summary>
public sealed class QuotaReconcileService : BackgroundService
{
    private readonly QuotaReconciler _reconciler;

    private readonly TimeSpan _interval;

    private readonly ILogger<QuotaReconcileService> _logger;

    /// <summary>Wires the hosted run to the sweep it drives (tests pass a null logger's instance).</summary>
    public QuotaReconcileService(
        QuotaReconciler reconciler,
        TimeSpan interval,
        ILogger<QuotaReconcileService>? logger = null)
    {
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
        _interval = interval;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>One sweep over the blob volume into the ledger — the pass the tests invoke.</summary>
    public Task<long> RunOnceAsync(CancellationToken cancellationToken = default) =>
        _reconciler.ReconcileAsync(cancellationToken);

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
                // One failed sweep must not stop the schedule: the ledger keeps the last good value.
                _logger.LogWarning(failure, "quota reconcile sweep failed; it keeps the last ledger value");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
