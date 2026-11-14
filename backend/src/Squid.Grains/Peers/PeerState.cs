namespace Squid.Grains.Peers;

/// <summary>data-model §2.9's <c>Peer.Status</c> — the health word a remote domain carries.</summary>
public enum PeerStatus
{
    /// <summary>No remembered failure: deliveries go out as they come.</summary>
    Healthy,

    /// <summary>Failures remembered, but the backoff window has expired — the next send is a probe.</summary>
    Degraded,

    /// <summary>Circuit open: nothing goes out until <see cref="PeerState.BackoffUntil"/> passes.</summary>
    Down,

    /// <summary>Refused domain: nothing is ever sent (AC-04), no matter the health or quota.</summary>
    Blocked,
}

/// <summary>
/// The <c>peer</c> row of data-model §2.9 (SPEC-0011 section G): one record per remote domain,
/// holding the circuit (Status + BackoffUntil), the consecutive failure counter, the last success
/// and the per-pair send quota the contract §3 step 5 calls "configurável por par". A domain here
/// is the recipient authority (host plus port when it is not the default), so two instances
/// sharing one IP are two peers.
/// </summary>
/// <param name="Domain">The recipient authority this state belongs to.</param>
/// <param name="Status">Healthy | Degraded | Down (circuit open) | Blocked.</param>
/// <param name="BackoffUntil">When the circuit may admit a probe; null while no failure is remembered.</param>
/// <param name="FailureCount">Consecutive failures — the exponent of the domain's own backoff.</param>
/// <param name="LastSuccessAt">The last accepted delivery to this domain, or null.</param>
/// <param name="RateLimitPerMinute">Sends admitted per fixed minute for this pair (default 60).</param>
public sealed record PeerState(
    string Domain,
    PeerStatus Status,
    DateTimeOffset? BackoffUntil,
    int FailureCount,
    DateTimeOffset? LastSuccessAt,
    int RateLimitPerMinute)
{
    /// <summary>The quota a peer is born with — the same value the 0008 migration defaults the column to.</summary>
    public const int DefaultRateLimitPerMinute = 60;
}
