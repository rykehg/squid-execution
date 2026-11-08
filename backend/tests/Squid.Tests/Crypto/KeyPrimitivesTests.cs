using System.Security.Cryptography;
using System.Text.Json;
using Squid.Api.Crypto;

namespace Squid.Tests.Crypto;

/// <summary>AC-01..AC-04 of TASK-0012: the key primitives round-trip and never leak.</summary>
public sealed class KeyPrimitivesTests
{
    private const string Password = "correct horse battery staple";
    private const int Ed25519PublicKeySize = 32;

    [Fact]
    public void Ac01_RkIs32CspRngBytesAndNeverPrintsItself()
    {
        var rk = RootKey.Create();
        var other = RootKey.Create();

        Assert.Equal(RootKey.SizeInBytes, rk.AsSpan().Length);
        Assert.False(rk.AsSpan().SequenceEqual(other.AsSpan()), "two RKs came out identical");

        var printed = rk.ToString()!;
        Assert.DoesNotContain(Convert.ToBase64String(rk.AsSpan()), printed);
        Assert.DoesNotContain(Convert.ToHexString(rk.AsSpan()), printed);
    }

    [Fact]
    public void Ac01_SameRkDerivesSameEd25519AndOtherRkDerivesAnother()
    {
        var rk = RootKey.Create();

        var first = KeyDerivation.DeriveEd25519PublicKey(rk);
        var again = KeyDerivation.DeriveEd25519PublicKey(rk);

        Assert.Equal(Ed25519PublicKeySize, first.Length);
        Assert.Equal(first, again);
        Assert.NotEqual(first, KeyDerivation.DeriveEd25519PublicKey(RootKey.Create()));
    }

    [Fact]
    public void Ac01_RecoveryCodeIsNotAFunctionOfTheRk()
    {
        var rk = RootKey.Create();

        // If the RC were derived from the RK, the same RK would always produce the same RC.
        var first = KeyState.Create(rk, Password);
        var second = KeyState.Create(rk, Password);

        Assert.Equal(first.Ed25519PublicKey, second.Ed25519PublicKey);
        Assert.NotEqual(first.RecoveryCode, second.RecoveryCode);
    }

    [Fact]
    public void Ac02_RsaWrapRoundTripsAndWrongRkFailsAsTypedException()
    {
        var rk = RootKey.Create();
        using var rsa = RSA.Create(2048);
        var clear = rsa.ExportRSAPrivateKey();

        var wrapped = KeyDerivation.WrapRsaPrivateKey(rk, rsa);
        using var restored = KeyDerivation.UnwrapRsaPrivateKey(rk, wrapped);
        Assert.Equal(clear, restored.ExportRSAPrivateKey());

        var failure = Assert.ThrowsAny<CryptographicException>(() =>
        {
            using var denied = KeyDerivation.UnwrapRsaPrivateKey(RootKey.Create(), wrapped);
        });
        Assert.DoesNotContain(Convert.ToBase64String(clear), failure.Message);
    }

    [Fact]
    public void Ac03_Argon2idRoundTripsAndWrongPasswordDestroysNothing()
    {
        var rk = RootKey.Create();
        var encrypted = RootKeyCipher.Encrypt(rk, Password);
        var before = encrypted.ToArray();

        Assert.ThrowsAny<CryptographicException>(() => { _ = RootKeyCipher.Decrypt(encrypted, "another password"); });

        Assert.Equal(before, encrypted); // a failed attempt only reads the blob
        var restored = RootKeyCipher.Decrypt(encrypted, Password);
        Assert.Equal(rk.AsSpan().ToArray(), restored.AsSpan().ToArray());
    }

    [Fact]
    public void Ac04_SerializedStateCarriesNeitherRkNorPrivateKeyInClear()
    {
        var rk = RootKey.Create();
        var state = KeyState.Create(rk, Password);

        var json = JsonSerializer.Serialize(state);
        var printed = state.ToString()!;

        var rkBytes = rk.AsSpan();
        Assert.Contains("EncryptedRootKey", json, StringComparison.Ordinal); // it really serialized
        foreach (var representation in new[] { json, printed })
        {
            Assert.DoesNotContain(Convert.ToBase64String(rkBytes), representation);
            Assert.DoesNotContain(Convert.ToHexString(rkBytes), representation);
        }

        using var rsa = KeyDerivation.UnwrapRsaPrivateKey(rk, state.WrappedRsaPrivateKey);
        foreach (var clearPrivate in new[] { rsa.ExportPkcs8PrivateKey(), rsa.ExportRSAPrivateKey() })
        {
            Assert.DoesNotContain(Convert.ToBase64String(clearPrivate), json);
            Assert.DoesNotContain(Convert.ToHexString(clearPrivate), json);
        }
    }
}
