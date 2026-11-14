using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Squid.Api.Federation;
using Squid.Grains.Peers;
using Squid.ReadModels.Federation;

namespace Squid.Service;

/// <summary>
/// The delivery motor of SPEC-0011 section G (FR-036, data-model §3.4): the hosted loop that
/// drains <c>peer_delivery</c>, one pass of due Pending rows at a time. Every row goes through
/// <see cref="PeerGrain"/> first — blocked pairs are never touched, an open circuit pushes the row
/// straight back to Pending with a future <c>NextAttemptAt</c> (AC-03: never a failure on the
/// spot), the per-pair quota decides how many sends a minute may leave — and only then does the
/// outbound pipeline do its R-05 re-evaluation, its signature and its POST. A transient failure
/// reschedules the row with the grain's exponential backoff and counts the attempt (AC-01), the
/// eighth attempt or a permanent refusal dead-letters it with motive and alert (AC-02), and a
/// success heals the peer. The hosted loop honours <c>Federation:DeliveryPumpSeconds</c> (default
/// 1 s; zero keeps the pump registered but idle, which is how a suite drives
/// <see cref="RunOnceAsync"/> by hand).
/// </summary>
public sealed class PeerDeliveryPump : BackgroundService
{
    /// <summary>Rows one pass picks up — a queue per instance is small, and the next pass continues.</summary>
    public const int BatchLimit = 32;

    /// <summary>How long the row waits when nothing went on the wire (no key): an attempt must not burn every tick.</summary>
    private static readonly TimeSpan KeyHold = TimeSpan.FromMinutes(1);

    private readonly SqlitePeerQueue _queue;
    private readonly IPeerDeliveryStore _deliveries;
    private readonly IServiceScopeFactory _scopes;
    private readonly PeerGrain _peer;
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private readonly ILogger<PeerDeliveryPump> _log;

