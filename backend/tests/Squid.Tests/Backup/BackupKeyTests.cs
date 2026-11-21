using System.Security.Cryptography;
using Squid.Api.Crypto;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-05 of TASK-0054 at the primitive: the per-backup `DK` is wrapped by
/// <c>HKDF(RK, "fed/backup-kek")</c> — only the same RK opens it, the wrapped box carries no raw
/// key byte (AC-03's disk scan cannot find the DK either), and a wrong RK fails as the typed AEAD
/// error without leaking the material.
/// </summary>
public sealed class BackupKeyTests
{
    [Fact]
    public void Ac05_The_wrapped_data_key_opens_only_with_the_same_rk()
    {
        var rk = RootKey.Create();
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var wrapped = KeyDerivation.WrapBackupKey(rk, dataKey);

        // The label is the contract of SPEC-0012 section A; a different label is a different key.
        Assert.Equal("fed/backup-kek", KeyDerivation.BackupKekInfo);

        // The box carries no raw DK byte for a destination scan to find.
        Assert.True(wrapped.AsSpan().IndexOf(dataKey) < 0, "raw DK bytes inside the wrapped box");

        Assert.Equal(dataKey, KeyDerivation.UnwrapBackupKey(rk, wrapped));

        var denied = Assert.ThrowsAny<CryptographicException>(
            () => KeyDerivation.UnwrapBackupKey(RootKey.Create(), wrapped));
        Assert.DoesNotContain(Convert.ToBase64String(dataKey), denied.Message);
        Assert.DoesNotContain(Convert.ToHexString(dataKey), denied.Message);
    }

    [Fact]
    public void Ac05_Wrapping_the_same_dk_under_another_rk_yields_a_box_only_that_rk_opens()
    {
        var rk = RootKey.Create();
        var other = RootKey.Create();
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var wrapped = KeyDerivation.WrapBackupKey(rk, dataKey);

        Assert.ThrowsAny<CryptographicException>(() => KeyDerivation.UnwrapBackupKey(other, wrapped));
        Assert.Equal(dataKey, KeyDerivation.UnwrapBackupKey(rk, wrapped));
    }
}
