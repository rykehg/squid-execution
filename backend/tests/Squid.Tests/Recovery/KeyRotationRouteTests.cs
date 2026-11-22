using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Admin;
using Squid.Api.Crypto;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Backup;
using Squid.Tests.Posts;
using Squid.Tests.Projections;

namespace Squid.Tests.Recovery;

/// <summary>
/// AC-01, AC-04 and AC-06 of TASK-0058 over HTTP against the host <see cref="SquidServiceHost"/>
/// builds: the rotation behind the P-03 operator gate with the ledger it must leave behind
/// (previous epoch retired, fingerprint recorded), the credential that stops verifying the moment
/// a later rotation replaces it while the reissued kit still restores the root key the instance
/// signs with, and the one-shot rule — the kit reaches this response and no log line, no other
/// route and no second answer ever carries its material.
/// </summary>
[Collection("ServiceHost")]
public sealed class KeyRotationRouteTests
{
    private const string Password = PostRoutes.Password;

    [Fact] // AC-01
    public async Task Ac01_rotate_is_operator_only_and_retires_the_previous_epoch()
    {
        var operatorHandle = PostRoutes.Handle("rotoper");
        var strangerHandle = PostRoutes.Handle("rotstran");
        var storage = NewDb();
        var volume = NewVolume();
        try
        {
            await using var host = await StartAsync(storage, volume, operatorHandle);

            // The gate of every /api/admin route: no session is 401, a session outside
            // Instance:Operators is 403 policy_denied — before any key is even looked at.
            var anonymous = host.NewClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(
                HttpMethod.Post, "/api/admin/recovery/rotate")).StatusCode);

