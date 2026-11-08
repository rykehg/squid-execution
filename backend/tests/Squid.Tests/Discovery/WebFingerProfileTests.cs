using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orleans;
using Squid.Api.Crypto;
using Squid.Grains.Actors;
using Squid.Protocols.ActivityPub;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Discovery;

/// <summary>
/// AC-01..AC-04 of TASK-0015, proved over HTTP against the host <see cref="SquidServiceHost"/>
/// builds: webfinger resolving a local handle, the three 404 cases (unknown, malformed, foreign
/// domain — B-09a), the session-less profile with both public keys and no private material, and
/// the lossless canonical ↔ JSON-LD round-trip (B-11).
/// </summary>
[Collection("ServiceHost")]
public sealed class WebFingerProfileTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public WebFingerProfileTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Webfinger_resolves_a_local_handle_to_the_profile_with_a_self_link()
    {
        var handle = Handle("wf01");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, Password)).StatusCode);

        var response = await _host.Http.GetAsync(
            $"/.well-known/webfinger?resource={Uri.EscapeDataString($"acct:{handle}@{Authority}")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"acct:{handle}@{Authority}", json.RootElement.GetProperty("subject").GetString());

        var self = json.RootElement.GetProperty("links").EnumerateArray()
            .Single(link => link.GetProperty("rel").GetString() == "self");
        Assert.Equal("application/activity+json", self.GetProperty("type").GetString());
        Assert.Equal($"{Origin}/users/{handle}", self.GetProperty("href").GetString());
    }

    [Fact] // AC-02
    public async Task Webfinger_answers_404_for_unknown_malformed_and_foreign_resources()
    {
        var handle = Handle("wf02");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, Password)).StatusCode);

        // Unknown: a well-formed acct on this domain whose actor does not exist.
        await AssertNotFound($"acct:{Handle("wf02none")}@{Authority}");

        // Malformed: no acct scheme, no @, empty name, wrong scheme case, no resource at all.
        foreach (var resource in new[]
        {
            handle,
            $"acct:{handle}",
            $"acct:@{Authority}",
            $"ACCT:{handle}@{Authority}",
        })
        {
            await AssertNotFound(resource);
        }

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _host.Http.GetAsync("/.well-known/webfinger")).StatusCode);

        // Foreign domain, existing actor: no proxying — even the same host without the port
        // counts as another domain, because the domain is this request's Host (B-09a).
        await AssertNotFound($"acct:{handle}@example.org");
        await AssertNotFound($"acct:{handle}@127.0.0.1");
    }

    [Fact] // AC-03
    public async Task The_profile_serves_without_a_session_with_both_keys_and_no_private_material()
    {
        var handle = Handle("wf03");
        var register = await _host.NewClient().RegisterAsync(handle, Password, displayName: "Alice Perfil");
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        // No session: a brand-new client whose jar is empty, so no cookie rides along.
        var response = await _host.Http.GetAsync($"/users/{handle}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/activity+json", response.Content.Headers.ContentType?.MediaType);

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

        var publicKey = document.GetProperty("publicKey");
        Assert.Equal($"{Origin}/users/{handle}#main-key", publicKey.GetProperty("id").GetString());
        Assert.Equal($"{Origin}/users/{handle}", publicKey.GetProperty("owner").GetString());
        var pem = publicKey.GetProperty("publicKeyPem").GetString();
        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", pem, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", pem, StringComparison.Ordinal);

        var ed25519 = document.GetProperty("squid:ed25519Key");
        Assert.Equal($"{Origin}/users/{handle}#ed25519", ed25519.GetProperty("id").GetString());
        Assert.StartsWith("z", ed25519.GetProperty("publicKeyMultibase").GetString(), StringComparison.Ordinal);

        // No private material anywhere in the document: the wrapped RSA key of the actor row and
        // the clear RK from the kit, scanned in the raw body, in the raw body with \u002B unescaped
        // and in every decoded string (S-25: System.Text.Json escapes '+' in base64 as \u002B).
        var actorRow = await AuthDb.ReadAsync(
            _host.StoragePath,
            SqliteActorEventStore.StoreName,
            GrainId.Create("actor", handle).ToString());
        Assert.NotNull(actorRow);
        var wrapped = Regex.Match(actorRow!, "\"WrappedSigningKey\":\"([^\"]+)\"");
        Assert.True(wrapped.Success, "the wrapped signing key was not found in the actor row");

        using var kit = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var encryptedRootKey = kit.RootElement.GetProperty("kit").GetProperty("encryptedRootKey").GetString()!;
        var rootKey = RootKeyCipher.Decrypt(Convert.FromBase64String(encryptedRootKey), Password);
        var secrets = new[]
        {
            wrapped.Groups[1].Value,
            Convert.ToBase64String(rootKey.AsSpan()),
            Convert.ToHexStringLower(rootKey.AsSpan()),
        };

        var unescaped = raw.Replace("\\u002B", "+", StringComparison.Ordinal);
        var decodedStrings = StringValues(document).ToList();
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, raw, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, unescaped, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, decodedStrings);
        }
    }

    [Fact] // AC-04
    public async Task The_round_trip_canonical_to_jsonld_and_back_loses_nothing()
    {
        var keys = KeyState.Create(RootKey.Create(), Password);
        var material = new ActorKeyMaterial(
            [
                new ActorPublicKey("RSA-2048", "#main-key", Convert.ToBase64String(keys.RsaPublicKey)),
                new ActorPublicKey("Ed25519", "#ed25519", Convert.ToBase64String(keys.Ed25519PublicKey)),
            ],
            Convert.ToBase64String(keys.WrappedRsaPrivateKey));
        var state = new ActorState("alice_roundtrip", "Alice da Silva", material);

        var profile = ActorProfile.FromState(state, Origin);
        var back = ProfileJsonLd.Materialize(ProfileJsonLd.Serialize(state, Origin));

        // The full record: drop the name, a key or the origin from the document and this fails.
        Assert.Equal(profile, back);
        Assert.Equal(profile.Id, back.Id); // B-11: id → JSON-LD → id igual

        // And the server's own document materializes back to the same id and fields.
        var handle = Handle("wf04");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, Password, displayName: "Alice Roundtrip")).StatusCode);
        var served = ProfileJsonLd.Materialize(await _host.Http.GetStringAsync($"/users/{handle}"));
        Assert.Equal($"{Origin}/users/{handle}", served.Id);
        Assert.Equal(handle, served.PreferredUsername);
        Assert.Equal("Alice Roundtrip", served.Name);
    }

    /// <summary>Sends the resource to webfinger and demands the same 404 every refusal answers.</summary>
    private async Task AssertNotFound(string resource)
    {
        var response = await _host.Http.GetAsync(
            $"/.well-known/webfinger?resource={Uri.EscapeDataString(resource)}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Every string leaf of the document, decoded — the half of S-25 that sees past \u002B.</summary>
    private static IEnumerable<string> StringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var value in StringValues(property.Value))
                    {
                        yield return value;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in StringValues(item))
                    {
                        yield return value;
                    }
                }

                break;
        }
    }

    /// <summary>The authority and origin exactly as this client addresses the host (B-09a).</summary>
    private string Authority => _host.Http.BaseAddress!.Authority;

    private string Origin => $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}";

    private static string Handle(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
