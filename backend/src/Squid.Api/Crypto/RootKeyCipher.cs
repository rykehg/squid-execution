using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Squid.Api.Crypto;

/// <summary>
/// The `RK` sealed under the actor password: Argon2id derives the key, AES-256-GCM seals the bytes
/// (SPEC-0007 B-04, data-model §2.12 `EncryptedRootKey`). A wrong password fails as a typed
/// exception and only ever reads the blob, so nothing is destroyed by a failed attempt.
/// </summary>
public static class RootKeyCipher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;

    // BouncyCastle's own Argon2id defaults (memory 4096 KiB, 3 passes, 1 lane), registered here so a
    // package bump cannot silently change the cost of the password KDF (SPEC-0007 non-functional).
    private const int Argon2Iterations = 3;
    private const int Argon2MemoryKib = 4096;
    private const int Argon2Parallelism = 1;

    /// <summary><c>salt || nonce || tag || RK</c> — 76 bytes for a 32-byte `RK`.</summary>
    public static byte[] Encrypt(RootKey rootKey, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);
        try
        {
            var box = AesGcmBox.Seal(key, rootKey.AsSpan());
            var encrypted = new byte[SaltSize + box.Length];
            salt.CopyTo(encrypted, 0);
            box.CopyTo(encrypted, SaltSize);
            return encrypted;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Recovers the original 32 bytes. A wrong password throws
    /// <see cref="AuthenticationTagMismatchException"/>; <paramref name="encrypted"/> is only read.
    /// </summary>
    public static RootKey Decrypt(byte[] encrypted, string password)
    {
        ArgumentNullException.ThrowIfNull(encrypted);
        ArgumentNullException.ThrowIfNull(password);

        var minimum = SaltSize + AesGcmBox.NonceSize + AesGcmBox.TagSize + RootKey.SizeInBytes;
        if (encrypted.Length < minimum)
            throw new ArgumentException($"An encrypted RK needs {minimum} bytes, got {encrypted.Length}.", nameof(encrypted));

        var salt = encrypted.AsSpan(0, SaltSize).ToArray();
        var box = encrypted.AsSpan(SaltSize).ToArray();
        var key = DeriveKey(password, salt);
        try
        {
            return RootKey.FromBytes(AesGcmBox.Open(key, box));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        var generator = new Argon2BytesGenerator();
        generator.Init(new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithSalt(salt)
            .WithIterations(Argon2Iterations)
            .WithMemoryAsKB(Argon2MemoryKib)
            .WithParallelism(Argon2Parallelism)
            .Build());

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var key = new byte[KeySize];
            generator.GenerateBytes(passwordBytes, key);
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
