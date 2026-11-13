using System.Security.Cryptography;

namespace Squid.Grains.Actors;

/// <summary>
/// The activated runtime signing key of an actor (SPEC-0011 section B): it signs bytes and hands
/// back nothing else, so the private key itself never leaves the grain — no route, log or row can
/// carry material it was never given (AC-02). In memory for the lifetime of the activation only.
/// </summary>
public sealed class ActorSigner(RSA key)
{
    /// <summary>RSASSA-PKCS1-v1_5 over SHA-256 — the draft-cavage <c>rsa-sha256</c> of ADR-28.</summary>
    public byte[] Sign(byte[] data) =>
        key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    /// <summary>Masked on purpose: printing a signer would print a door to the private key.</summary>
    public override string ToString() => "ActorSigner(masked)";
}