            var stranger = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await stranger.RegisterAsync(strangerHandle, Password)).StatusCode);
            var refused = await PostRoutes.SendAsync(
                stranger, HttpMethod.Post, "/api/admin/recovery/rotate", null, stranger.Csrf);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("policy_denied", await refused.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            // The operator rotates: keyEpoch++ and the previous epoch carries RetiredAt
            // (data-model 2.10), with a fingerprint that names the material without exposing it.
            var operatorClient = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await operatorClient.RegisterAsync(operatorHandle, Password)).StatusCode);
            var rotate = await PostRoutes.SendAsync(
                operatorClient, HttpMethod.Post, "/api/admin/recovery/rotate", null,
                operatorClient.Csrf);
            Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);

            using var kit = JsonDocument.Parse(await rotate.Content.ReadAsStringAsync());
            Assert.Equal(2, kit.RootElement.GetProperty("keyEpoch").GetInt32());

            var epochs = await ProjectionDb.QueryAsync<EpochRow>(storage,
                "SELECT epoch AS Epoch, public_fingerprint AS PublicFingerprint, "
                + "retired_at AS RetiredAt FROM key_epoch ORDER BY epoch");
            Assert.Equal(2, epochs.Count);
            Assert.NotNull(epochs[0].RetiredAt);
            Assert.StartsWith("sha256:", epochs[0].PublicFingerprint, StringComparison.Ordinal);
            Assert.Null(epochs[1].RetiredAt);
            Assert.Equal(2, epochs[1].Epoch);
            Assert.NotEqual(epochs[0].PublicFingerprint, epochs[1].PublicFingerprint);

            // And the ledger is what the next backup stamps from: the envelope of a run after the
            // rotation carries the new epoch (SPEC-0012 section A reads it here).
            var run = await PostRoutes.SendAsync(
                operatorClient, HttpMethod.Post, "/api/admin/backup/run", null, operatorClient.Csrf);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
            Assert.Equal(2, await ProjectionDb.ScalarAsync<long>(storage,
                "SELECT MAX(key_epoch) FROM backup_envelope"));
        }
        finally
        {
            Cleanup(storage, volume);
        }
    }

    [Fact] // AC-04 (with AC-05's signing half)
    public async Task Ac04_the_previous_credential_stops_verifying_and_the_reissued_kit_restores_the_root_key()
    {
        var operatorHandle = PostRoutes.Handle("rotkit");
        var storage = NewDb();
        var volume = NewVolume();
        try
        {
            await using var host = await StartAsync(storage, volume, operatorHandle);
            var operatorClient = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await operatorClient.RegisterAsync(operatorHandle, Password)).StatusCode);

            // First rotation: the fresh credential verifies, and the account-kit half of the route
            // (FR-007, no body) answers exactly as contracts/http-api.md §3 says it always did.
            var first = await RotateAsync(operatorClient);
            Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(
                operatorClient, first.GetProperty("recoveryCredential").GetString()!)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PostRoutes.SendAsync(
                operatorClient, HttpMethod.Post, "/api/recovery/kit/verify", null,
                operatorClient.Csrf)).StatusCode);

            // Second rotation: the credential of the previous kit stops verifying (401, the one
            // typed refusal) while the reissued one passes — AC-04's "RC antiga deixa de verificar".
            var second = await RotateAsync(operatorClient);
            var oldCredential = first.GetProperty("recoveryCredential").GetString()!;
            var currentCredential = second.GetProperty("recoveryCredential").GetString()!;
            Assert.NotEqual(oldCredential, currentCredential);
            Assert.Equal(HttpStatusCode.Unauthorized, (await VerifyAsync(
                operatorClient, oldCredential)).StatusCode);
            var accepted = await VerifyAsync(operatorClient, currentCredential);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

            // "restaura a RK em teste" (FR-006 with the new material): opening the reissued kit
            // with its credential yields the very key the instance now signs with, and that key
            // opens the 0009 copy of this operator — re-wrapped by both rotations — into an RSA
            // that signs a challenge its own public half verifies (outbound signing, AC-05).
            var sealedRootKey = Convert.FromBase64String(
                second.GetProperty("encryptedRootKey").GetString()!);
            var restored = RootKeyCipher.Decrypt(sealedRootKey, currentCredential);

            var copies = host.App.Services.GetRequiredService<IInstanceSigningKeyStore>();
            var row = await copies.FindAsync(operatorHandle);
            Assert.NotNull(row);
            using var signing = KeyDerivation.UnwrapRsaPrivateKey(
                restored, Convert.FromBase64String(row!));
            var challenge = RandomNumberGenerator.GetBytes(32);
            var signature = signing.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.True(signing.VerifyData(
                challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            // The kit's key IS the instance's current key: the same bytes the backup chain wraps
            // with after the swap (ConfiguredBackupKeySource.Resolve), never a foreign material.
            var current = host.App.Services.GetRequiredService<IBackupKeySource>().Resolve();
            Assert.NotNull(current);
            Assert.Equal(current!.AsSpan().ToArray(), restored.AsSpan().ToArray());

            // The ring swapped in place: output stays enabled with no restart (AC-05).
            Assert.True(host.App.Services.GetRequiredService<RuntimeSigningKeyRing>().OutputEnabled);
        }
        finally
        {
            Cleanup(storage, volume);
        }
    }

    [Fact] // AC-06
    public async Task Ac06_the_reissued_kit_is_answered_once_and_reaches_no_log_route_or_second_answer()
    {
        var operatorHandle = PostRoutes.Handle("rotonce");
        var storage = NewDb();
        var volume = NewVolume();
        var tap = new LockedWriter();
        var console = Console.Out;
        Console.SetOut(tap); // the Serilog console sink writes to the current Console.Out
        try
        {
            await using var host = await StartAsync(storage, volume, operatorHandle);
            var operatorClient = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await operatorClient.RegisterAsync(operatorHandle, Password)).StatusCode);

            var first = await RotateAsync(operatorClient);
            var seeds = MaterialOf(first);

            // No second answer carries the kit: a later rotation issues DIFFERENT current material
            // (its keyring re-serves the epochs by SPEC-0012 section E — that is the config
            // delivery R-20 needs, not this kit re-issued), and the previous credential is dead.
            var second = await RotateAsync(operatorClient);
            Assert.NotEqual(seeds.Credential, second.GetProperty("recoveryCredential").GetString());
            Assert.NotEqual(seeds.SealedRootKey, second.GetProperty("encryptedRootKey").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await VerifyAsync(
                operatorClient, seeds.Credential)).StatusCode);

            // No later route carries it either: the account kit, the session view and the backup
            // status are all scanned for every form the material takes on the wire.
            var routes = new[]
            {
                await PostRoutes.SendAsync(operatorClient, HttpMethod.Post, "/api/recovery/kit"),
                await PostRoutes.SendAsync(operatorClient, HttpMethod.Get, "/api/auth/me"),
                await PostRoutes.SendAsync(operatorClient, HttpMethod.Get, "/api/admin/backup/status"),
            };
            foreach (var route in routes)
            {
                var body = await route.Content.ReadAsStringAsync();
                Assert.True(body.Length > 0, "uma varredura sobre corpo vazio não provaria nada");
                AssertAbsent(body, seeds.All);
            }

            // And no log line: boot, rotation and every refusal above were captured from the very
            // first write of the host — and the capture has to be non-empty, or the absence proof
            // below would pass vacuously against nothing.
            var logged = tap.Snapshot();
            Assert.Contains("instance key rotated", logged, StringComparison.Ordinal);
            AssertAbsent(logged, seeds.All);
        }
        finally
        {
            Console.SetOut(console);
            Cleanup(storage, volume);
        }
    }

    /// <summary>The rotation call every fact makes, parsed.</summary>
    private static async Task<JsonElement> RotateAsync(AuthClient client)
    {
        var response = await PostRoutes.SendAsync(
            client, HttpMethod.Post, "/api/admin/recovery/rotate", null, client.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>The instance-credential half of <c>POST /api/recovery/kit/verify</c>.</summary>
    private static Task<HttpResponseMessage> VerifyAsync(AuthClient client, string credential) =>
        PostRoutes.SendAsync(
            client, HttpMethod.Post, "/api/recovery/kit/verify", new { recoveryCredential = credential },
            client.Csrf);

    /// <summary>Every form the kit's material takes on the wire — the seeds of the absence proofs.</summary>
    private static (string Credential, string SealedRootKey, string[] All) MaterialOf(JsonElement kit)
    {
        var credential = kit.GetProperty("recoveryCredential").GetString()!;
        var sealedRootKey = kit.GetProperty("encryptedRootKey").GetString()!;
        var words = kit.GetProperty("mnemonic").EnumerateArray()
            .Select(word => word.GetString()!).ToArray();
        var keys = kit.GetProperty("keyring").EnumerateArray()
            .Select(entry => entry.GetProperty("key").GetString()!).ToArray();
        return (credential, sealedRootKey,
        [
            credential,
            sealedRootKey,
            .. keys,
            // The mnemonic in every JSON shape it can ride in — joined and as array leaves.
            string.Join(' ', words),
            string.Join("\",\"", words),
            string.Join("\", \"", words),
        ]);
    }

    /// <summary>
    /// Fails when any seed appears in the text — raw and with the JSON <c>\uXXXX</c> leaves
    /// decoded (S-25: a base64 `+` travels both ways, so a raw scan alone can miss it).
    /// </summary>
    private static void AssertAbsent(string text, IEnumerable<string> seeds)
    {
        var decoded = Regex.Replace(text, @"\\u([0-9a-fA-F]{4})",
            static match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
        foreach (var seed in seeds.Where(seed => !string.IsNullOrEmpty(seed)))
        {
            Assert.DoesNotContain(seed, text, StringComparison.Ordinal);
            Assert.DoesNotContain(seed, decoded, StringComparison.Ordinal);
        }
    }

    private static async Task<BackupHost> StartAsync(
        string storage, string volume, string operatorHandle)
    {
        var recoveryKey = Convert.ToBase64String(RootKey.Create().AsSpan());
        return await BackupHost.StartAsync(
        [
            "--Storage:Path=" + storage,
            "--Logging:LogLevel:Default=Warning",
            "--Instance:Operators=" + operatorHandle,
            "--Backup:Local:Path=" + volume,
            "--Backup:IntervalMinutes=0",
            "--Squid:RecoveryKey=" + recoveryKey,
        ]);
    }

    /// <summary>One <c>key_epoch</c> row as Dapper binds it (SQLite INTEGER lands as long — the house rule).</summary>
    private sealed record EpochRow(long Epoch, string PublicFingerprint, string RetiredAt);

    /// <summary>Console capture a live host keeps logging into: writes and reads share one lock.</summary>
    private sealed class LockedWriter : TextWriter
    {
        private readonly StringBuilder _buffer = new();
        private readonly object _gate = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_gate)
            {
                _buffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_gate)
            {
                _buffer.Append(value);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_gate)
            {
                _buffer.Append(buffer, index, count);
            }
        }

        public string Snapshot()
        {
            lock (_gate)
            {
                return _buffer.ToString();
            }
        }
    }

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"squid-rotate-route-{Guid.NewGuid():N}.db");

    private static string NewVolume() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"squid-rotate-route-{Guid.NewGuid():N}")).FullName;

    private static void Cleanup(string storage, string volume)
    {
        ProjectionDb.TryDelete(storage);
        try
        {
            Directory.Delete(volume, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a product failure; the OS cleans it up.
        }
    }
}
