using System.Diagnostics;

namespace Squid.Service;

/// <summary>
/// The trace seam of the outbound delivery (SPEC-0011 non-functional observability, SC-015): one
/// ActivitySource for every federated send, whose single tag <c>peer.domain</c> carries the
/// recipient's domain and nothing else — never the actor URI, never a handle (the tag is a fixed
/// key with an attacker-relevant value kept low-cardinality and PII-free by construction).
/// </summary>
public static class FederationTrace
{
    /// <summary>The source the host subscribes to and a test listens on.</summary>
    public const string SourceName = "Squid.Federation";

    /// <summary>The source itself — started spans are exported only where something listens.</summary>
    public static readonly ActivitySource Source = new(SourceName);
}
