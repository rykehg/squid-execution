using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orleans;
using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Grains.Actors;
using Squid.Service;
using Squid.Tests.Architecture;
using Xunit.Abstractions;

namespace Squid.Tests.Auth;

/// <summary>
/// QA-0013 — independent verification of TASK-0014's AC-01..AC-05 against the acceptance list.
/// These scenarios are QA's own (not DEV's <see cref="RegisterLoginSessionTests"/>): every expected
/// value is derived from the artifact that states it (contract <c>http-api.md</c> §1/§2/§3,
/// SPEC-0007 B-01..B-07), the two 401 bodies are compared as raw bytes, and the extra facet QA-0012
/// deferred to this task — the clear RK never appears in an HTTP response — is scanned over every
/// auth response body this run produced. AC-06 (gate + touches) is proved by
/// <c>docs/sdd/qa/qa-check-task-0014.ps1</c>, not here.
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0013AuthAcceptanceTests : IClassFixture<AuthHostFixture>
{
    /// <summary>Fixture password of this suite: ≥ 10 characters, never a real credential.</summary>
    private const string Password = "qa0013-pii-probe-458291";

    private readonly AuthHostFixture _host;
    private readonly ITestOutputHelper _output;

    public Qa0013AuthAcceptanceTests(AuthHostFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    [Fact] // AC-01
    public async Task Ac01_register_is_201_with_both_contract_cookies_and_exactly_one_kit()
    {
        var handle = Unique("qa01");
        var client = _host.NewClient();

        var response = await client.RegisterAsync(handle, Password, displayName: "QA Zero Um");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // contracts/http-api.md §2: squid_session HttpOnly + Secure + SameSite=Lax + 30 dias deslizantes.
        var session = AuthClient.SetCookie(response, AuthSessions.SessionCookie);
        Assert.NotNull(session);
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);
        var expiry = CookieExpiry(session);
        Assert.InRange(expiry, DateTimeOffset.UtcNow.AddDays(29.5), DateTimeOffset.UtcNow.AddDays(30.5));
        _output.WriteLine($"PROBE session-cookie = {session}");
        _output.WriteLine($"PROBE session-window-hours = {(expiry - DateTimeOffset.UtcNow).TotalHours:F1} (contract: 30d)");

        // squid_csrf must be readable by the page script — double submission (R-09).
        var csrf = AuthClient.SetCookie(response, AuthSessions.CsrfCookie);
        Assert.NotNull(csrf);
        Assert.DoesNotContain("httponly", csrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", csrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", csrf, StringComparison.OrdinalIgnoreCase);

        // "o kit aparece na resposta uma vez" — one `"kit"` key in the whole body.
        Assert.Equal(1, Occurrences(body, "\"kit\""));
        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(handle, json.RootElement.GetProperty("handle").GetString());
        var kit = json.RootElement.GetProperty("kit");

        // contracts/http-api.md §3 shape: recoveryCredential, mnemonic[24], encryptedRootKey, createdAt.
        var fields = kit.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "recoveryCredential", "mnemonic", "encryptedRootKey", "createdAt" }, fields);
        _output.WriteLine($"PROBE kit-fields = {string.Join(",", fields)}");

        var words = kit.GetProperty("mnemonic");
        Assert.Equal(24, words.GetArrayLength()); // SPEC-0007 B-12: mnemônico de 24 palavras
        _output.WriteLine($"PROBE mnemonic-words = {words.GetArrayLength()}");

        var rc = kit.GetProperty("recoveryCredential").GetString();
        Assert.NotNull(rc);
        Assert.Matches(@"^RC-([0-9A-F]{4}-)+[0-9A-F]{4}$", rc); // §3 example: "RC-XXXX-..."
        _output.WriteLine($"PROBE recoveryCredential = {rc[..Math.Min(rc.Length, 19)]}…");

        var encrypted = kit.GetProperty("encryptedRootKey").GetString();
        Assert.NotNull(encrypted);
        Assert.True(Convert.FromBase64String(encrypted).Length > 0, "encryptedRootKey is not base64");

        var createdAt = kit.GetProperty("createdAt").GetString();
        Assert.True(
            DateTimeOffset.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            "createdAt is not an ISO-8601 timestamp: " + createdAt);

        // The actor exists (event store) and the credential row exists (auth store): one KV row each,
        // keyed exactly as the adjudicated contract says (store auth / key account/<handle>).
        Assert.NotNull(await AuthDb.ReadAsync(
            _host.StoragePath, SqliteActorEventStore.StoreName, GrainId.Create("actor", handle).ToString()));
        var row = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteAuthStore.StoreName, GrainId.Create("account", handle).ToString());
        Assert.NotNull(row);
        _output.WriteLine($"PROBE auth-row-store = {SqliteAuthStore.StoreName} key = account/{handle}");
    }

    [Fact] // AC-02
    public async Task Ac02_refused_registrations_answer_409_422_and_persist_nothing()
    {
        var actorsBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteActorEventStore.StoreName);
        var credentialsBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteAuthStore.StoreName);

        var client = _host.NewClient();
        var handle = Unique("qa02");
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);

        // duplicate → 409, message names the handle and the conflict (FR-001 "mensagem clara")
        var duplicate = await client.RegisterAsync(handle, Password);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var duplicateBody = await duplicate.Content.ReadAsStringAsync();
        Assert.Contains(handle, duplicateBody, StringComparison.Ordinal);
        Assert.Contains("em uso", duplicateBody, StringComparison.Ordinal);
        Assert.Contains("handle_taken", duplicateBody, StringComparison.Ordinal);

        // malformed handle → 422 citing the rule verbatim (SPEC-0007 B-01)
        var malformed = await client.PostAsync(
            "/api/auth/register", new { handle = "Ab!", password = Password });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, malformed.StatusCode);
        var malformedBody = await malformed.Content.ReadAsStringAsync();
        Assert.Contains("[a-z0-9_]{3,30}", malformedBody, StringComparison.Ordinal);
        _output.WriteLine($"PROBE rule-in-body = {ActorHandle.Rule}");

        // password below the minimum → 422 citing the number (SPEC-0007 B-04)
        var weakHandle = Unique("qa02weak");
        var weak = await client.RegisterAsync(weakHandle, "curta1234");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, weak.StatusCode);
        var weakBody = await weak.Content.ReadAsStringAsync();
        Assert.Contains("10", weakBody, StringComparison.Ordinal);
        Assert.Contains("senha", weakBody, StringComparison.OrdinalIgnoreCase);

        // all three refusals wrote nothing: only the one valid registration is on disk
        Assert.Equal(actorsBefore + 1, await AuthDb.CountAsync(_host.StoragePath, SqliteActorEventStore.StoreName));
        Assert.Equal(credentialsBefore + 1, await AuthDb.CountAsync(_host.StoragePath, SqliteAuthStore.StoreName));
        Assert.Null(await AuthDb.ReadAsync(
            _host.StoragePath, SqliteAuthStore.StoreName, GrainId.Create("account", weakHandle).ToString()));
        Assert.Null(await AuthDb.ReadAsync(
            _host.StoragePath, SqliteActorEventStore.StoreName, GrainId.Create("actor", weakHandle).ToString()));
        _output.WriteLine($"PROBE actors {actorsBefore} -> {actorsBefore + 1}; credentials {credentialsBefore} -> {credentialsBefore + 1}");
    }

    [Fact] // AC-03
    public async Task Ac03_the_two_login_401_bodies_are_byte_identical_and_trace_less()
    {
        var handle = Unique("qa03");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, Password)).StatusCode);

        var accepted = _host.NewClient();
        var ok = await accepted.LoginAsync(handle, Password);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.NotNull(AuthClient.SetCookie(ok, AuthSessions.SessionCookie));
        Assert.NotNull(AuthClient.SetCookie(ok, AuthSessions.CsrfCookie));

        var wrongPassword = await _host.NewClient().LoginAsync(handle, "qa0013-senha-diferente-000");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        var wrongBytes = await wrongPassword.Content.ReadAsByteArrayAsync();

        var unknownHandleText = Unique("qa03none");
        var unknownHandle = await _host.NewClient().LoginAsync(unknownHandleText, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownHandle.StatusCode);
        var unknownBytes = await unknownHandle.Content.ReadAsByteArrayAsync();

        // byte for byte, not "similar": SHA-256 of both bodies and a raw span comparison.
        var wrongHash = Convert.ToHexString(SHA256.HashData(wrongBytes));
        var unknownHash = Convert.ToHexString(SHA256.HashData(unknownBytes));
        _output.WriteLine($"PROBE 401 wrong-password sha256={wrongHash} len={wrongBytes.Length}");
        _output.WriteLine($"PROBE 401 unknown-handle  sha256={unknownHash} len={unknownBytes.Length}");
        Assert.True(wrongBytes.AsSpan().SequenceEqual(unknownBytes), "the two 401 bodies differ byte-wise");
        Assert.Equal(wrongHash, unknownHash);
        Assert.Equal(
            wrongPassword.Content.Headers.ContentType?.ToString(),
            unknownHandle.Content.Headers.ContentType?.ToString());

        var text = Encoding.UTF8.GetString(wrongBytes);
        Assert.Contains("not_authenticated", text, StringComparison.Ordinal);
        Assert.Contains("credenciais inválidas", text, StringComparison.Ordinal);
        // the traceId is omitted only here (adjudication): a per-request id would break equality
        Assert.DoesNotContain("traceId", text, StringComparison.Ordinal);
        // neither answer enumerates the account or echoes the password
        Assert.DoesNotContain(handle, text, StringComparison.Ordinal);
        Assert.DoesNotContain(unknownHandleText, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);

        // the dummy hash is consulted on the real code path — a login nobody has creates nothing
        Assert.Null(await AuthDb.ReadAsync(
            _host.StoragePath, SqliteAuthStore.StoreName, GrainId.Create("account", unknownHandleText).ToString()));
        var endpoints = File.ReadAllText(
            Path.Combine(BackendRoot.ProjectDirectory("Squid.Api"), "Auth", "AuthEndpoints.cs"));
        Assert.Contains(
            "account?.PasswordHash ?? PasswordHasher.DummyHash", endpoints, StringComparison.Ordinal);
        _output.WriteLine("PROBE dummy-hash = PasswordHasher.Verify(password, account?.PasswordHash ?? PasswordHasher.DummyHash)");
    }

    [Fact] // AC-04
    public async Task Ac04_csrf_refuses_a_mutation_and_register_and_login_stay_exempt()
    {
        var client = _host.NewClient();
        Assert.Equal(
            HttpStatusCode.Created,
            (await client.RegisterAsync(Unique("qa04"), Password)).StatusCode);

        // pre-authentication mutations carry no cookie to double-submit, so they must NOT be 403
        var loginWithoutCsrf = await _host.NewClient().LoginAsync(Unique("qa04nobody"), Password);
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithoutCsrf.StatusCode);
        var registerWithoutCsrf = await _host.NewClient().RegisterAsync(Unique("qa04reg"), Password);
        Assert.Equal(HttpStatusCode.Created, registerWithoutCsrf.StatusCode);

        // absent token → 403 with the CSRF code
        var missing = await client.PostAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        var missingBody = await missing.Content.ReadAsStringAsync();
        Assert.Contains("csrf_failed", missingBody, StringComparison.Ordinal);
        Assert.Contains("csrf", missingBody, StringComparison.OrdinalIgnoreCase);

        // divergent token → 403 (the cookie the jar holds is still the right one)
        var divergent = await client.PostAsync(
            "/api/auth/logout", configure: request =>
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, new string('0', 32)));
        Assert.Equal(HttpStatusCode.Forbidden, divergent.StatusCode);

        // the same rule covers logout-all
        var logoutAllWithoutCsrf = await client.PostAsync("/api/auth/logout-all");
        Assert.Equal(HttpStatusCode.Forbidden, logoutAllWithoutCsrf.StatusCode);

        // the refusals did not revoke anything: the right token still executes the mutation
        var accepted = await client.PostAsync(
            "/api/auth/logout", configure: request =>
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, client.Csrf));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        _output.WriteLine($"PROBE csrf header {AuthSessions.CsrfHeader}: absent=403 divergent=403 matching=200");
    }

    [Fact] // AC-05
    public async Task Ac05_logout_and_logout_all_revoke_and_the_row_carries_only_the_phc_hash()
    {
        var handle = Unique("qa05");
        var first = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await first.RegisterAsync(handle, Password)).StatusCode);
        var firstSession = first.Session!;
        var firstCsrf = first.Csrf!;

        var second = _host.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await second.LoginAsync(handle, Password)).StatusCode);
        var secondSession = second.Session!;
        var secondCsrf = second.Csrf!;
        Assert.NotEqual(firstSession, secondSession);

        // logout-all from one jar revokes both sessions (SPEC-0007 B-07)
        var logoutAll = await PostWithCsrf(second, "/api/auth/logout-all", secondCsrf);
        Assert.Equal(HttpStatusCode.OK, logoutAll.StatusCode);
        first.Inject(AuthSessions.SessionCookie, firstSession);
        first.Inject(AuthSessions.CsrfCookie, firstCsrf);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await PostWithCsrf(first, "/api/auth/logout", firstCsrf)).StatusCode);
        second.Inject(AuthSessions.SessionCookie, secondSession);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await PostWithCsrf(second, "/api/auth/logout", secondCsrf)).StatusCode);

        // a single logout revokes the session it carries — replay of the old cookie answers 401
        var third = _host.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await third.LoginAsync(handle, Password)).StatusCode);
        var thirdSession = third.Session!;
        var thirdCsrf = third.Csrf!;
        Assert.Equal(HttpStatusCode.OK, (await PostWithCsrf(third, "/api/auth/logout", thirdCsrf)).StatusCode);

        third.Inject(AuthSessions.SessionCookie, thirdSession);
        third.Inject(AuthSessions.CsrfCookie, thirdCsrf);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await PostWithCsrf(third, "/api/auth/logout", thirdCsrf)).StatusCode);
        third.Inject(AuthSessions.SessionCookie, thirdSession);
        Assert.Equal(HttpStatusCode.Unauthorized, (await third.PostAsync("/api/auth/logout")).StatusCode);

        // PII: the persisted credential document is the Argon2id PHC hash and nothing else.
        // The row is read twice on purpose — raw, as SQLite holds it, and decoded, the way the
        // product deserializes it: System.Text.Json escapes '+' inside base64 as \u002B, so a
        // raw-only assertion of base64 material only holds when the random salt has no '+'.
        var row = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteAuthStore.StoreName, GrainId.Create("account", handle).ToString());
        Assert.NotNull(row);
        var decodedRow = DecodeJsonStrings(row!);
        Assert.Contains("$argon2id$v=19$m=4096,t=3,p=1$", row, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(row!, "$argon2id$")); // exactly one password field, already hashed
        var passwordBytes = Encoding.UTF8.GetBytes(Password);
        foreach (var text in new[] { row!, decodedRow })
        {
            Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(passwordBytes), text, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToHexStringLower(passwordBytes), text, StringComparison.OrdinalIgnoreCase);
        }

        // the product's own read path: deserialize the row, then check what the account carries
        var account = JsonSerializer.Deserialize<AuthAccount>(row!);
        Assert.NotNull(account);
        var phc = account!.PasswordHash;
        Assert.StartsWith("$argon2id$v=19$m=4096,t=3,p=1$", phc, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify(Password, phc), "the stored PHC does not verify this password");
        Assert.False(PasswordHasher.Verify("qa0013-senha-diferente-000", phc));
        Assert.DoesNotContain(Password, phc, StringComparison.Ordinal);
        _output.WriteLine($"PROBE persisted-password = {phc[..Math.Min(phc.Length, 45)]}… (PHC, plaintext absent)");

        var actor = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteActorEventStore.StoreName, GrainId.Create("actor", handle).ToString());
        Assert.NotNull(actor);
        Assert.DoesNotContain(Password, actor, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexStringLower(passwordBytes), actor!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // extra facet QA-0012 deferred to TASK-0014 (FR-004, B-03)
    public async Task Extra_the_clear_root_key_and_the_password_never_appear_in_any_auth_response()
    {
        var handle = Unique("qa06");
        var client = _host.NewClient();
        var register = await client.RegisterAsync(handle, Password);
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["register-201"] = await register.Content.ReadAsStringAsync(),
        };

        // open the kit this run produced: the clear RK is what must be absent everywhere else
        using (var kitJson = JsonDocument.Parse(bodies["register-201"]))
        {
            var encrypted = kitJson.RootElement.GetProperty("kit").GetProperty("encryptedRootKey").GetString()!;
            var rootKey = RootKeyCipher.Decrypt(Convert.FromBase64String(encrypted), Password);
            var rootKeyBytes = rootKey.AsSpan().ToArray();
            Assert.Equal(32, rootKeyBytes.Length); // SPEC-0007 B-02: RK de 32 bytes

            var login = await _host.NewClient().LoginAsync(handle, Password);
            bodies["login-200"] = await login.Content.ReadAsStringAsync();
            bodies["login-401-wrong-password"] = await (
                await _host.NewClient().LoginAsync(handle, "qa0013-senha-diferente-000")).Content.ReadAsStringAsync();
            bodies["login-401-unknown-handle"] = await (
                await _host.NewClient().LoginAsync(Unique("qa06none"), Password)).Content.ReadAsStringAsync();
            bodies["logout-403-no-csrf"] = await (await client.PostAsync("/api/auth/logout")).Content.ReadAsStringAsync();
            bodies["register-409-duplicate"] = await (await client.RegisterAsync(handle, Password)).Content.ReadAsStringAsync();
            bodies["register-422-weak"] = await (await client.RegisterAsync(Unique("qa06weak"), "curta1234")).Content.ReadAsStringAsync();

            var forms = new[]
            {
                Convert.ToBase64String(rootKeyBytes),
                Convert.ToHexStringLower(rootKeyBytes),
                Convert.ToHexString(rootKeyBytes),
            };

            foreach (var pair in bodies)
            {
                // raw body AND unescaped string values: base64 containing '+' is written as \u002B,
                // so a leak could hide from a raw-only scan — neither view is allowed to hide it.
                foreach (var text in new[] { pair.Value, DecodeJsonStrings(pair.Value) })
                {
                    Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
                    foreach (var form in forms)
                    {
                        Assert.DoesNotContain(form, text, StringComparison.OrdinalIgnoreCase);
                    }
                }

                _output.WriteLine($"PROBE {pair.Key}: {Truncate(pair.Value)}");
            }

            // The log half of AC-05 runs after this suite: qa-check-task-0014 greps every
            // docs/sdd/runs/test-runs log for the forms of every marker this session wrote,
            // then deletes the markers — one per executed run, so no run escapes the grep.
            var marker = MarkerPath();
            File.WriteAllLines(marker, forms);
            _output.WriteLine($"PROBE rk-marker = {marker} ({forms.Length} forms)");
        }
    }

    private static Task<HttpResponseMessage> PostWithCsrf(AuthClient client, string path, string? token) =>
        client.PostAsync(path, configure: request =>
        {
            if (token is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, token);
            }
        });

    /// <summary>Where this run writes the RK forms for the checker's log grep (deleted by it).</summary>
    private static string MarkerPath() =>
        Path.Combine(Path.GetTempPath(), $"qa0013-rk-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt");

    /// <summary>
    /// Every JSON string value of <paramref name="text"/>, unescaped and concatenated. Searching
    /// raw JSON is not enough: System.Text.Json writes <c>+</c> as <c>\u002B</c>, so base64
    /// material in a response or in a stored row is only findable after decoding.
    /// </summary>
    private static string DecodeJsonStrings(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(text);
            var builder = new StringBuilder();
            Collect(document.RootElement, builder);
            return builder.ToString();
        }
        catch (JsonException)
        {
            return text; // a body that is not JSON is already literal
        }
    }

    private static void Collect(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Collect(property.Value, builder);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, builder);
                break;
            case JsonValueKind.String:
                builder.Append(element.GetString()).Append(' ');
                break;
        }
    }

    private static DateTimeOffset CookieExpiry(string setCookie)
    {
        var marker = setCookie.IndexOf("expires=", StringComparison.OrdinalIgnoreCase);
        Assert.True(marker >= 0, "cookie carries no expires= attribute: " + setCookie);
        var start = setCookie.IndexOf('=', marker) + 1;
        var end = setCookie.IndexOf(';', start);
        var raw = (end >= 0 ? setCookie[start..end] : setCookie[start..]).Trim();
        Assert.True(
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var value),
            "unparseable cookie expiry: " + raw);
        return value;
    }

    private static string Truncate(string text) =>
        text.Length <= 220 ? text : text[..220] + "…";

    private static string Unique(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    private static int Occurrences(string text, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }
}
