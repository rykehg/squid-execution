using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;

namespace Squid.Api.Crypto;

/// <summary>
/// Key material derived from the `RK` (SPEC-0007 B-02): the Ed25519 pair by
/// <c>HKDF(RK, "fed/ed25519-sign")</c> and the RSA-2048 private key wrapped by
/// <c>HKDF(RK, "fed/rsa-wrap")</c>. Derivation is pure — the same `RK` always yields the same
/// material — and only public halves leave this class in clear.
/// </summary>
public static class KeyDerivation
{
    /// <summary>HKDF info label of the signing pair (B-02).</summary>
    public const string Ed25519Info = "fed/ed25519-sign";

    /// <summary>HKDF info label that wraps the RSA private key (B-02).</summary>
    public const string RsaWrapInfo = "fed/rsa-wrap";

    private const int AesKeySize = 32;

    /// <summary>The 32-byte Ed25519 public key for this `RK`.</summary>
    public static byte[] DeriveEd25519PublicKey(RootKey rootKey)
    {
        var seed = Derive(rootKey, Ed25519Info, Ed25519PrivateKeyParameters.KeySize);
        try
        {
            return new Ed25519PrivateKeyParameters(seed).GeneratePublicKey().GetEncoded();
        }
        finally
        {
            // The derived private seed lives only in the key object above; this copy must not linger.
            CryptographicOperations.ZeroMemory(seed);
        }
    }

    /// <summary>Wraps the private key of <paramref name="rsa"/> so only the matching `RK` opens it.</summary>
    public static byte[] WrapRsaPrivateKey(RootKey rootKey, RSA rsa)
    {
        var key = Derive(rootKey, RsaWrapInfo, AesKeySize);
        try
        {
            return AesGcmBox.Seal(key, rsa.ExportRSAPrivateKey());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Opens a wrapped RSA private key. A wrong `RK` surfaces the typed AEAD failure as a
    /// <see cref="CryptographicException"/> whose message carries no key material.
    /// </summary>
    public static RSA UnwrapRsaPrivateKey(RootKey rootKey, byte[] wrapped)
    {
        ArgumentNullException.ThrowIfNull(wrapped);

        var key = Derive(rootKey, RsaWrapInfo, AesKeySize);
        try
        {
            var clear = AesGcmBox.Open(key, wrapped);
            try
            {
                var rsa = RSA.Create();
                rsa.ImportRSAPrivateKey(clear, out _);
                return rsa;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clear);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary><c>HKDF-SHA256(RK, info)</c> — HKDF here takes the key as bytes, so the root key is
    /// copied and the copy zeroed on the way out.</summary>
    private static byte[] Derive(RootKey rootKey, string label, int length)
    {
        var ikm = rootKey.AsSpan().ToArray();
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, length, info: Encoding.UTF8.GetBytes(label));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ikm);
        }
    }
}
