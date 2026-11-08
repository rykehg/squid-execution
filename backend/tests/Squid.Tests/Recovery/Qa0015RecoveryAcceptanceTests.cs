using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Orleans;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Recovery;

/// <summary>
/// QA-0015 (TASK-0016): QA's own four scenarios against real HTTP on <see cref="SquidServiceHost"/>
/// — the kit once with the exact contract shape, the FR-007 notice in all four of its states, the
/// clean-instance round-trip whose webfinger resolves the SAME handle with the SAME keys, and the
/// wrong password refused as one typed 401 while every row keeps its exact bytes. Expectations are
/// derived from the artifacts (the credential next to the words, the origin profile next to the
/// restored one), never from a label remembered elsewhere. Every host is created and stopped
/// inside its own test: localhost clustering fixes the silo ports, and the shared
/// <c>ServiceHost</c> collection keeps the other host-starting classes from overlapping them.
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0015RecoveryAcceptanceTests
{
    private const string Password = "senha-forte-1234";
    private const string WrongPassword = "senha-diferente-99";

    [Fact] // AC-01
    public async Task Ac01_the_kit_is_201_with_exactly_five_fields_24_words_once_and_no_read_route_recovers_it()
    {
        var handle = Handle("qa01");
        var host = new AuthHostFixture();
        try
        {
            var owner = host.NewClient();
            var registered = await owner.RegisterAsync(handle, Password);
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            var registerBody = await registered.Content.ReadAsStringAsync();

            // The route is authenticated: no session answers the typed 401, a session without
            // the double submission token answers 403 (B-06) — both before any kit exists.
            var anonymous = new AuthClient(host.Http);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await anonymous.PostAsync("/api/recovery/kit")).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await owner.PostAsync("/api/recovery/kit")).StatusCode);

            var response = await WithCsrf(owner, "/api/recovery/kit");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();

            // The exact field set — contract §3's four plus wrappedSigningKey (data-model §2.12) —
            // and nothing else: one extra property would be an accidental new channel.
            using var json = JsonDocument.Parse(body);
            var kit = json.RootElement;
            Assert.Equal(
                new[] { "createdAt", "encryptedRootKey", "mnemonic", "recoveryCredential", "wrappedSigningKey" },
                kit.EnumerateObject().Select(property => property.Name)
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray());

            var credential = kit.GetProperty("recoveryCredential").GetString();
            Assert.StartsWith("RC-", credential, StringComparison.Ordinal);
            var words = kit.GetProperty("mnemonic").EnumerateArray()
                .Select(word => word.GetString()!).ToArray();
            Assert.Equal(24, words.Length);                    // the AC's own number
            Assert.Equal(Mnemonic.WordCount, words.Length);    // and the constant registration issued
            Assert.All(words, word => Assert.False(string.IsNullOrWhiteSpace(word)));

            // Probe against the artifact: the words next to the credential ARE this kit's words.
            var recoveryCode = ParseRecoveryCredential(credential!);
            Assert.Equal(Mnemonic.FromRecoveryCode(recoveryCode), words);

            var encrypted = kit.GetProperty("encryptedRootKey").GetString();
            Assert.False(string.IsNullOrEmpty(encrypted));
            Assert.False(string.IsNullOrEmpty(kit.GetProperty("wrappedSigningKey").GetString()));
            Assert.True(DateTimeOffset.TryParse(kit.GetProperty("createdAt").GetString(), out _));
            Assert.Equal(1, Occurrences(body, "\"mnemonic\"")); // shown exactly once

            // The clear RK, opened here with the password: it appears in no form of this very
            // response — raw, \u-decoded and JSON leaves (S-25) — nor does the password.
            var rootKey = RootKeyCipher.Decrypt(Convert.FromBase64String(encrypted!), Password);
            var rkForms = RkForms(rootKey);
            AssertAbsent(body, [.. rkForms, Password]);

            // No read route recovers the kit: the three recovery paths answer 405 (POST only) and
            // me/health/profile/actors/webfinger carry none of it in any of the three views.
            var readRoutes = new[]
            {
                await owner.SendAsync(HttpMethod.Get, "/api/recovery/kit"),
                await owner.SendAsync(HttpMethod.Get, "/api/recovery/kit/verify"),
                await owner.SendAsync(HttpMethod.Get, "/api/recovery/import"),
                await owner.SendAsync(HttpMethod.Get, "/api/auth/me"),
                await host.Http.GetAsync("/health"),
                await host.Http.GetAsync($"/users/{handle}"),
                await host.Http.GetAsync($"/api/actors/{handle}"),
                await host.Http.GetAsync(WebFinger(host.Http, handle)),
            };
            Assert.Equal(HttpStatusCode.MethodNotAllowed, readRoutes[0].StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, readRoutes[1].StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, readRoutes[2].StatusCode);

            var secrets = new List<string>(rkForms)
            {
                Password,
                Convert.ToBase64String(recoveryCode),
                credential!,
                string.Join(' ', words),
                string.Join("\",\"", words),
                string.Join("\", \"", words),
                "\"mnemonic\"",
            };
            foreach (var read in readRoutes)
            {
                AssertAbsent(await read.Content.ReadAsStringAsync(), secrets.ToArray());
            }

            // The register response (which carries its own kit) never sees the clear RK either.
            AssertAbsent(registerBody, [.. rkForms, Password]);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact] // AC-02
    public async Task Ac02_me_switches_between_unverified_both_stale_and_silent_in_every_state()
    {
        var handle = Handle("qa02");
        var host = new AuthHostFixture();
        try
        {
            var owner = host.NewClient();
            Assert.Equal(HttpStatusCode.Created, (await owner.RegisterAsync(handle, Password)).StatusCode);

            // Contract §2: pending notices belong to the authenticated actor only.
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await new AuthClient(host.Http).SendAsync(HttpMethod.Get, "/api/auth/me")).StatusCode);

            // State 1 — fresh and unproven: the persistent notice (FR-007, first half).
            using (var pending = await MeAsync(owner))
            {
                Assert.Equal(handle, pending.RootElement.GetProperty("handle").GetString());
                Assert.Equal(new[] { "kit_unverified" }, Codes(pending));
            }

            // State 2 — 13 months old and still unproven: both notices, in report order.
            await SetKitCreatedAtAsync(host.StoragePath, handle, DateTimeOffset.UtcNow.AddMonths(-13));
            using (var aged = await MeAsync(owner))
            {
                Assert.Equal(new[] { "kit_unverified", "kit_stale" }, Codes(aged));
            }

            // The proof is a mutation too: 403 without the token, then recorded (B-13).
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await owner.PostAsync("/api/recovery/kit/verify")).StatusCode);
            var verified = await WithCsrf(owner, "/api/recovery/kit/verify");
            Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
            using var verifiedJson = JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
            Assert.Equal(handle, verifiedJson.RootElement.GetProperty("handle").GetString());
            Assert.True(DateTimeOffset.TryParse(
                verifiedJson.RootElement.GetProperty("verifiedAt").GetString(), out _));

            // State 3 — proven but old: verifying silenced only the unverified half.
            using (var stale = await MeAsync(owner))
            {
                Assert.Equal(new[] { "kit_stale" }, Codes(stale));
            }

            // State 4 — young again: fully silent, so the second half really is the age.
            await SetKitCreatedAtAsync(host.StoragePath, handle, DateTimeOffset.UtcNow);
            using (var silent = await MeAsync(owner))
            {
                Assert.Empty(Codes(silent));
            }
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact] // AC-03
    public async Task Ac03_a_downloaded_kit_restores_the_identity_and_webfinger_resolves_the_same_keys()
    {
        var handle = Handle("qa03");

        // The download half: register → export → stop (localhost clustering fixes the silo
        // ports, so the two instances below never overlap).
        var (kit, originPem, originMultibase) = await ExportWithOriginAsync(handle);

        var clean = new AuthHostFixture();
        try
        {
            // The premise of a clean instance, proved before the import runs: no actor here.
            Assert.Equal(HttpStatusCode.NotFound, (await clean.Http.GetAsync($"/users/{handle}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await clean.Http.GetAsync(WebFinger(clean.Http, handle))).StatusCode);

            // The import is the pre-login path: no session rides along (the AC-04 rule).
            var stranger = new AuthClient(clean.Http);
            Assert.Null(stranger.Session);

            var import = await stranger.PostAsync("/api/recovery/import", new
            {
                handle,
                password = Password,
                recoveryCredential = kit.Credential,
                encryptedRootKey = kit.EncryptedRootKey,
                wrappedSigningKey = kit.WrappedSigningKey,
                createdAt = kit.CreatedAt,
            });
            Assert.Equal(HttpStatusCode.Created, import.StatusCode);
            var importBody = await import.Content.ReadAsStringAsync();
            using var importJson = JsonDocument.Parse(importBody);
            Assert.Equal(handle, importJson.RootElement.GetProperty("handle").GetString());

            // The DoD chain in one breath: webfinger on the restored instance resolves the same
            // handle, and the profile behind the self link carries the SAME RSA and the SAME
            // Ed25519 the origin published (string equality, read by QA's own JSON walk).
            var restored = await ResolvedAsync(clean.Http, handle);
            Assert.Equal(originPem, restored.Pem);
            Assert.Equal(originMultibase, restored.Multibase);

            // The kit rehydrated the RK (data-model §2.12): it opens with the password and
            // re-derives the very Ed25519 both profiles publish...
            var rootKey = RootKeyCipher.Decrypt(Convert.FromBase64String(kit.EncryptedRootKey), Password);
            Assert.Equal(
                Convert.ToBase64String(DecodeEd25519Multibase(restored.Multibase)),
                Convert.ToBase64String(KeyDerivation.DeriveEd25519PublicKey(rootKey)));

            // ...and both restored private keys sign a challenge the published keys verify.
            AssertRsaSignature(rootKey, kit.WrappedSigningKey, restored.Pem, handle);
            AssertEd25519Signature(rootKey, restored.Multibase, handle);

            // The success response carries no clear RK either.
            var rkForms = RkForms(rootKey);
            AssertAbsent(importBody, [.. rkForms, Password]);
        }
        finally
        {
            await clean.DisposeAsync();
        }
    }

    [Fact] // AC-04
    public async Task Ac04_a_wrong_password_is_one_typed_401_and_keeps_every_row_byte_identical()
    {
        var handle = Handle("qa04");
        var host = new AuthHostFixture();
        try
        {
            var owner = host.NewClient();
            Assert.Equal(HttpStatusCode.Created, (await owner.RegisterAsync(handle, Password)).StatusCode);
            var exported = await WithCsrf(owner, "/api/recovery/kit");
            Assert.Equal(HttpStatusCode.Created, exported.StatusCode);
            using var kitJson = JsonDocument.Parse(await exported.Content.ReadAsStringAsync());
            var kit = kitJson.RootElement;

            var before = await StateHashAsync(host.StoragePath);

            // The attempt runs WITHOUT a session on purpose: an authenticated request would slide
            // the session expiry by B-05 and rewrite the very rows this hash compares.
            var attempt = new AuthClient(host.Http);
            Assert.Null(attempt.Session);
            var denied = await attempt.PostAsync("/api/recovery/import", new
            {
                handle,
                password = WrongPassword,
                recoveryCredential = kit.GetProperty("recoveryCredential").GetString(),
                encryptedRootKey = kit.GetProperty("encryptedRootKey").GetString(),
                wrappedSigningKey = kit.GetProperty("wrappedSigningKey").GetString(),
                createdAt = kit.GetProperty("createdAt").GetString(),
            });

            // One typed refusal — 401 recovery_failed, once, and it leaks nothing.
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            var deniedBody = await denied.Content.ReadAsStringAsync();
            using var error = JsonDocument.Parse(deniedBody);
            var envelope = error.RootElement.GetProperty("error");
            Assert.Equal("recovery_failed", envelope.GetProperty("code").GetString());
            Assert.False(string.IsNullOrEmpty(envelope.GetProperty("message").GetString()));
            Assert.Equal(1, Occurrences(deniedBody, "recovery_failed"));

            // Nothing destroyed, byte for byte: every row of every store keeps its exact content.
            Assert.Equal(before, await StateHashAsync(host.StoragePath));

            // ...and the refusal carried out no kit material, clear or sealed-open.
            var rootKey = RootKeyCipher.Decrypt(
                Convert.FromBase64String(kit.GetProperty("encryptedRootKey").GetString()!), Password);
            AssertAbsent(deniedBody, [.. RkForms(rootKey), Password]);

            // The account still opens with its own password afterwards.
            var stillOurs = new AuthClient(host.Http);
            Assert.Equal(HttpStatusCode.OK, (await stillOurs.LoginAsync(handle, Password)).StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    // --- helpers ---------------------------------------------------------------------------------

    private sealed record ExportedKit(
        string Credential,
        string[] Words,
        string EncryptedRootKey,
        string WrappedSigningKey,
        string CreatedAt);

    private sealed record ResolvedProfile(string PreferredUsername, string Pem, string Multibase);

    /// <summary>The throwaway origin instance: register, export, resolve the keys, stop.</summary>
    private static async Task<(ExportedKit Kit, string RsaPem, string Ed25519Multibase)>
        ExportWithOriginAsync(string handle)
    {
        var origin = new AuthHostFixture();
        try
        {
            var owner = origin.NewClient();
            Assert.Equal(HttpStatusCode.Created, (await owner.RegisterAsync(handle, Password)).StatusCode);
            var exported = await WithCsrf(owner, "/api/recovery/kit");
            Assert.Equal(HttpStatusCode.Created, exported.StatusCode);
            using var json = JsonDocument.Parse(await exported.Content.ReadAsStringAsync());
            var kit = json.RootElement;
            var exportedKit = new ExportedKit(
                kit.GetProperty("recoveryCredential").GetString()!,
                kit.GetProperty("mnemonic").EnumerateArray().Select(word => word.GetString()!).ToArray(),
                kit.GetProperty("encryptedRootKey").GetString()!,
                kit.GetProperty("wrappedSigningKey").GetString()!,
                kit.GetProperty("createdAt").GetString()!);

            var profile = await ResolvedAsync(origin.Http, handle);
            return (exportedKit, profile.Pem, profile.Multibase);
        }
        finally
        {
            await origin.DisposeAsync();
        }
    }

    /// <summary>
    /// Webfinger → self link → profile, asserted as a chain: the subject and the href sit on this
    /// instance's own authority with the handle's path, and the document behind them publishes the
    /// two public keys QA compares across instances.
    /// </summary>
    private static async Task<ResolvedProfile> ResolvedAsync(HttpClient http, string handle)
    {
        var authority = http.BaseAddress!.Authority;
        var jrd = await http.GetAsync(WebFinger(http, handle));
        Assert.Equal(HttpStatusCode.OK, jrd.StatusCode);
        using var document = JsonDocument.Parse(await jrd.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal($"acct:{handle}@{authority}", root.GetProperty("subject").GetString());

        var self = root.GetProperty("links").EnumerateArray()
            .Where(link => link.GetProperty("rel").GetString() == "self")
            .ToArray();
        Assert.Single(self);
        Assert.Equal("application/activity+json", self[0].GetProperty("type").GetString());
        var href = self[0].GetProperty("href").GetString()!;
        var uri = new Uri(href);
        Assert.Equal(authority, uri.Authority);
        Assert.Equal($"/users/{handle}", uri.AbsolutePath);

        var profile = await http.GetAsync(href);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var json = JsonDocument.Parse(await profile.Content.ReadAsStringAsync());
        var documentRoot = json.RootElement;
        var preferred = documentRoot.GetProperty("preferredUsername").GetString();
        Assert.Equal(handle, preferred);
        return new ResolvedProfile(
            preferred!,
            documentRoot.GetProperty("publicKey").GetProperty("publicKeyPem").GetString()!,
            documentRoot.GetProperty("squid:ed25519Key").GetProperty("publicKeyMultibase").GetString()!);
    }

    /// <summary>Signs a challenge with the restored RSA private key and verifies it with the PEM the restored instance publishes.</summary>
    private static void AssertRsaSignature(RootKey rootKey, string wrappedSigningKey, string publishedPem, string handle)
    {
        var challenge = Encoding.UTF8.GetBytes($"qa0015-round-trip:{handle}");
        using var restored = KeyDerivation.UnwrapRsaPrivateKey(
            rootKey, Convert.FromBase64String(wrappedSigningKey));
        var signature = restored.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using var published = RSA.Create();
        published.ImportSubjectPublicKeyInfo(PemBody(publishedPem), out _);
        Assert.True(published.VerifyData(
            challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    /// <summary>Signs with the Ed25519 seed the restored RK derives (B-02) and verifies against the multibase the instance publishes.</summary>
    private static void AssertEd25519Signature(RootKey rootKey, string multibase, string handle)
    {
        var challenge = Encoding.UTF8.GetBytes($"qa0015-round-trip:{handle}");
        var seed = HKDF.DeriveKey(HashAlgorithmName.SHA256, rootKey.AsSpan().ToArray(), 32,
            info: Encoding.UTF8.GetBytes(KeyDerivation.Ed25519Info));

        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(seed));
        signer.BlockUpdate(challenge, 0, challenge.Length);
        var signature = signer.GenerateSignature();

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(DecodeEd25519Multibase(multibase)));
        verifier.BlockUpdate(challenge, 0, challenge.Length);
        Assert.True(verifier.VerifySignature(signature));
    }

    /// <summary>base58btc multibase of contract activitypub §1.1 decoded by QA's own decoder — the product's decoder must not grade itself.</summary>
    private static byte[] DecodeEd25519Multibase(string multibase)
    {
        Assert.StartsWith("z", multibase, StringComparison.Ordinal);
        var framed = Base58Decode(multibase[1..]);
        Assert.Equal(34, framed.Length); // 0xed 0x01 multicodec + 32 key bytes
        Assert.Equal(0xed, (int)framed[0]);
        Assert.Equal(0x01, (int)framed[1]);
        return framed[2..];
    }

    private static readonly string Base58Alphabet =
        "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    private static byte[] Base58Decode(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (var character in text)
        {
            var index = Base58Alphabet.IndexOf(character);
            if (index < 0)
            {
                throw new FormatException($"invalid base58 character '{character}'");
            }

            var carry = index;
            for (var i = 0; i < bytes.Count; i++)
            {
                var value = bytes[i] * 58 + carry;
                bytes[i] = (byte)value;
                carry = value >> 8;
            }

            while (carry > 0)
            {
                bytes.Add((byte)carry);
                carry >>= 8;
            }
        }

        var leading = 0;
        while (leading < text.Length && text[leading] == '1')
        {
            bytes.Add(0); // little-endian: they come to the front after the reverse
            leading++;
        }

        bytes.Reverse();
        return bytes.ToArray();
    }

    private static byte[] PemBody(string pem)
    {
        var body = pem
            .Replace("-----BEGIN PUBLIC KEY-----", string.Empty, StringComparison.Ordinal)
            .Replace("-----END PUBLIC KEY-----", string.Empty, StringComparison.Ordinal);
        var compact = new string(body.Where(character => !char.IsWhiteSpace(character)).ToArray());
        return Convert.FromBase64String(compact);
    }

    private static async Task<JsonDocument> MeAsync(AuthClient client)
    {
        var response = await client.SendAsync(HttpMethod.Get, "/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string[] Codes(JsonDocument document) =>
        document.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(warning => warning.GetProperty("code").GetString()!).ToArray();

    /// <summary>Backdates or refreshes CreatedAt of the kit inside the credential row — the ageing no route offers.</summary>
    private static async Task SetKitCreatedAtAsync(string storagePath, string handle, DateTimeOffset createdAt)
    {
        var grainId = GrainId.Create("account", handle).ToString();
        await using var connection = new SqliteConnection($"Data Source={storagePath};Pooling=False");
        await connection.OpenAsync();

        string state;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT State FROM GrainState WHERE StoreName = @Store AND GrainId = @Id";
            read.Parameters.AddWithValue("@Store", SqliteAuthStore.StoreName);
            read.Parameters.AddWithValue("@Id", grainId);
            state = (string?)await read.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("credential row missing");
        }

        // Only CreatedAt moves; Version stays where it is, so the row keeps answering to its ETag.
        var document = JsonNode.Parse(state)!.AsObject();
        document["Kit"]!.AsObject()["CreatedAt"] = JsonValue.Create(createdAt);

        await using (var write = connection.CreateCommand())
        {
            write.CommandText = "UPDATE GrainState SET State = @State WHERE StoreName = @Store AND GrainId = @Id";
            write.Parameters.AddWithValue("@State", document.ToJsonString());
            write.Parameters.AddWithValue("@Store", SqliteAuthStore.StoreName);
            write.Parameters.AddWithValue("@Id", grainId);
            Assert.Equal(1, await write.ExecuteNonQueryAsync());
        }
    }

    /// <summary>SHA-256 over every row of every store, columns and all — the byte-level proof of AC-04.</summary>
    private static async Task<string> StateHashAsync(string storagePath)
    {
        var rows = new List<string>();
        await using var connection = new SqliteConnection($"Data Source={storagePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT StoreName, GrainId, State, Version FROM GrainState ORDER BY StoreName, GrainId";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(string.Join("|", reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3)));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows))));
    }

    /// <summary>The wire `RC-1A2B3C4D-…` back into its bytes — QA's own inverse of RecoveryCredential.Format.</summary>
    private static byte[] ParseRecoveryCredential(string credential)
    {
        Assert.StartsWith("RC-", credential, StringComparison.Ordinal);
        var hex = credential["RC-".Length..].Replace("-", string.Empty, StringComparison.Ordinal);
        var bytes = Convert.FromHexString(hex);
        Assert.Equal(KeyState.RecoveryCodeSizeInBytes, bytes.Length);
        return bytes;
    }

    private static string[] RkForms(RootKey rootKey) =>
    [
        Convert.ToBase64String(rootKey.AsSpan()),
        Convert.ToHexStringLower(rootKey.AsSpan()),
        Convert.ToHexString(rootKey.AsSpan()),
    ];

    private static string WebFinger(HttpClient http, string handle) =>
        "/.well-known/webfinger?resource="
        + Uri.EscapeDataString($"acct:{handle}@{http.BaseAddress!.Authority}");

    private static Task<HttpResponseMessage> WithCsrf(AuthClient client, string path) =>
        client.PostAsync(path, configure: request =>
            request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, client.Csrf!));

    /// <summary>Every form an absence proof has to survive: the raw body, the same body with its \uXXXX escapes decoded, and the decoded string leaves of the JSON it parses to (S-25).</summary>
    private static IEnumerable<string> TextForms(string body)
    {
        var leaves = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(body);
            CollectLeaves(document.RootElement, leaves);
        }
        catch (JsonException)
        {
            // A body that is not JSON has only the raw and unescaped forms to be scanned in.
        }

        return
        [
            body,
            Regex.Replace(body, @"\\u([0-9a-fA-F]{4})",
                static match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString()),
            string.Join('\n', leaves),
        ];
    }

    private static void CollectLeaves(JsonElement element, List<string> leaves)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) CollectLeaves(property.Value, leaves);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectLeaves(item, leaves);
                break;
            case JsonValueKind.String:
                leaves.Add(element.GetString()!);
                break;
        }
    }

    private static void AssertAbsent(string body, params string[] secrets)
    {
        foreach (var form in TextForms(body))
        {
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, form, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string Handle(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    private static int Occurrences(string text, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(fragment, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }
}
