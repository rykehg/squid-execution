using System.Security.Cryptography;

namespace Squid.Api.Crypto;

/// <summary>
/// The one authenticated box of this module: <c>nonce || tag || ciphertext</c> under AES-256-GCM.
/// A wrong key fails as a typed <see cref="AuthenticationTagMismatchException"/> instead of
/// handing back a garbage plaintext.
/// </summary>
internal static class AesGcmBox
{
    internal const int NonceSize = 12;
    internal const int TagSize = 16;

    /// <summary>Seals <paramref name="plaintext"/> under <paramref name="key"/>.</summary>
    internal static byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var box = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(box);
        tag.CopyTo(box.AsSpan(NonceSize));
        ciphertext.CopyTo(box.AsSpan(NonceSize + TagSize));
        return box;
    }

    /// <summary>Opens a sealed box; the typed AEAD failure of a wrong key propagates untouched.</summary>
    internal static byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> box)
    {
        if (box.Length < NonceSize + TagSize)
            throw new ArgumentException(
                $"A sealed box needs at least {NonceSize + TagSize} bytes, got {box.Length}.", nameof(box));

        var plaintext = new byte[box.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            box.Slice(0, NonceSize),
            box.Slice(NonceSize + TagSize),
            box.Slice(NonceSize, TagSize),
            plaintext);
        return plaintext;
    }
}
