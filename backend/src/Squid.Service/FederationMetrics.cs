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

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Unknown = Meter.CreateCounter<long>(UnknownActivityCounter);

    /// <summary>One activity arrived that this instance acknowledges with a 202 and applies nothing.</summary>
    public static void UnknownActivity() => Unknown.Add(1);
}
