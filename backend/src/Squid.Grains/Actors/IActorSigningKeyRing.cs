using System.Security.Cryptography;

namespace Squid.Grains.Actors;

/// <summary>
/// Port that opens the wrapped signing key of an actor with the runtime secret of SPEC-0011
/// section B (GAP-09): the composition root binds it and <c>Squid.Api/Crypto</c> implements it,
/// so <c>Squid.Grains</c> keeps reaching neither the edge nor the crypto module (ADR-35).
/// Contract: null when <c>Squid:RecoveryKey</c> is not configured — federated output disabled,
/// never a failure — and a typed cryptographic failure when the configured secret does not open
/// the wrapped key, which the grain turns into <see cref="SigningKeyActivationException"/>.
/// </summary>
public interface IActorSigningKeyRing
{
    /// <summary>Opens the wrapped key (base64) with the runtime secret; null when none is configured.</summary>
    RSA? OpenSigningKey(string wrappedSigningKey);
}
