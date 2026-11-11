namespace Squid.Domain.Objects;

/// <summary>
/// The <c>Actor.Status</c> of data-model §3.1: a <c>Suspended</c> actor does not publish, does
/// not enter a container and does not federate out; existing content stays. The terminal
/// <c>Deleted</c> state of §3.1 arrives with the account-deletion phase — this phase only needs
/// the two live states, and every state the machine grows joins this enum instead of a second
/// one. The enum lives with the objects because P-04's publish ladder is its first consumer
/// (SPEC-0009 section A: suspenso não publica, regra ativa desde a publicação).
/// </summary>
public enum ActorStatus
{
    /// <summary>The default: an actor with no status row at all publishes under the normal rules.</summary>
    Active,

    /// <summary>§3.1: refused at every publish and audited, while reading keeps working.</summary>
    Suspended,
}
