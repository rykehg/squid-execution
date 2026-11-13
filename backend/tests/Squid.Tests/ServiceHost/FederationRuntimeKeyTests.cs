using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Crypto;
using Squid.Grains.Actors;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using ActorStore = Squid.Tests.Actors.SqliteActorEventStore;

namespace Squid.Tests.ServiceHost;

/// <summary>
/// AC-01..AC-03 of TASK-0042 against the host <see cref="SquidServiceHost"/> builds (SPEC-0011
/// section B, GAP-09): no <c>Squid:RecoveryKey</c> boots and keeps serving local reads and writes
/// behind one Warning; a valid one lets the actor grain activate the wrapped RSA key in memory,
/// with row, routes and log all unable to show private material; a wrong one refuses at activation
/// as a typed error that leaks nothing. The class shares the ServiceHost collection because
/// localhost clustering fixes the silo ports, which is also what makes the console capture below
/// safe: every host test of the suite runs inside that same serialization.
/// </summary>
[Collection("ServiceHost")]
public sealed class FederationRuntimeKeyTests
{
    private const string DisabledWarning = "federation output disabled";

    [Fact] // AC-01
    public async Task Ac01_without_a_recovery_key_the_instance_boots_serves_local_traffic_and_warns()
    {
        var output = new CapturedOutput();
        var console = Console.Out;
        Console.SetOut(output); // the Serilog console sink writes to the current Console.Out
        try
        {
            await using var host = await StartAsync(); // no Squid:RecoveryKey anywhere

            // Local write and local read both keep serving — a missing key is never a fall.
            var client = new AuthClient(host.Http);
            var handle = PostRoutes.Handle("loc");
            using var registered = await client.RegisterAsync(handle, PostRoutes.Password);
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            using var profile = await client.SendAsync(HttpMethod.Get, $"/users/{handle}");
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/health")).StatusCode);

            // The warning that registers what exactly is disabled: only the federated output.
            var text = await WaitForOutputAsync(output,
                line => line.Contains(DisabledWarning, StringComparison.Ordinal));
            Assert.Contains(DisabledWarning, text, StringComparison.Ordinal);
            Assert.Contains("Squid:RecoveryKey", text, StringComparison.Ordinal);
            Assert.Contains("\"Level\":\"Warning\"", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    [Fact] // AC-02
    public async Task Ac02_with_a_valid_recovery_key_the_grain_activates_the_key_in_memory_only()
    {
        var rootKey = RootKey.Create();
        var keys = KeyState.Create(rootKey, PostRoutes.Password);
        var material = Material(keys);
        var secrets = Secrets(rootKey, keys);

        var output = new CapturedOutput();
        var console = Console.Out;
        Console.SetOut(output);
        try
        {
            await using var host = await StartAsync(
                "--Squid:RecoveryKey=" + Convert.ToBase64String(rootKey.AsSpan()));

            // The actor as registration would write it, wrapped by the very key configured at boot.
            var handle = PostRoutes.Handle("fed");
            var store = new ActorStore($"Data Source={host.StoragePath};Pooling=False");
            await new ActorGrain(store).CreateAsync(handle, "Federação", material);

            // Activation opens the wrapped key and keeps it behind a signer: the signature it
            // produces only verifies against the public key the profile publishes, so the unwrap
            // really happened — and the grain handed back no key, only a way to sign.
            var grain = host.App.Services.GetRequiredService<ActorGrain>();
            var signer = await grain.ActivateSigningKeyAsync(handle);
            Assert.NotNull(signer);

            var payload = Encoding.UTF8.GetBytes("assinatura com a chave ativada");
            var signature = signer!.Sign(payload);
            var state = await new ActorGrain(store).GetStateAsync(handle);
            var published = Assert.Single(state!.Keys.PublicKeys, key => key.KeyId == "#main-key");
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(published.PublicKey), out _);
            Assert.True(rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            Assert.Contains("masked", signer.ToString(), StringComparison.Ordinal);

            // The three channels of the AC: the row, the routes, the log.
            var row = await store.ReadPersistedJsonAsync(handle);
            Assert.NotNull(row);
            var profile = await GetAsync(host, $"/users/{handle}");
            Assert.Contains("BEGIN PUBLIC KEY", profile, StringComparison.Ordinal); // public material really is served
            var everything = row + profile + output.Snapshot();
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
            }

            // A usable key is not a disabled output: the boot warning never fired.
            Assert.DoesNotContain(DisabledWarning, output.Snapshot(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    [Fact] // AC-03
    public async Task Ac03_with_a_wrong_recovery_key_activation_refuses_without_leaking_anything()
    {
        var realKey = RootKey.Create();               // the key the actor was actually wrapped with
        var configured = RootKey.Create();            // the well-formed secret that is simply wrong
        var keys = KeyState.Create(realKey, PostRoutes.Password);
        var material = Material(keys);
        string[] secrets = [.. Secrets(realKey, keys), Convert.ToBase64String(configured.AsSpan())];

        var output = new CapturedOutput();
        var console = Console.Out;
        Console.SetOut(output);
        try
        {
            // Present and well-formed, so boot must not even notice — only activation can.
            await using var host = await StartAsync(
                "--Squid:RecoveryKey=" + Convert.ToBase64String(configured.AsSpan()));

            var handle = PostRoutes.Handle("bad");
            var store = new ActorStore($"Data Source={host.StoragePath};Pooling=False");
            await new ActorGrain(store).CreateAsync(handle, "Chave errada", material);
            var before = await store.ReadPersistedJsonAsync(handle);

            var grain = host.App.Services.GetRequiredService<ActorGrain>();
            var failure = await Assert.ThrowsAsync<SigningKeyActivationException>(
                () => grain.ActivateSigningKeyAsync(handle));

            // Auditável: the refusal names the actor and the cause — and only those.
            Assert.Equal(handle, failure.Handle);
            Assert.Contains(handle, failure.Message, StringComparison.Ordinal);
            Assert.Contains("Squid:RecoveryKey", failure.Message, StringComparison.Ordinal);

            // Silent: the message, the log and any answer carry no material, and the row stands
            // byte-identical — a refused activation writes nothing (SPEC-0011 section B).
            var profile = await GetAsync(host, $"/users/{handle}");
            var everything = failure.Message + (failure.InnerException?.Message ?? string.Empty) + profile + output.Snapshot();
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
            }

            Assert.Equal(before, await store.ReadPersistedJsonAsync(handle));

            // The instance never falls for a refused activation (FR-040).
            Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/health")).StatusCode);
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    /// <summary>The actor row exactly as registration writes it (AuthEndpoints), wrapped by the runtime key.</summary>
    private static ActorKeyMaterial Material(KeyState keys) =>
        new(
            [
                new ActorPublicKey("RSA-2048", "#main-key", Convert.ToBase64String(keys.RsaPublicKey)),
                new ActorPublicKey("Ed25519", "#ed25519", Convert.ToBase64String(keys.Ed25519PublicKey)),
            ],
            Convert.ToBase64String(keys.WrappedRsaPrivateKey));

    /// <summary>Every representation the private material could leak as, plus the runtime secret itself.</summary>
    private static string[] Secrets(RootKey rootKey, KeyState keys)
    {
        using var opened = KeyDerivation.UnwrapRsaPrivateKey(rootKey, keys.WrappedRsaPrivateKey);
        var pkcs8 = opened.ExportPkcs8PrivateKey();
        var pkcs1 = opened.ExportRSAPrivateKey();
        return
        [
            Convert.ToBase64String(pkcs8),
            Convert.ToBase64String(pkcs1),
            Convert.ToHexString(pkcs8),
            Convert.ToHexString(pkcs1),
            Convert.ToBase64String(rootKey.AsSpan()),
        ];
    }

    private static async Task<string> GetAsync(Host host, string path)
    {
        using var response = await host.Http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Waits until any line of the captured output matches, returning the whole output.</summary>
    private static async Task<string> WaitForOutputAsync(CapturedOutput output, Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var text = output.Snapshot();
            if (text.Split('\n').Any(line => predicate(line)) || predicate(text)) return text;
            await Task.Delay(100);
        }

        return output.Snapshot();
    }

    private static async Task<Host> StartAsync(params string[] extra)
    {
        var storage = Path.Combine(Path.GetTempPath(), $"squid-fedkey-{Guid.NewGuid():N}.db");
        var app = SquidServiceHost.Build(
            ["--Storage:Path=" + storage, "--Logging:LogLevel:Default=Warning", .. extra]);
        app.Urls.Add("http://127.0.0.1:0"); // dynamic port: tests never fight over a fixed one
        await app.StartAsync();

        return new Host(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }, storage);
    }

    /// <summary>
    /// Console capture a live host keeps logging into: every write and every read share one lock,
    /// so <see cref="Snapshot"/> cannot race a background log line the way a bare StringWriter would
    /// (a host logs from its own threads while the assertions below read the buffer).
    /// </summary>
    private sealed class CapturedOutput : TextWriter
    {
        private readonly object _gate = new();
        private readonly StringBuilder _buffer = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Snapshot()
        {
            lock (_gate) return _buffer.ToString();
        }

        public override void Write(char value)
        {
            lock (_gate) _buffer.Append(value);
        }

        public override void Write(string? value)
        {
            lock (_gate) _buffer.Append(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_gate) _buffer.Append(buffer, index, count);
        }
    }

    /// <summary>One host under test, on a dynamic port, with its own temp storage file.</summary>
    private sealed record Host(WebApplication App, HttpClient Http, string StoragePath) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            try
            {
                File.Delete(StoragePath);
            }
            catch (IOException)
            {
                // a leftover temp file is not a product failure; the OS cleans it up
            }
        }
    }
}
