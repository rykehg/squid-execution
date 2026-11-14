using System.Security.Cryptography;

namespace Squid.Grains.Actors;

/// <summary>
/// Port that opens the runtime signing key of an actor with the secret of SPEC-0011
/// section B (GAP-09): the composition root binds it and <c>Squid.Api/Crypto</c> implements it,
/// so <c>Squid.Grains</c> keeps reaching neither the edge nor the crypto module (ADR-35). The
/// grain only ever asks by handle — which copy of the wrapped material opens it is the
/// implementation's concern (TASK-0051 reads the instance copy of the double wrap).
/// Contract: null when <c>Squid:RecoveryKey</c> is not configured or this actor has no instance
/// copy (federated output disabled, never a failure) and a typed cryptographic failure when the
/// configured secret does not open the copy, which the grain turns into
/// <see cref="SigningKeyActivationException"/>.
/// </summary>
public interface IActorSigningKeyRing
{
    /// <summary>Opens the instance copy of <paramref name="handle"/> with the runtime secret; null when there is none to open.</summary>
    Task<RSA?> OpenInstanceCopyAsync(string handle);
}
