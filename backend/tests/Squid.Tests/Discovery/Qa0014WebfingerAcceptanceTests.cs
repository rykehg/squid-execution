using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Squid.Api.Crypto;
using Squid.Grains.Actors;
using Squid.Protocols.ActivityPub;
using Squid.Service;
using Squid.Tests.Auth;
using Xunit.Abstractions;

namespace Squid.Tests.Discovery;

/// <summary>
/// QA-0014 — independent verification of TASK-0015's AC-01..AC-04 against the acceptance list
/// (SPEC-0007 B-09..B-11 and the B-09a settlement of D-01). These are QA's own scenarios, not a
/// re-read of DEV's <see cref="WebFingerProfileTests"/>: expected values come from the artifacts
/// that state them (SPEC-0007 B-09..B-11, contract activitypub section 1/1.1, RFC 7033), the served
/// RSA PEM is parsed and asserted to be a 2048-bit SPKI matching the persisted row, the Ed25519
/// multibase is decoded by QA's own base58 and checked byte for byte against the persisted public
/// key, and the B-09a settlement — origin is the request's scheme + Host, no config key,
/// X-Forwarded-* ignored (parked I-11) — is exercised over HTTP with a forged Host header and with
/// forwarded headers. AC-05 (gate + touches) is proved by
/// <c>docs/sdd/qa/qa-check-task-0015.ps1</c>, not here.
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0014WebfingerAcceptanceTests : IClassFixture<AuthHostFixture>
{
    /// <summary>Fixture password of this suite: at least 10 characters, never a real credential.</summary>
    private const string Password = "qa0014-webfinger-probe-61728";

    private const string ActivityJson = "application/activity+json";
    private const string ForgedHost = "qa0014-forged.example";
    private const string ForwardedHost = "forwarded.example";

    private readonly AuthHostFixture _host;
    private readonly ITestOutputHelper _output;

    public Qa0014WebfingerAcceptanceTests(AuthHostFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    [Fact] // AC-01
    public async Task Ac01_webfinger_is_200_with_subject_and_a_self_link_to_the_profile()
    {
        var handle = Unique("qa14a");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, Password)).StatusCode);

        using var anon = Anon();
        using var response = await anon.GetAsync(Webfinger($"acct:{handle}@{Authority}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // RFC 7033 section 3.1: a webfinger JRD is served as application/jrd+json.
        Assert.Equal("application/jrd+json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var subject = root.GetProperty("subject").GetString();
        Assert.Equal($"acct:{handle}@{Authority}", subject);

        var self = root.GetProperty("links").EnumerateArray()
            .Single(link => link.GetProperty("rel").GetString() == "self");
        Assert.Equal(ActivityJson, self.GetProperty("type").GetString());
        var href = self.GetProperty("href").GetString();
        Assert.Equal($"{Origin}/users/{handle}", href);

        // B-09a: subject and href sit on the one origin this request asked (scheme + Host).
        Assert.Equal(Authority, new Uri(href!).Authority);
        _output.WriteLine($"PROBE ac01 subject={subject} href={href}");
    }

    [Fact] // AC-02
    public async Task Ac02_webfinger_is_404_for_unknown_malformed_and_foreign_resources()
    {
        var handle = Unique("qa14b");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, Password)).StatusCode);

        using var anon = Anon();

        // Unknown: a well-formed acct on this very domain whose actor does not exist (B-09).
        await AssertNotFound(anon, $"acct:{Unique("qa14u")}@{Authority}", handle);

        // Malformed: every shape that is not acct:<name>@<host> of this request.
        foreach (var resource in new[]
        {
            handle,                               // no acct: scheme
            $"acct:{handle}",                     // no @
            $"acct:@{Authority}",                 // empty name
            $"ACCT:{handle}@{Authority}",         // scheme case is part of the shape
            $"acct:{handle}@@{Authority}",        // two @
            $"acct:{handle}@{Authority}/",        // trailing junk after the authority
        })
        {
            await AssertNotFound(anon, resource, handle);
        }

        // No resource parameter at all.
        using (var none = await anon.GetAsync("/.well-known/webfinger"))
        {
            Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        }

        // Foreign domain with an EXISTING actor: no proxying (B-09a). The port is part of the
        // authority — the same host without it, and the same host on another port, are foreign.
        await AssertNotFound(anon, $"acct:{handle}@example.org", handle);
        await AssertNotFound(anon, $"acct:{handle}@127.0.0.1", handle);
        var port = new Uri(Origin).Port;
        var otherPort = port >= 65535 ? port - 1 : port + 1;
        await AssertNotFound(anon, $"acct:{handle}@127.0.0.1:{otherPort}", handle);

        _output.WriteLine($"PROBE ac02 refused: unknown=1 malformed=6 no-resource=1 foreign=3");
    }

    [Fact] // AC-03
    public async Task Ac03_profile_serves_without_session_with_2048_rsa_ed25519_and_no_private_material()
    {
        var handle = Unique("qa14c");
        var register = await _host.NewClient().RegisterAsync(handle, Password, displayName: "QA Perfil Quatorze");
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registerBody = await register.Content.ReadAsStringAsync();

        // The persisted keys, read through the product's own store and typed (S-25: deserialized,
        // never regex-scanned — base64 '+' is escaped as the six-character sequence by
        // System.Text.Json, so a raw scan sees only half of any key). A fresh actor has no
        // snapshot yet, so the state comes from the ActorCreated event of the open log.
        var store = new SqliteActorEventStore($"Data Source={_host.StoragePath};Pooling=False");
        var record = await store.ReadAsync(handle);
        Assert.NotNull(record);
        var persistedKeys = record!.Stream.Snapshot?.Keys
            ?? record.Stream.Events.OfType<ActorCreated>().Single(created => created.Handle == handle).Keys;
        var storedRsa = persistedKeys.PublicKeys.Single(key => key.Algorithm == "RSA-2048");
        var storedEd = persistedKeys.PublicKeys.Single(key => key.Algorithm == "Ed25519");
        var wrapped = persistedKeys.WrappedSigningKey;

        // The clear RK behind this registration, opened with the fixture password (test data).
        using var kitJson = JsonDocument.Parse(registerBody);
        var encryptedRootKey = kitJson.RootElement.GetProperty("kit").GetProperty("encryptedRootKey").GetString()!;
        var rootKey = RootKeyCipher.Decrypt(Convert.FromBase64String(encryptedRootKey), Password);
        var rkForms = new[]
        {
            Convert.ToBase64String(rootKey.AsSpan()),
            Convert.ToHexStringLower(rootKey.AsSpan()),
            Convert.ToHexString(rootKey.AsSpan()),
        };

        // Session-less by construction: a fresh client that cannot hold cookies sends none.
        using var anon = Anon();
        using var response = await anon.GetAsync($"/users/{handle}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ActivityJson, response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("Set-Cookie"), "the session-less profile must not start a session");

        var raw = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(raw);
        var document = json.RootElement;

        Assert.Equal($"{Origin}/users/{handle}", document.GetProperty("id").GetString());
        Assert.Equal(handle, document.GetProperty("preferredUsername").GetString());
        Assert.Equal("Person", document.GetProperty("type").GetString());

        // Fixed, embedded @context — no network fetch in a test (task Gaps).
        var context = document.GetProperty("@context");
        Assert.Equal("https://www.w3.org/ns/activitystreams", context[0].GetString());
        Assert.Equal($"{Origin}/ns/squid#", context[1].GetProperty("squid").GetString());

        // RSA-2048 #main-key: parsed OUT of the served PEM, not merely looked at.
        var publicKey = document.GetProperty("publicKey");
        Assert.Equal($"{Origin}/users/{handle}#main-key", publicKey.GetProperty("id").GetString());
        Assert.Equal($"{Origin}/users/{handle}", publicKey.GetProperty("owner").GetString());
        var pem = publicKey.GetProperty("publicKeyPem").GetString()!;
        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", pem, StringComparison.Ordinal);
        var pemBody = string.Concat(pem.Split('\n').Where(line => !line.StartsWith("-----", StringComparison.Ordinal)));
        Assert.Equal(storedRsa.PublicKey, pemBody); // the PEM carries THE persisted SPKI, verbatim
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pemBody), out _);
        var rsaBits = rsa.KeySize;
        Assert.Equal(2048, rsaBits);

        // Ed25519 #ed25519: decoded with QA's own base58 (the product's Base58 is internal) and
        // checked byte for byte against the persisted public key.
        var ed25519 = document.GetProperty("squid:ed25519Key");
        Assert.Equal($"{Origin}/users/{handle}#ed25519", ed25519.GetProperty("id").GetString());
        var multibase = ed25519.GetProperty("publicKeyMultibase").GetString()!;
        Assert.StartsWith("z", multibase, StringComparison.Ordinal);
        var framed = Base58Decode(multibase[1..]);
        Assert.Equal(34, framed.Length); // multicodec ed25519-pub (0xed 0x01) + 32 raw bytes
        Assert.Equal((byte)0xed, framed[0]);
        Assert.Equal((byte)0x01, framed[1]);
        Assert.Equal(storedEd.PublicKey, Convert.ToBase64String(framed, 2, 32));

        // No private material anywhere in the document (S-25): the wrapped RSA key of the row,
        // the clear RK in base64 + hex, the password and the private-key markers, each scanned in
        // the raw body, in the raw body with the plus escape unescaped, and in every decoded string.
        var secrets = new List<string> { wrapped, Password, "PRIVATE", "WrappedSigningKey" };
        secrets.AddRange(rkForms);
        var unescaped = raw.Replace("\\u002B", "+", StringComparison.Ordinal);
        var decoded = StringValues(document);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, raw, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, unescaped, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, decoded);
        }

        // No auth dependency leaked in: a garbage session cookie still serves the profile.
        using var poisoned = new HttpRequestMessage(HttpMethod.Get, $"/users/{handle}");
        poisoned.Headers.TryAddWithoutValidation("Cookie", "squid_session=not-a-real-session; squid_csrf=bogus");
        using var second = await anon.SendAsync(poisoned);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        _output.WriteLine($"PROBE ac03 rsa-bits={rsaBits} ed25519-framed={framed.Length} secrets-scanned={secrets.Count} views=raw,unescaped,decoded");
    }

    [Fact] // AC-04
    public async Task Ac04_round_trip_canonical_jsonld_canonical_keeps_every_field()
    {
        var keys = KeyState.Create(RootKey.Create(), Password);
        var material = new ActorKeyMaterial(
            [
                new ActorPublicKey("RSA-2048", "#main-key", Convert.ToBase64String(keys.RsaPublicKey)),
                new ActorPublicKey("Ed25519", "#ed25519", Convert.ToBase64String(keys.Ed25519PublicKey)),
            ],
            Convert.ToBase64String(keys.WrappedRsaPrivateKey));
        var state = new ActorState("alice_qa14", "Alice do QA", material);

        var profile = ActorProfile.FromState(state, Origin);
        var json = ProfileJsonLd.Serialize(state, Origin);
        var back = ProfileJsonLd.Materialize(json);

        // Every field of the canonical record survives — drop the name, a key or the origin from
        // the document and this equality breaks.
        Assert.Equal(profile, back);
        Assert.Equal(profile.Id, back.Id); // B-11 literal: id -> JSON-LD -> id igual

        // Re-serializing what came back reproduces the exact document — nothing encoded is lost.
        Assert.Equal(json, ProfileJsonLd.Serialize(StateOf(back), back.Origin));

        // A nameless actor: the omitted "name" round-trips to null, never to a phantom value.
        var nameless = state with { DisplayName = null };
        var namelessJson = ProfileJsonLd.Serialize(nameless, Origin);
        Assert.DoesNotContain("\"name\"", namelessJson, StringComparison.Ordinal);
        Assert.Equal(ActorProfile.FromState(nameless, Origin), ProfileJsonLd.Materialize(namelessJson));

        // The server's own document, over HTTP: materialize -> re-serialize -> byte-identical.
        var handle = Unique("qa14d");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, Password, displayName: "QA Roundtrip")).StatusCode);
        using var anon = Anon();
        var served = await anon.GetStringAsync($"/users/{handle}");
        var materialized = ProfileJsonLd.Materialize(served);
        Assert.Equal($"{Origin}/users/{handle}", materialized.Id);
        Assert.Equal(handle, materialized.PreferredUsername);
        Assert.Equal("QA Roundtrip", materialized.Name);
        Assert.Equal(served, ProfileJsonLd.Serialize(StateOf(materialized), materialized.Origin));

        _output.WriteLine($"PROBE ac04 record-equal=True served-bytes={served.Length} reserialize-identical=True");
    }

    [Fact] // B-09a settlement of D-01 (feeds AC-01/AC-02/AC-03)
    public async Task Extra_origin_comes_from_the_request_host_and_ignores_forwarded_headers()
    {
        var handle = Unique("qa14e");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, Password)).StatusCode);

        using var anon = Anon();

        // (a) X-Forwarded-* changes nothing (parked I-11): href and id stay on the asking origin.
        using (var forwardedWebfinger = new HttpRequestMessage(HttpMethod.Get, Webfinger($"acct:{handle}@{Authority}")))
        {
            forwardedWebfinger.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
            forwardedWebfinger.Headers.TryAddWithoutValidation("X-Forwarded-Host", ForwardedHost);
            using var response = await anon.SendAsync(forwardedWebfinger);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal($"acct:{handle}@{Authority}", json.RootElement.GetProperty("subject").GetString());
            var href = json.RootElement.GetProperty("links")[0].GetProperty("href").GetString();
            Assert.Equal($"{Origin}/users/{handle}", href);
        }

        using (var forwardedProfile = new HttpRequestMessage(HttpMethod.Get, $"/users/{handle}"))
        {
            forwardedProfile.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
            forwardedProfile.Headers.TryAddWithoutValidation("X-Forwarded-Host", ForwardedHost);
            using var response = await anon.SendAsync(forwardedProfile);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal($"{Origin}/users/{handle}", json.RootElement.GetProperty("id").GetString());
        }

        // (b) The same actor asked under a different Host answers on THAT host: one source, the
        // request (scheme + Host), never a config key.
        using (var forgedWebfinger = new HttpRequestMessage(HttpMethod.Get, Webfinger($"acct:{handle}@{ForgedHost}")))
        {
            forgedWebfinger.Headers.Host = ForgedHost;
            using var response = await anon.SendAsync(forgedWebfinger);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal($"acct:{handle}@{ForgedHost}", json.RootElement.GetProperty("subject").GetString());
            var href = json.RootElement.GetProperty("links")[0].GetProperty("href").GetString();
            Assert.Equal($"http://{ForgedHost}/users/{handle}", href);
        }

        // (c) The real authority asked while the Host is forged -> 404: the domain checked is the
        // request's Host, not the socket's address.
        using (var forgedRealAuthority = new HttpRequestMessage(HttpMethod.Get, Webfinger($"acct:{handle}@{Authority}")))
        {
            forgedRealAuthority.Headers.Host = ForgedHost;
            using var response = await anon.SendAsync(forgedRealAuthority);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // (d) The JSON-LD id follows the same request origin — subject, href and id agree.
        using (var forgedProfile = new HttpRequestMessage(HttpMethod.Get, $"/users/{handle}"))
        {
            forgedProfile.Headers.Host = ForgedHost;
            using var response = await anon.SendAsync(forgedProfile);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal($"http://{ForgedHost}/users/{handle}", json.RootElement.GetProperty("id").GetString());
        }

        _output.WriteLine($"PROBE b09a origin={Origin} forwarded-ignored=True forged-host={ForgedHost} serves=True");
    }

    /// <summary>Demands the same 404 every refusal answers, without leaking the existing actor.</summary>
    private static async Task AssertNotFound(HttpClient client, string resource, string existingHandle)
    {
        using var response = await client.GetAsync(Webfinger(resource));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(existingHandle, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subject", body, StringComparison.OrdinalIgnoreCase); // never a partial JRD
    }

    /// <summary>A client that cannot hold cookies at all — the profile must need no session.</summary>
    private HttpClient Anon() =>
        new(new HttpClientHandler { UseCookies = false }) { BaseAddress = _host.Http.BaseAddress };

    private static string Webfinger(string resource) =>
        "/.well-known/webfinger?resource=" + Uri.EscapeDataString(resource);

    /// <summary>Canonical state rebuilt from a materialized profile — the mapper only reads the
    /// published fields, so the wrapped key it is handed is never used.</summary>
    private static ActorState StateOf(ActorProfile profile) =>
        new(profile.PreferredUsername, profile.Name, new ActorKeyMaterial(
            [
                new ActorPublicKey("RSA-2048", "#main-key", profile.RsaPublicKeyBase64),
                new ActorPublicKey("Ed25519", "#ed25519", profile.Ed25519PublicKeyBase64),
            ],
            "wrapped-not-read-by-the-mapper"));

    /// <summary>
    /// base58btc with QA's own decoder: the product's Base58 is internal, and trusting it here
    /// would make the round-trip check agree with a broken decoder.
    /// </summary>
    private static byte[] Base58Decode(string text)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var zeros = text.TakeWhile(character => character == '1').Count();
        var value = BigInteger.Zero;
        foreach (var character in text)
        {
            var digit = alphabet.IndexOf(character);
            if (digit < 0)
            {
                throw new FormatException($"base58: invalid character '{character}' in '{text}'");
            }

            value = value * 58 + digit;
        }

        var body = value.IsZero
            ? Array.Empty<byte>()
            : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var result = new byte[zeros + body.Length];
        body.CopyTo(result, zeros);
        return result;
    }

    /// <summary>Every string leaf of the document, decoded — the half of S-25 that sees past the escape.</summary>
    private static List<string> StringValues(JsonElement element)
    {
        var found = new List<string>();
        CollectStrings(element, found);
        return found;
    }

    private static void CollectStrings(JsonElement element, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                found.Add(element.GetString()!);
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectStrings(property.Value, found);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, found);
                }

                break;
        }
    }

    /// <summary>The authority and origin exactly as this client addresses the host (B-09a).</summary>
    private string Authority => _host.Http.BaseAddress!.Authority;

    private string Origin => $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}";

    private static string Unique(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
