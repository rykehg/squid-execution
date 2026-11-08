using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Orleans;
using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Recovery;

/// <summary>
/// AC-01 and AC-02 of TASK-0016 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// the kit once (201, 24 words, no clear `RK`, no read route that hands any of it back) and the
/// FR-007 notice that lives until the kit is proven kept — and returns the moment it passes
/// twelve months, verified or not.
/// </summary>
[Collection("ServiceHost")]
public sealed class RecoveryKitTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public RecoveryKitTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task The_kit_route_answers_201_with_24_words_once_and_never_the_clear_root_key()
    {
        var handle = Handle("kit01");
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);

        // The route is authenticated (401 without a session) and its mutation sits behind the
        // double submission like every other (B-06, 403 without the token).
        var anonymous = _host.NewClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync("/api/recovery/kit")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/recovery/kit")).StatusCode);

        var response = await WithCsrf(client, "/api/recovery/kit");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var kit = json.RootElement;

        Assert.Equal(1, Occurrences(body, "\"mnemonic\""));
        Assert.StartsWith("RC-", kit.GetProperty("recoveryCredential").GetString(), StringComparison.Ordinal);
        var words = kit.GetProperty("mnemonic").EnumerateArray().Select(word => word.GetString()!).ToArray();
        Assert.Equal(Mnemonic.WordCount, words.Length);
        Assert.False(string.IsNullOrEmpty(kit.GetProperty("encryptedRootKey").GetString()));
        Assert.True(kit.TryGetProperty("createdAt", out _));
        // data-model §2.12 puts the wrapped signing key in the kit; B-14 has no other path to it.
        Assert.False(string.IsNullOrEmpty(kit.GetProperty("wrappedSigningKey").GetString()));

        // The clear `RK`: open the sealed blob with the password and prove those bytes appear in
        // no form of no response — raw and \u-escaped JSON both (S-25: base64 `+` travels both ways).
        var rootKey = RootKeyCipher.Decrypt(
            Convert.FromBase64String(kit.GetProperty("encryptedRootKey").GetString()!), Password);
        var clear = new[]
        {
            Convert.ToBase64String(rootKey.AsSpan()),
            Convert.ToHexStringLower(rootKey.AsSpan()),
        };
        var credential = kit.GetProperty("recoveryCredential").GetString()!;

        // No read route recovers the kit: the GET of the kit path answers 405 (only POST exists),
        // and neither /api/auth/me nor the public profile nor webfinger carries a word of it.
        var readRoutes = new[]
        {
            await client.SendAsync(HttpMethod.Get, "/api/recovery/kit"),
            await client.SendAsync(HttpMethod.Get, "/api/auth/me"),
            await _host.Http.GetAsync($"/users/{handle}"),
            await _host.Http.GetAsync($"/.well-known/webfinger?resource="
                + Uri.EscapeDataString($"acct:{handle}@{Authority}")),
        };
        Assert.Equal(HttpStatusCode.MethodNotAllowed, readRoutes[0].StatusCode);

        var arrayForms = new[]
        {
            string.Join(' ', words),
            string.Join("\",\"", words),
            string.Join("\", \"", words),
        };
        foreach (var read in readRoutes)
        {
            var text = await read.Content.ReadAsStringAsync();
            AssertAbsent(text, clear);
            AssertAbsent(text, arrayForms);
            AssertAbsent(text, credential);
        }
    }

    [Fact] // AC-02
    public async Task The_me_route_warns_until_the_kit_is_verified_and_again_once_the_kit_turns_old()
    {
        var handle = Handle("me01");
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);

        // Contract §2: "ator autenticado" — no session answers the typed 401, notices or not.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _host.NewClient().SendAsync(HttpMethod.Get, "/api/auth/me")).StatusCode);

        using (var pending = await MeAsync(client))
        {
            Assert.Equal(handle, pending.RootElement.GetProperty("handle").GetString());
            Assert.Equal(new[] { "kit_unverified" }, Codes(pending));
        }

        // Verify is a mutation too: 403 without the token, then the proof is recorded (B-13).
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/recovery/kit/verify")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await WithCsrf(client, "/api/recovery/kit/verify")).StatusCode);

        using (var proven = await MeAsync(client))
        {
            Assert.Empty(Codes(proven));
        }

        // A kit of 13 months brings the notice back even though it was verified (FR-007).
        await AgeKitAsync(handle, DateTimeOffset.UtcNow.AddMonths(-13));
        using (var stale = await MeAsync(client))
        {
            Assert.Equal(new[] { "kit_stale" }, Codes(stale));
        }
    }

    private async Task<JsonDocument> MeAsync(AuthClient client)
    {
        var response = await client.SendAsync(HttpMethod.Get, "/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string[] Codes(JsonDocument document) =>
        document.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(warning => warning.GetProperty("code").GetString()!).ToArray();

    /// <summary>Backdates the kit inside the credential row in place — the one ageing no route offers.</summary>
    private async Task AgeKitAsync(string handle, DateTimeOffset createdAt)
    {
        var grainId = GrainId.Create("account", handle).ToString();
        await using var connection = new SqliteConnection($"Data Source={_host.StoragePath};Pooling=False");
        await connection.OpenAsync();

        string state;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT State FROM GrainState WHERE StoreName = @Store AND GrainId = @Id";
            read.Parameters.AddWithValue("@Store", SqliteAuthStore.StoreName);
            read.Parameters.AddWithValue("@Id", grainId);
            state = (string?)await read.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("credential row ausente");
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

    private static Task<HttpResponseMessage> WithCsrf(AuthClient client, string path) =>
        client.PostAsync(path, configure: request =>
            request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, client.Csrf!));

    /// <summary>Every form an absence proof has to survive: the raw body, the same body with its
    /// \uXXXX escapes decoded, and the decoded string leaves of the JSON it parses to.</summary>
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

    /// <summary>The host part of the origin — the instance domain webfinger has to match (B-09a).</summary>
    private string Authority => _host.Http.BaseAddress!.Authority;

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
