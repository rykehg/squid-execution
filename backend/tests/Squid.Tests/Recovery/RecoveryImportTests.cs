using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Protocols.ActivityPub;
using Squid.Tests.Auth;

namespace Squid.Tests.Recovery;

/// <summary>
/// AC-03 and AC-04 of TASK-0016 - the DoD Sprint 1 closes on: a kit exported on one instance
/// restores the SAME identity on a clean one (same handle, same Ed25519, same RSA, signing with
/// the old keys against the ones that instance publishes), and a wrong password fails as one
/// typed refusal while every persisted row keeps its exact bytes (data-model 2.12).
/// Each half starts its own host and stops it before the next one begins: localhost clustering
/// fixes the silo ports, so hosts never overlap - the shared ServiceHost collection keeps other
/// host-starting classes from overlapping them either.
/// </summary>
[Collection("ServiceHost")]
public sealed class RecoveryImportTests
{
    private const string Password = "senha-forte-1234";

    [Fact] // AC-03
    public async Task A_downloaded_kit_restores_the_same_identity_on_a_clean_instance()
    {
        var handle = Handle("ac03");
        var kit = await ExportOnFreshInstanceAsync(handle);

        var clean = new AuthHostFixture();
        try
        {
            // Instancia limpa, sem o ator - the premise of AC-03, proved before the import runs.
            Assert.Equal(HttpStatusCode.NotFound, (await clean.Http.GetAsync($"/users/{handle}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clean.Http.GetAsync(WebFinger(clean, handle))).StatusCode);

            var stranger = new AuthClient(clean.Http); // no session: import is the pre-login path
            var import = new
            {
                handle,
                password = Password,
                recoveryCredential = kit.Credential,
                encryptedRootKey = kit.EncryptedRootKey,
                wrappedSigningKey = kit.WrappedSigningKey,
                createdAt = kit.CreatedAt,
            };
            var restored = await stranger.PostAsync("/api/recovery/import", import);
            Assert.Equal(HttpStatusCode.Created, restored.StatusCode);
            using var restoredJson = JsonDocument.Parse(await restored.Content.ReadAsStringAsync());
            Assert.Equal(handle, restoredJson.RootElement.GetProperty("handle").GetString());

            // Same handle, same two public keys, and the actor resolves in webfinger (FR-006).
            var profile = ProfileJsonLd.Materialize(await clean.Http.GetStringAsync($"/users/{handle}"));
            Assert.Equal(handle, profile.PreferredUsername);
            Assert.Equal(kit.Profile.RsaPublicKeyBase64, profile.RsaPublicKeyBase64);
            Assert.Equal(kit.Profile.Ed25519PublicKeyBase64, profile.Ed25519PublicKeyBase64);
            Assert.Equal(HttpStatusCode.OK, (await clean.Http.GetAsync(WebFinger(clean, handle))).StatusCode);

            // The RK of the kit came back: it re-derives the same Ed25519, and both restored
            // private keys sign a challenge the published keys verify (the assina/verifica of AC-03).
            var rootKey = RootKeyCipher.Decrypt(Convert.FromBase64String(kit.EncryptedRootKey), Password);
            Assert.Equal(
                kit.Profile.Ed25519PublicKeyBase64,
                Convert.ToBase64String(KeyDerivation.DeriveEd25519PublicKey(rootKey)));
            AssertRsaSignature(rootKey, kit.WrappedSigningKey, profile);
            AssertEd25519Signature(rootKey, profile);

            // The restored account logs in with its own password and hands out the very same kit.
            var owner = new AuthClient(clean.Http);
            Assert.Equal(HttpStatusCode.OK, (await owner.LoginAsync(handle, Password)).StatusCode);
            var again = await WithCsrf(owner, "/api/recovery/kit");
            Assert.Equal(HttpStatusCode.Created, again.StatusCode);
            using var againJson = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
            var exported = againJson.RootElement;
            Assert.Equal(kit.Credential, exported.GetProperty("recoveryCredential").GetString());
            Assert.Equal(kit.EncryptedRootKey, exported.GetProperty("encryptedRootKey").GetString());
            Assert.Equal(kit.WrappedSigningKey, exported.GetProperty("wrappedSigningKey").GetString());
            Assert.Equal(kit.CreatedAt, exported.GetProperty("createdAt").GetString());
            Assert.Equal(kit.Words, exported.GetProperty("mnemonic").EnumerateArray()
                .Select(word => word.GetString()!).ToArray());

            // Importing the same kit again answers with the same identity, not a conflict (B-14).
            var repeat = await stranger.PostAsync("/api/recovery/import", import);
            Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);
            Assert.Equal(
                await restored.Content.ReadAsStringAsync(),
                await repeat.Content.ReadAsStringAsync());
        }
        finally
        {
            await clean.DisposeAsync();
        }
    }

    [Fact] // AC-04
    public async Task A_wrong_password_answers_typed_and_leaves_every_row_byte_identical()
    {
        var handle = Handle("ac04");
        var host = new AuthHostFixture();
        try
        {
            var owner = host.NewClient();
            Assert.Equal(HttpStatusCode.Created, (await owner.RegisterAsync(handle, Password)).StatusCode);
            var exported = await WithCsrf(owner, "/api/recovery/kit");
            Assert.Equal(HttpStatusCode.Created, exported.StatusCode);
            using var json = JsonDocument.Parse(await exported.Content.ReadAsStringAsync());
            var kit = json.RootElement;

            var before = await StateHashAsync(host.StoragePath);

            var attempt = new AuthClient(host.Http);
            var denied = await attempt.PostAsync("/api/recovery/import", new
            {
                handle,
                password = "senha-diferente-99",
                recoveryCredential = kit.GetProperty("recoveryCredential").GetString(),
                encryptedRootKey = kit.GetProperty("encryptedRootKey").GetString(),
                wrappedSigningKey = kit.GetProperty("wrappedSigningKey").GetString(),
                createdAt = kit.GetProperty("createdAt").GetString(),
            });

            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using var error = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.Equal(
                "recovery_failed",
                error.RootElement.GetProperty("error").GetProperty("code").GetString());

            // Byte for byte: every row of every store keeps exactly the content it had - the
            // attempt only ever read the sealed blob (data-model 2.12, B-15).
            Assert.Equal(before, await StateHashAsync(host.StoragePath));

            // ...and the account still opens with its own password afterwards.
            var stillOurs = new AuthClient(host.Http);
            Assert.Equal(HttpStatusCode.OK, (await stillOurs.LoginAsync(handle, Password)).StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>The throwaway instance of the download half: register, export, stop (ports are fixed).</summary>
    private static async Task<ExportedKit> ExportOnFreshInstanceAsync(string handle)
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
            return new ExportedKit(
                kit.GetProperty("recoveryCredential").GetString()!,
                kit.GetProperty("mnemonic").EnumerateArray().Select(word => word.GetString()!).ToArray(),
                kit.GetProperty("encryptedRootKey").GetString()!,
                kit.GetProperty("wrappedSigningKey").GetString()!,
                kit.GetProperty("createdAt").GetString()!,
                ProfileJsonLd.Materialize(await origin.Http.GetStringAsync($"/users/{handle}")));
        }
        finally
        {
            await origin.DisposeAsync();
        }
    }

    /// <summary>Signs a challenge with the restored RSA private key and verifies it with the SPKI that instance publishes.</summary>
    private static void AssertRsaSignature(RootKey rootKey, string wrappedSigningKey, ActorProfile profile)
    {
        var challenge = Encoding.UTF8.GetBytes($"recovery-round-trip:{profile.PreferredUsername}");
        using var restored = KeyDerivation.UnwrapRsaPrivateKey(
            rootKey, Convert.FromBase64String(wrappedSigningKey));
        var signature = restored.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using var published = RSA.Create();
        published.ImportSubjectPublicKeyInfo(Convert.FromBase64String(profile.RsaPublicKeyBase64), out _);
        Assert.True(published.VerifyData(
            challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    /// <summary>Signs with the Ed25519 seed the restored RK derives (B-02) and verifies against the published key.</summary>
    private static void AssertEd25519Signature(RootKey rootKey, ActorProfile profile)
    {
        var challenge = Encoding.UTF8.GetBytes($"recovery-round-trip:{profile.PreferredUsername}");
        var seed = HKDF.DeriveKey(HashAlgorithmName.SHA256, rootKey.AsSpan().ToArray(), 32,
            info: Encoding.UTF8.GetBytes(KeyDerivation.Ed25519Info));

        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(seed));
        signer.BlockUpdate(challenge, 0, challenge.Length);
        var signature = signer.GenerateSignature();

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(
            Convert.FromBase64String(profile.Ed25519PublicKeyBase64)));
        verifier.BlockUpdate(challenge, 0, challenge.Length);
        Assert.True(verifier.VerifySignature(signature));
    }

    /// <summary>SHA-256 over every row of every store, columns and all - the byte-level proof of AC-04.</summary>
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
            // Column separators keep the concatenation unambiguous whatever the JSON contains.
            rows.Add(string.Join("|", reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3)));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows))));
    }

    private sealed record ExportedKit(
        string Credential,
        string[] Words,
        string EncryptedRootKey,
        string WrappedSigningKey,
        string CreatedAt,
        ActorProfile Profile);

    private static string WebFinger(AuthHostFixture host, string handle) =>
        "/.well-known/webfinger?resource="
        + Uri.EscapeDataString($"acct:{handle}@{host.Http.BaseAddress!.Authority}");

    private static Task<HttpResponseMessage> WithCsrf(AuthClient client, string path) =>
        client.PostAsync(path, configure: request =>
            request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, client.Csrf!));

    private static string Handle(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}