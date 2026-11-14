namespace Squid.Grains.Peers;

/// <summary>
/// The state port of <see cref="PeerGrain"/> (SPEC-0011 section G): the circuit, the failure
/// counter and the per-pair quota of one remote domain, persisted by <c>Squid.Service</c> over the
/// <c>peer</c> table of <c>0008_federation.sql</c> (ADR-35: this project declares the port, it
/// never learns where the row lives).
/// </summary>
public interface IPeerStateStore
{
    /// <summary>One domain's state, or null when the domain never failed and was never configured.</summary>
    Task<PeerState?> FindAsync(string domain);

    /// <summary>Inserts or replaces one domain's state — the whole row, last write wins.</summary>
    Task SaveAsync(PeerState state);
}
