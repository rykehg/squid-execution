using System.Security.Cryptography;
using Squid.Api.Crypto;

namespace Squid.Tests.Crypto;

/// <summary>
/// The loader behind TASK-0042 AC-01/AC-03: base64 of a 32-byte <c>RK</c> in, ring out. Anything
/// else never breaks the boot — it only decides whether opening a key opens it, refuses loudly, or
/// has nothing to open with.
/// </summary>
public sealed class RuntimeSigningKeyRingTests
{
    [Theory] // AC-01: absent means disabled and disabled means nothing to open with
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_value_disables_the_ring_silently(string? value)
    {
        var ring = RuntimeSigningKeyRing.FromConfiguration(value);

        Assert.False(ring.OutputEnabled);
        Assert.Null(ring.OpenSigningKey("irrelevant")); // never parsed, never attempted
    }

    [Theory] // AC-03: a value that IS there but cannot work refuses at activation, typed
    [InlineData("isso-nao-e-base64!!")]
    [InlineData("aGVsbG8=")] // base64 of 5 bytes — well-formed text, not a root key
    public void A_configured_value_that_cannot_work_refuses_when_a_key_is_opened(string value)
    {
        var ring = RuntimeSigningKeyRing.FromConfiguration(value);

        Assert.False(ring.OutputEnabled); // the boot warning still fires: output is disabled
        var failure = Assert.Throws<FormatException>(() => ring.OpenSigningKey("a3JhcHBlZA=="));
        Assert.Contains("Squid:RecoveryKey", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, failure.Message, StringComparison.Ordinal); // never the value itself
    }

    [Fact] // AC-02: the configured secret opens exactly what it wrapped
    public void A_well_formed_secret_opens_its_own_wrapped_key_and_no_other()
    {
        var rootKey = RootKey.Create();
        using var rsa = RSA.Create(2048);
        var wrapped = Convert.ToBase64String(KeyDerivation.WrapRsaPrivateKey(rootKey, rsa));

        var ring = RuntimeSigningKeyRing.FromConfiguration(Convert.ToBase64String(rootKey.AsSpan()));

        Assert.True(ring.OutputEnabled);
        using var opened = ring.OpenSigningKey(wrapped);
        Assert.NotNull(opened);
        Assert.Equal(rsa.ExportRSAPrivateKey(), opened!.ExportRSAPrivateKey());

        // another well-formed secret: boots as enabled, fails as a typed crypto error at opening
        var other = RuntimeSigningKeyRing.FromConfiguration(Convert.ToBase64String(RootKey.Create().AsSpan()));
        Assert.True(other.OutputEnabled);
        Assert.ThrowsAny<CryptographicException>(() => other.OpenSigningKey(wrapped));
    }
}