    /// <summary>Wires the pump to the queue, the rows, the pipeline seam, the grain and the clock (tests pass their own).</summary>
    public PeerDeliveryPump(
        SqlitePeerQueue queue,
        IPeerDeliveryStore deliveries,
        IServiceScopeFactory scopes,
        PeerGrain peer,
        TimeProvider time,
        TimeSpan interval,
        ILogger<PeerDeliveryPump> log)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _interval = interval;
    }

    /// <summary>
    /// One pass: every Pending row whose <c>NextAttemptAt</c> has arrived, oldest first. Returns
    /// how many rows were examined; a row that fails hard throws and the hosted loop logs it —
    /// the rows before it already reached their store, the next pass picks the same point up.
    /// This is the function the tests invoke.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _queue.DueAsync(_time.GetUtcNow(), BatchLimit);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StepAsync(row, cancellationToken);
        }

        return rows.Count;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_interval <= TimeSpan.Zero)
        {
            return; // registered but idle: Federation:DeliveryPumpSeconds <= 0 (the suite's own clock)
        }

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
                // One bad row must not kill the motor: the pass is row by row, so the next poll
                // resumes exactly where this one stopped (the log names the cause).
                _log.LogWarning(failure, "peer delivery pass failed; it will resume from the rows still Pending");
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

    // ---- one row -----------------------------------------------------------------------------

    private async Task StepAsync(PeerQueueRow row, CancellationToken cancellationToken)
    {
        var delivery = ToDelivery(row);
        var domain = DomainOf(delivery.RecipientActorId);

        if (delivery.Attempts >= PeerGrain.MaxAttempts)
        {
            // AC-02's floor wherever the eighth attempt was counted: never a ninth send.
            await DeadLetterAsync(delivery, "max_attempts", domain);
            return;
        }

        if (domain is not null)
        {
            var admission = await _peer.AdmitAsync(domain);
            if (!admission.Send)
            {
                if (admission.DeferUntil is { } until && until > delivery.NextAttemptAt)
                {
                    // AC-03: open circuit — straight back to Pending with a future NextAttemptAt,
                    // no attempt burned and never a failure on the spot.
                    await _deliveries.SaveAsync(delivery with { NextAttemptAt = until });
                }

                return; // blocked pair or exhausted quota: the row keeps its place, untouched (AC-04)
            }
        }

        // A fresh pipeline per attempt: OutboundPipeline signs with the actor of THIS payload, and
        // ActorGrain keeps one activated key per instance — a pipeline held across attempts would
        // sign a second handle with the first one's key.
        await using var scope = _scopes.CreateAsyncScope();
        var outbound = scope.ServiceProvider.GetRequiredService<IOutboundPipeline>();
        var verdict = await outbound.SendAsync(delivery, cancellationToken);
        switch (verdict.State)
        {
            case PeerDeliveryState.Sent:
                FederationMetrics.DeliverySent();
                if (domain is not null)
                {
                    await _peer.RecordSuccessAsync(domain);
                }

                break;
            case PeerDeliveryState.Pending:
                await RescheduleAsync(delivery, verdict.Reason ?? "send_failed", domain);
                break;
            // Cancelled (R-05) and Failed (DLQ) are already rows with their reason and alert —
            // the pipeline wrote both the moment it decided.
        }
    }

    /// <summary>
    /// A send that did not happen: <c>send_failed</c> counts the attempt and reschedules with the
    /// grain's backoff — or dead-letters at the eighth (AC-01/AC-02) — while a missing key just
    /// waits, the attempt uncounted, because nothing reached the wire (SPEC-0011 section G:
    /// an absent Squid:RecoveryKey disables the sending, never the queue).
    /// </summary>
    private async Task RescheduleAsync(PeerDelivery delivery, string reason, string? domain)
    {
        var now = _time.GetUtcNow();
        if (reason != "send_failed")
        {
            await _deliveries.SaveAsync(delivery with { NextAttemptAt = now + KeyHold, Reason = reason });
            return;
        }

        var attempts = delivery.Attempts + 1;
        if (attempts >= PeerGrain.MaxAttempts)
        {
            await DeadLetterAsync(delivery with { Attempts = attempts }, "max_attempts", domain);
            return;
        }

        await _deliveries.SaveAsync(delivery with
        {
            Attempts = attempts,
            NextAttemptAt = now + _peer.Backoff(attempts),
            Reason = reason,
        });
        if (domain is not null)
        {
            await _peer.RecordFailureAsync(domain);
        }

        FederationMetrics.DeliveryRetried();
    }

    /// <summary>The row that can never go out, with its motive: DLQ, counter and warning (AC-02).</summary>
    private async Task DeadLetterAsync(PeerDelivery row, string reason, string? domain)
    {
        await _deliveries.SaveAsync(row with { State = PeerDeliveryState.Failed, Reason = reason });
        FederationMetrics.DeliveryDlq();
        _log.LogWarning(
            "peer delivery dead-lettered ({ActivityType} -> {PeerDomain}): {Reason}",
            row.Activity, domain ?? "unknown", reason);
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>
    /// The peer a recipient addresses: its authority — host plus port when it is not the default,
    /// so two instances sharing one IP are two peers. Null when the recipient is not an http(s)
    /// URI at all, which the pipeline itself answers as <c>invalid_recipient</c>.
    /// </summary>
    private static string? DomainOf(string recipientActorId) =>
        Uri.TryCreate(recipientActorId, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.Authority
            : null;

    /// <summary>
    /// The row as the edge knows it: Squid.ReadModels may not reference Squid.Api (the dependency
    /// rule), so the pump maps the reader's shape over — mechanical, in one place.
    /// </summary>
    private static PeerDelivery ToDelivery(PeerQueueRow row) => new(
        row.ActivityId,
        row.RecipientActorId,
        Enum.Parse<PeerActivity>(row.Activity),
        row.ObjectId,
        row.Payload,
        checked((int)row.PolicyVersion),
        checked((int)row.Attempts),
        DateTimeOffset.Parse(row.NextAttemptAt, CultureInfo.InvariantCulture),
        Enum.Parse<PeerDeliveryState>(row.State),
        row.Reason);
}
