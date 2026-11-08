using System.Security.Cryptography;

namespace Squid.Api.Crypto;

/// <summary>
/// `RK` — the 32-byte root secret of a local actor (SPEC-0007 B-02, FR-004).
/// It reaches no log, response or persisted form in clear: <see cref="ToString"/> masks it, and the
/// only serialized shapes are the Argon2id and HKDF-wrapped ones carried by <see cref="KeyState"/>.
/// </summary>
public sealed class RootKey
{
    /// <summary>Length every root key has, in bytes.</summary>
    public const int SizeInBytes = 32;

    private readonly byte[] _bytes;

    private RootKey(byte[] bytes) => _bytes = bytes;

    /// <summary>A fresh `RK` straight from the platform CSPRNG.</summary>
    public static RootKey Create()
    {
        var bytes = new byte[SizeInBytes];
        RandomNumberGenerator.Fill(bytes);
        return new RootKey(bytes);
    }

    /// <summary>The raw key for the derivation primitives — callers must never serialize it.</summary>
    public ReadOnlySpan<byte> AsSpan() => _bytes;

    /// <summary>Masked on purpose: a printed `RK` would be a leaked `RK` (B-03).</summary>
    public override string ToString() => $"RootKey(bytes={SizeInBytes}, masked)";

    /// <summary>Rebuilds a key from bytes an AEAD already authenticated (kit import).</summary>
    internal static RootKey FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeInBytes)
            throw new ArgumentException($"An RK needs exactly {SizeInBytes} bytes, got {bytes.Length}.", nameof(bytes));

        return new RootKey(bytes.ToArray());
    }
}
