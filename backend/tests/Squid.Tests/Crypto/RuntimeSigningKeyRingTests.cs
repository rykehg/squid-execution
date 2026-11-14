using System.Security.Cryptography;
using Squid.Api.Crypto;

namespace Squid.Tests.Crypto;

/// <summary>
/// The loader behind TASK-0042 AC-01/AC-03 and the double wrap of TASK-0051: base64 of a 32-byte
/// <c>RK</c> in, ring out. Anything else never breaks the boot — it only decides whether an
/// instance copy opens, refuses loudly, or has nothing to open with. The store behind the port is
/// the in-memory double below, which is exactly how little the ring owes to storage.
/// </summary>
public sealed class RuntimeSigningKeyRingTests
{
    [Theory] // AC-01: absent means disabled, and disabled means nothing is even attempted
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_value_disables_the_ring_silently(string? value)
    {
        var copies = new FakeCopies();
        await copies.SaveAsync("ana_01", "a3JhcHBlZA=="); // a copy exists: no secret can ever open it
        var ring = RuntimeSigningKeyRing.FromConfiguration(value, copies);

        Assert.False(ring.OutputEnabled);
        Assert.Null(await ring.OpenInstanceCopyAsync("ana_01")); // never parsed, never attempted
        using var rsa = RSA.Create(2048);
        Assert.Null(ring.WrapInstanceCopy(rsa)); // and a registration births no copy either
    }

    [Theory] // AC-03: a value that IS there but cannot work refuses at activation, typed
    [InlineData("isso-nao-e-base64!!")]
    [InlineData("aGVsbG8=")] // base64 of 5 bytes — well-formed text, not a root key
    public async Task A_configured_value_that_cannot_work_refuses_when_a_key_is_opened(string value)
    {
        var copies = new FakeCopies();
        await copies.SaveAsync("ana_01", "a3JhcHBlZA==");
        var ring = RuntimeSigningKeyRing.FromConfiguration(value, copies);

        Assert.False(ring.OutputEnabled); // the boot warning still fires: output is disabled
        var failure = await Assert.ThrowsAsync<FormatException>(
            () => ring.OpenInstanceCopyAsync("ana_01"));
        Assert.Contains("Squid:RecoveryKey", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, failure.Message, StringComparison.Ordinal); // never the value itself
    }

    [Fact] // AC-02 + TASK-0051: the configured secret opens exactly its own copy, and only it
    public async Task A_well_formed_secret_opens_its_own_copy_and_no_other()
    {
        var rootKey = RootKey.Create();
        using var rsa = RSA.Create(2048);
        var copies = new FakeCopies();
        await copies.SaveAsync(
            "ana_01", Convert.ToBase64String(KeyDerivation.WrapRsaPrivateKey(rootKey, rsa)));

        var ring = RuntimeSigningKeyRing.FromConfiguration(
            Convert.ToBase64String(rootKey.AsSpan()), copies);

        Assert.True(ring.OutputEnabled);
        using var opened = await ring.OpenInstanceCopyAsync("ana_01");
        Assert.NotNull(opened);
        Assert.Equal(rsa.ExportRSAPrivateKey(), opened!.ExportRSAPrivateKey());

        // another well-formed secret: boots as enabled, fails as a typed crypto error at opening
        var other = RuntimeSigningKeyRing.FromConfiguration(
            Convert.ToBase64String(RootKey.Create().AsSpan()), copies);
        Assert.True(other.OutputEnabled);
        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => other.OpenInstanceCopyAsync("ana_01"));
    }

    [Fact] // TASK-0051 AC-05: configured, but this actor has no copy — activation is null, not a failure
    public async Task An_actor_without_an_instance_copy_activates_to_null()
    {
        var ring = RuntimeSigningKeyRing.FromConfiguration(
            Convert.ToBase64String(RootKey.Create().AsSpan()), new FakeCopies());

        Assert.Null(await ring.OpenInstanceCopyAsync("sem_copia"));
    }

    [Fact] // TASK-0051 AC-01: what registration wraps is the SAME RSA, sealed by the instance secret
    public void The_registration_copy_is_the_same_rsa_wrapped_by_the_instance_secret()
    {
        var rootKey = RootKey.Create();
        var ring = RuntimeSigningKeyRing.FromConfiguration(
            Convert.ToBase64String(rootKey.AsSpan()), new FakeCopies());
        using var rsa = RSA.Create(2048);

        var copy = ring.WrapInstanceCopy(rsa);

        Assert.NotNull(copy);
        using var opened = KeyDerivation.UnwrapRsaPrivateKey(rootKey, copy!);
        Assert.Equal(rsa.ExportRSAPrivateKey(), opened.ExportRSAPrivateKey());
    }

    /// <summary>The instance copies behind the port, in memory — rows of base64, nothing else.</summary>
    private sealed class FakeCopies : IInstanceSigningKeyStore
    {
        private readonly Dictionary<string, string> _rows = new(StringComparer.Ordinal);

        public Task SaveAsync(string handle, string wrappedSigningKey)
        {
            _rows[handle] = wrappedSigningKey;
            return Task.CompletedTask;
        }

        public Task<string?> FindAsync(string handle) =>
            Task.FromResult(_rows.TryGetValue(handle, out var wrapped) ? wrapped : null);
    }
}
