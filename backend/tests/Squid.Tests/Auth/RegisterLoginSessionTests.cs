using System.Net;
using System.Text.Json;
using Orleans;
using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Grains.Actors;
using Squid.Service;
using Squid.Tests.Architecture;
using System.Text.RegularExpressions;

namespace Squid.Tests.Auth;

/// <summary>
/// AC-01..AC-05 of TASK-0014, proved over HTTP against the host <see cref="SquidServiceHost"/>
/// builds: registration with its cookies and kit, the refusals that must persist nothing, the one
/// 401 login answers, the CSRF double submission, revocation, and the PII rule of FR-002/FR-004.
/// </summary>
[Collection("ServiceHost")]
public sealed class RegisterLoginSessionTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public RegisterLoginSessionTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Register_answers_with_the_session_cookies_and_the_kit_exactly_once()
    {
        var handle = Handle("ac01");
        var client = _host.NewClient();

        var response = await client.RegisterAsync(handle, Password, displayName: "Alice");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var sessionCookie = AuthClient.SetCookie(response, AuthSessions.SessionCookie);
        Assert.NotNull(sessionCookie);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", sessionCookie, StringComparison.OrdinalIgnoreCase); // the 30-day window, not a browser session

        var csrfCookie = AuthClient.SetCookie(response, AuthSessions.CsrfCookie);
        Assert.NotNull(csrfCookie);
        // Double submission needs a cookie the page script can read (R-09), and it is Secure too.
        Assert.DoesNotContain("httponly", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", csrfCookie, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(handle, json.RootElement.GetProperty("handle").GetString());

        // "o kit aparece na resposta uma vez": one kit key, and only there.
        Assert.Equal(1, Occurrences(body, "\"kit\""));
        var kit = json.RootElement.GetProperty("kit");
        Assert.Equal(Mnemonic.WordCount, kit.GetProperty("mnemonic").GetArrayLength());
        Assert.StartsWith("RC-", kit.GetProperty("recoveryCredential").GetString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(kit.GetProperty("encryptedRootKey").GetString()));

        // The actor itself exists: one row in the event-sourced store, keyed by the handle.
        var actor = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteActorEventStore.StoreName, GrainId.Create("actor", handle).ToString());
        Assert.NotNull(actor);
    }

    [Fact] // AC-02
    public async Task A_refused_registration_answers_clearly_and_persists_nothing()
    {
        var actorsBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteActorEventStore.StoreName);
        var credentialsBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteAuthStore.StoreName);

        // Duplicated handle: the answer is 409 with a clear message, from a client that already
        // holds a valid session and sends no CSRF header — register/login are pre-authentication.
        var handle = Handle("ac02");
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);

        var duplicate = await client.RegisterAsync(handle, Password);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var duplicateBody = await duplicate.Content.ReadAsStringAsync();
        Assert.Contains(handle, duplicateBody, StringComparison.Ordinal);
        Assert.Contains("em uso", duplicateBody, StringComparison.Ordinal);

        // Malformed handle: 422 citing the rule verbatim.
        var malformed = await client.PostAsync(
            "/api/auth/register", new { handle = "Ab!", password = Password });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, malformed.StatusCode);
        Assert.Contains(ActorHandle.Rule, await malformed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Password below the minimum: 422 citing the number.
        var weak = await client.RegisterAsync(Handle("ac02weak"), "curta1234");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, weak.StatusCode);
        Assert.Contains(
            PasswordHasher.MinLength.ToString(),
            await weak.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // Beyond the account the single valid registration created, nothing was written.
        Assert.Equal(actorsBefore + 1, await AuthDb.CountAsync(_host.StoragePath, SqliteActorEventStore.StoreName));
        Assert.Equal(
            credentialsBefore + 1, await AuthDb.CountAsync(_host.StoragePath, SqliteAuthStore.StoreName));
    }

    [Fact] // AC-03
    public async Task Login_answers_one_401_for_a_wrong_password_and_for_an_unknown_handle()
    {
        var handle = Handle("ac03");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, Password)).StatusCode);

        var accepted = _host.NewClient();
        var ok = await accepted.LoginAsync(handle, Password);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.NotNull(AuthClient.SetCookie(ok, AuthSessions.SessionCookie));
        Assert.NotNull(AuthClient.SetCookie(ok, AuthSessions.CsrfCookie));

        var wrongPassword = await _host.NewClient().LoginAsync(handle, "senha-diferente-99");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        var wrongPasswordBody = await wrongPassword.Content.ReadAsStringAsync();

        var unknownHandle = await _host.NewClient().LoginAsync(Handle("ac03none"), Password);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownHandle.StatusCode);
        var unknownHandleBody = await unknownHandle.Content.ReadAsStringAsync();

        // Same body, byte for byte: neither answer tells the caller which half was wrong.
        Assert.Equal(wrongPasswordBody, unknownHandleBody);
        Assert.DoesNotContain(Password, wrongPasswordBody, StringComparison.Ordinal);
    }

    [Fact] // AC-04
    public async Task A_mutation_is_403_without_the_csrf_token_and_runs_with_it()
    {
        var client = _host.NewClient();
        Assert.Equal(
            HttpStatusCode.Created,
            (await client.RegisterAsync(Handle("ac04"), Password)).StatusCode);

        var missing = await client.PostAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Contains(
            "csrf", await missing.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var divergent = await WithCsrf(client, "/api/auth/logout", "0000000000000000");
        Assert.Equal(HttpStatusCode.Forbidden, divergent.StatusCode);

        var accepted = await WithCsrf(client, "/api/auth/logout", client.Csrf);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact] // AC-05 (logout)
    public async Task Logout_invalidates_the_session_and_the_old_cookie_stops_working()
    {
        var client = _host.NewClient();
        Assert.Equal(
            HttpStatusCode.Created,
            (await client.RegisterAsync(Handle("ac05out"), Password)).StatusCode);

        var revokedSession = client.Session!;
        var revokedCsrf = client.Csrf!;

        var logout = await WithCsrf(client, "/api/auth/logout", revokedCsrf);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        // The very same cookies the browser would still hold: the row is the revocation.
        client.Inject(AuthSessions.SessionCookie, revokedSession);
        client.Inject(AuthSessions.CsrfCookie, revokedCsrf);

        var withToken = await WithCsrf(client, "/api/auth/logout", revokedCsrf);
        Assert.Equal(HttpStatusCode.Unauthorized, withToken.StatusCode);

        client.Inject(AuthSessions.SessionCookie, revokedSession);
        var withoutToken = await client.PostAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
    }

    [Fact] // AC-05 (logout-all)
    public async Task Logout_all_invalidates_every_session_of_the_actor()
    {
        var handle = Handle("ac05all");
        var first = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await first.RegisterAsync(handle, Password)).StatusCode);
        var firstSession = first.Session!;

        var second = _host.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await second.LoginAsync(handle, Password)).StatusCode);
        var secondSession = second.Session!;
        Assert.NotEqual(firstSession, secondSession);

        var logoutAll = await WithCsrf(second, "/api/auth/logout-all", second.Csrf);
        Assert.Equal(HttpStatusCode.OK, logoutAll.StatusCode);

        // Both cookies, from both jars: neither session survives the call.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await WithCsrf(first, "/api/auth/logout", first.Csrf)).StatusCode);

        second.Inject(AuthSessions.SessionCookie, secondSession);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await WithCsrf(second, "/api/auth/logout", second.Csrf)).StatusCode);
    }

    [Fact] // AC-05 (PII: FR-002, FR-004, §4.7)
    public async Task The_password_and_the_root_key_reach_neither_a_response_nor_a_row()
    {
        var handle = Handle("ac05pii");
        var register = await _host.NewClient().RegisterAsync(handle, Password);
        var bodies = new List<string> { await register.Content.ReadAsStringAsync() };

        var login = await _host.NewClient().LoginAsync(handle, Password);
        bodies.Add(await login.Content.ReadAsStringAsync());

        foreach (var body in bodies)
        {
            Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
        }

        var account = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteAuthStore.StoreName, GrainId.Create("account", handle).ToString());
        Assert.NotNull(account);
        Assert.Contains("argon2id", account, StringComparison.Ordinal); // the password, as a hash
        Assert.DoesNotContain(Password, account, StringComparison.Ordinal);

        var actor = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteActorEventStore.StoreName, GrainId.Create("actor", handle).ToString());
        Assert.NotNull(actor);
        Assert.DoesNotContain(Password, actor, StringComparison.Ordinal);

        // The kit carries the RK sealed: open it with the password and prove its clear bytes are
        // nowhere — the runtime half of the assertion QA-0012 left to this task.
        using var json = JsonDocument.Parse(bodies[0]);
        var encrypted = json.RootElement.GetProperty("kit").GetProperty("encryptedRootKey").GetString()!;
        var rootKey = RootKeyCipher.Decrypt(Convert.FromBase64String(encrypted), Password);
        var clearForms = new[]
        {
            Convert.ToBase64String(rootKey.AsSpan()),
            Convert.ToHexStringLower(rootKey.AsSpan()),
        };

        foreach (var body in bodies)
        {
            foreach (var clear in clearForms)
            {
                Assert.DoesNotContain(clear, body, StringComparison.OrdinalIgnoreCase);
            }
        }

        foreach (var clear in clearForms)
        {
            Assert.DoesNotContain(clear, account, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(clear, actor, StringComparison.OrdinalIgnoreCase);
        }

        // The grep of AC-05, executable: no log statement of the auth path names the password.
        Assert.Empty(LogStatementsAboutThePassword());
    }

    private static Task<HttpResponseMessage> WithCsrf(AuthClient client, string path, string? token) =>
        client.PostAsync(path, configure: request =>
        {
            if (token is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, token);
            }
        });

    private static IEnumerable<string> LogStatementsAboutThePassword()
    {
        var statement = new Regex(
            @"Log(?:Information|Warning|Error|Critical|Debug|Trace|Fatal)\s*\(",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        foreach (var project in new[] { "Squid.Api", "Squid.Service" })
        {
            var directory = BackendRoot.ProjectDirectory(project);
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                foreach (var line in File.ReadLines(file))
                {
                    if (statement.IsMatch(line)
                        && (line.Contains("password", StringComparison.OrdinalIgnoreCase)
                            || line.Contains("senha", StringComparison.OrdinalIgnoreCase)))
                    {
                        yield return $"{file}: {line.Trim()}";
                    }
                }
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
