using System.Diagnostics.Metrics;

namespace Squid.Service;

/// <summary>
/// The federation metrics of SPEC-0011 (FR-052, observability): every inbound flow that ends
/// without applying anything because nobody here understands it still counts, so "silent" is
/// measurable — <c>federation.unknown_activity</c> rises for an unknown type and for the GAP-42
/// types this phase only acknowledges (<c>Update</c>/<c>Announce</c>/<c>Join</c>/<c>Leave</c>/
/// <c>Block</c>). Count only, never a tag: the type is attacker-supplied and must not become
/// unbounded metric cardinality.
/// </summary>
public static class FederationMetrics
{
    /// <summary>The meter these counters live under.</summary>
    public const string MeterName = "Squid.Federation";

    /// <summary>The counter AC-06 names.</summary>
    public const string UnknownActivityCounter = "federation.unknown_activity";

    /// <summary>The counter of a send-time cancellation (TASK-0045, R-05/FR-038): policy said no at the instant of delivery.</summary>
    public const string DeliveryCancelledCounter = "federation.delivery_cancelled";

    /// <summary>Delivery by state (SPEC-0011 observability): one row the peer accepted.</summary>
    public const string DeliverySentCounter = "federation.delivery_sent";

    /// <summary>Delivery by state: one row back to Pending with a backoff — the motor's retry (AC-01).</summary>
    public const string DeliveryRetriedCounter = "federation.delivery_retried";

    /// <summary>The DLQ with alert (data-model §2.9 invariant, AC-02): one row that reached Failed.</summary>
    public const string DeliveryDlqCounter = "federation.delivery_dlq";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Unknown = Meter.CreateCounter<long>(UnknownActivityCounter);

    private static readonly Counter<long> Cancelled = Meter.CreateCounter<long>(DeliveryCancelledCounter);

    private static readonly Counter<long> Sent = Meter.CreateCounter<long>(DeliverySentCounter);

    private static readonly Counter<long> Retried = Meter.CreateCounter<long>(DeliveryRetriedCounter);

    private static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>(DeliveryDlqCounter);

    /// <summary>One activity arrived that this instance acknowledges with a 202 and applies nothing.</summary>
    public static void UnknownActivity() => Unknown.Add(1);

    /// <summary>One queued delivery was cancelled by the re-evaluation of R-05 — never sent, never retried.</summary>
    public static void DeliveryCancelled() => Cancelled.Add(1);

    /// <summary>One queued delivery was accepted by the peer (AC-01's success half).</summary>
    public static void DeliverySent() => Sent.Add(1);

    /// <summary>One queued delivery failed transitively and went back to Pending with a backoff.</summary>
    public static void DeliveryRetried() => Retried.Add(1);

    /// <summary>One queued delivery reached Failed: DLQ row written, motive kept, alert fired.</summary>
    public static void DeliveryDlq() => DeadLettered.Add(1);
}
