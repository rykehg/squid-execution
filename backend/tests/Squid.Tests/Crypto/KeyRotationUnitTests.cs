using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Squid.Api.Admin;
using Squid.Api.Crypto;

namespace Squid.Tests.Crypto;

/// <summary>
/// The two new primitives of TASK-0058's rotation at unit level: the keyring box that keeps a
/// retired epoch's `RK` wrapped under the current one (SPEC-0012 section E), and the keyring
/// configuration the rotation response hands the operator to paste — a value that degrades to
/// fewer openable epochs, never to a boot failure.
/// </summary>
public sealed class KeyRotationUnitTests
{
    [Fact]
    public void The_kit_keyring_box_opens_only_with_the_wrapping_key()
    {
        var current = RootKey.Create();
        var epochKey = RootKey.Create().AsSpan().ToArray();

        var wrapped = KeyDerivation.WrapKitKey(current, epochKey);

        // Its own label: the box is not the backup one wearing the same bytes.
        Assert.Equal("fed/kit-keyring", KeyDerivation.KitKeyringInfo);
        Assert.Equal(epochKey, KeyDerivation.OpenKitKey(current, wrapped));

        var denied = Assert.ThrowsAny<CryptographicException>(
            () => KeyDerivation.OpenKitKey(RootKey.Create(), wrapped));
        Assert.DoesNotContain(Convert.ToBase64String(epochKey), denied.Message);
    }

    [Fact]
    public void The_configured_keyring_reads_epoch_pairs_and_skips_what_it_cannot_use()
    {
        var first = Convert.ToBase64String(RootKey.Create().AsSpan());
        var second = Convert.ToBase64String(RootKey.Create().AsSpan());
        var source = new ConfiguredBackupKeySource(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Squid:RecoveryKeyRing"] =
                    $"1:{first}, lixo, 2:{second}, 2:{first}, zero:base64invalida, 0:{first}",
            })
            .Build());

        var ring = source.ResolveRing();

        Assert.Equal(2, ring.Count); // the duplicate epoch and the junk entries are skipped
        Assert.Equal(first, Convert.ToBase64String(ring[1].AsSpan()));
        Assert.Equal(second, Convert.ToBase64String(ring[2].AsSpan()));

        // A single-key setup (or an absent one) knows no ring at all: restore keeps the ledger
        // fallback it always had, and an empty value never throws.
        Assert.Empty(new ConfiguredBackupKeySource(new ConfigurationBuilder().Build()).ResolveRing());
        Assert.Empty(new ConfiguredBackupKeySource(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Squid:RecoveryKeyRing"] = "  ",
            })
            .Build()).ResolveRing());
    }

    [Fact]
    public void The_single_key_configuration_still_resolves_and_the_rotation_swap_wins()
    {
        var current = Convert.ToBase64String(RootKey.Create().AsSpan());
        var source = new ConfiguredBackupKeySource(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Squid:RecoveryKey"] = current,
            })
            .Build());

        var resolved = source.Resolve();
        Assert.NotNull(resolved);
        Assert.Empty(source.ResolveRing());

        // What a rotation installs beats configuration until the operator reloads it — that is
        // what keeps backups and signing on the new epoch in the same process (AC-05).
        var replacement = RootKey.Create();
        var ring = new Dictionary<int, RootKey> { [1] = resolved!, [2] = replacement };
        source.RotateTo(replacement, ring);

        Assert.Equal(replacement.AsSpan().ToArray(), source.Resolve()!.AsSpan().ToArray());
        Assert.Equal(2, source.ResolveRing().Count);
        Assert.Equal(replacement.AsSpan().ToArray(), source.ResolveRing()[2].AsSpan().ToArray());
    }
}
