using System.Security.Cryptography;
using System.Text;
using Squid.Grains.Actors;

namespace Squid.Tests.Actors;

/// <summary>
/// Activation of the runtime signing key (SPEC-0011 section B, TASK-0051): ONE grain instance
/// serves more than one handle, so every activation is keyed by handle — the latch parked by
/// TASK-0046 (a long-lived instance signing the second actor with the first one's key) must be
/// impossible; no copy answers null (output disabled, never a failure), and a refusal arrives
/// typed, naming the actor and never any material.
/// </summary>
public sealed class ActorSigningKeyActivationTests : IDisposable
{
    private readonly string _database =
        Path.Combine(Path.GetTempPath(), $"squid-actkey-{Guid.NewGuid():N}.db");

    private readonly SqliteActorEventStore _store;

    public ActorSigningKeyActivationTests() =>
        _store = new SqliteActorEventStore($"Data Source={_database};Pooling=False");

    /// <summary>No temp file survives its test.</summary>
    public void Dispose()
    {
        if (File.Exists(_database)) File.Delete(_database);
    }

    [Fact] // the parked latch of TASK-0046: one instance, two actors, TWO keys
    public async Task One_instance_activates_each_handle_with_its_own_key()
    {
        var ring = new FakeRing();
        var grain = new ActorGrain(_store, ring);
        await grain.CreateAsync("alice_01", "Alice", ActorTestData.Material());
        await grain.CreateAsync("bob_01", "Bob", ActorTestData.Material());
        using var aliceKey = RSA.Create(2048);
        using var bobKey = RSA.Create(2048);
        ring.Keys["alice_01"] = aliceKey;
        ring.Keys["bob_01"] = bobKey;

        var first = await grain.ActivateSigningKeyAsync("alice_01");
        var second = await grain.ActivateSigningKeyAsync("bob_01"); // same instance: must NOT reuse alice's key

        var payload = Encoding.UTF8.GetBytes("quem assina é o dono da chave");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(aliceKey.VerifyData(payload, first!.Sign(payload),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.True(bobKey.VerifyData(payload, second!.Sign(payload),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        // the latch itself, as a red: alice's key must refuse bob's signature
        Assert.False(aliceKey.VerifyData(payload, second.Sign(payload),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact] // no instance copy → the output is disabled, never a failure (AC-05)
    public async Task Without_a_copy_the_activation_is_null_not_a_failure()
    {
        var ring = new FakeRing(); // no key registered for this handle
        var grain = new ActorGrain(_store, ring);
        await grain.CreateAsync("carol_01", "Carol", ActorTestData.Material());

        Assert.Null(await grain.ActivateSigningKeyAsync("carol_01"));
        // and without a ring at all — no runtime secret — the same null, as before this task
        Assert.Null(await new ActorGrain(_store).ActivateSigningKeyAsync("carol_01"));
    }

    [Fact] // a refusal is typed, auditable, and carries no material (TASK-0042 AC-03 kept)
    public async Task A_refusal_arrives_typed_and_names_the_actor_only()
    {
        var ring = new FakeRing { Failure = new FormatException("configured secret over a foreign wrap") };
        var grain = new ActorGrain(_store, ring);
        await grain.CreateAsync("dave_01", "Dave", ActorTestData.Material());

        var failure = await Assert.ThrowsAsync<SigningKeyActivationException>(
            () => grain.ActivateSigningKeyAsync("dave_01"));

        Assert.Equal("dave_01", failure.Handle);
        Assert.Contains("dave_01", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Squid:RecoveryKey", failure.Message, StringComparison.Ordinal);
        Assert.NotNull(failure.InnerException); // the typed cause stays with it for the audit
        Assert.Equal(ring.Failure, failure.InnerException);
    }

    [Fact] // an actor that does not exist is still a 404, never a half-activated signer
    public async Task An_unknown_handle_stays_not_found()
    {
        var grain = new ActorGrain(_store, new FakeRing());

        await Assert.ThrowsAsync<ActorNotFoundException>(
            () => grain.ActivateSigningKeyAsync("ghost_01"));
    }

    /// <summary>The port with the answers a test chooses: one key per handle, or a typed refusal.</summary>
    private sealed class FakeRing : IActorSigningKeyRing
    {
        public Dictionary<string, RSA> Keys { get; } = new(StringComparer.Ordinal);

        public Exception? Failure { get; init; }

        public Task<RSA?> OpenInstanceCopyAsync(string handle)
        {
            if (Failure is not null)
            {
                return Task.FromException<RSA?>(Failure);
            }

            return Task.FromResult(Keys.TryGetValue(handle, out var key) ? key : null);
        }
    }
}
