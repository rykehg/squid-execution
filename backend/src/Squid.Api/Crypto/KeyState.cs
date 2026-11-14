using System.Security.Cryptography;

namespace Squid.Api.Crypto;

/// <summary>
/// What registration keeps for a local actor (SPEC-0007 B-02): wrapped or public material only —
/// no clear `RK` and no clear private key — so serializing it cannot leak either one (AC-04).
/// </summary>
public sealed record KeyState
{
    /// <summary>Length of the `RC`, in bytes. The spec fixes only its independence from the `RK`.</summary>
    public const int RecoveryCodeSizeInBytes = 32;

    /// <summary>`RK` sealed with Argon2id under the actor password — the kit's `EncryptedRootKey`.</summary>
    public required byte[] EncryptedRootKey { get; init; }

    /// <summary>RSA-2048 private key wrapped by <c>HKDF(RK, "fed/rsa-wrap")</c> — data-model `WrappedSigningKey`.</summary>
    public required byte[] WrappedRsaPrivateKey { get; init; }

    /// <summary>RSA-2048 public key (SPKI DER) — safe to publish in the profile (B-10).</summary>
    public required byte[] RsaPublicKey { get; init; }

    /// <summary>Ed25519 public key (32 bytes) derived from the `RK` (B-02).</summary>
    public required byte[] Ed25519PublicKey { get; init; }

    /// <summary>`RC` (B-02): fresh CSPRNG bytes, independent of the `RK`.</summary>
    public required byte[] RecoveryCode { get; init; }

    /// <summary>
    /// The SAME RSA private key wrapped a second time with the instance `Squid:RecoveryKey`
    /// (SPEC-0011 section B, duplo embrulho — TASK-0051): what registration persists in its own
    /// table (<c>0009_*</c>) for the runtime to open. Null when no usable secret was configured —
    /// no copy is born and registration behaves exactly as before (AC-01). Wrapped bytes only;
    /// serializing this record still cannot leak a private key (AC-04).
    /// </summary>
    public byte[]? InstanceWrappedRsaPrivateKey { get; init; }

    /// <summary>
    /// Generates the whole key set of a registration from one `RK` and the actor password.
    /// <paramref name="instanceKeys"/> is the double wrap of TASK-0051: when the boot secret is
    /// configured, the very RSA generated here is also sealed with it — the clear key never
    /// leaves this method.
    /// </summary>
    public static KeyState Create(RootKey rootKey, string password, RuntimeSigningKeyRing? instanceKeys = null)
    {
        using var rsa = RSA.Create(2048);
        return new KeyState
        {
            EncryptedRootKey = RootKeyCipher.Encrypt(rootKey, password),
            WrappedRsaPrivateKey = KeyDerivation.WrapRsaPrivateKey(rootKey, rsa),
            InstanceWrappedRsaPrivateKey = instanceKeys?.WrapInstanceCopy(rsa),
            RsaPublicKey = rsa.ExportSubjectPublicKeyInfo(),
            Ed25519PublicKey = KeyDerivation.DeriveEd25519PublicKey(rootKey),
            RecoveryCode = RandomNumberGenerator.GetBytes(RecoveryCodeSizeInBytes),
        };
    }
}
