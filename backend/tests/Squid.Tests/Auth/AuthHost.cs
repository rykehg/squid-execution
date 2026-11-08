using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Squid.Api.Auth;
using Squid.Service;

namespace Squid.Tests.Auth;

/// <summary>
/// The host of the auth suite: one <see cref="SquidServiceHost"/> on a dynamic port with its own
/// temp storage file, started once for the class. Hosts never overlap, which is why the class
/// belongs to the <c>ServiceHost</c> collection — localhost clustering fixes the silo ports.
/// </summary>
public sealed class AuthHostFixture : IAsyncDisposable
{
    /// <summary>The application under test — the same code <c>Program</c> runs.</summary>
    public WebApplication App { get; }

    /// <summary>Base client the per-test cookie jars share.</summary>
    public HttpClient Http { get; }

    /// <summary>SQLite file behind the host, so a test can read the rows themselves.</summary>
    public string StoragePath { get; }

    /// <summary>Starts the host on <c>http://127.0.0.1:0</c>.</summary>
    public AuthHostFixture()
    {
        StoragePath = Path.Combine(Path.GetTempPath(), $"squid-auth-{Guid.NewGuid():N}.db");
        App = SquidServiceHost.Build([
            "--Storage:Path=" + StoragePath,
            "--Logging:LogLevel:Default=Warning",
        ]);
        App.Urls.Add("http://127.0.0.1:0");
        App.StartAsync().GetAwaiter().GetResult();

        Http = new HttpClient { BaseAddress = new Uri(App.Urls.Single()) };
    }

    /// <summary>A fresh jar: cookies are never shared between tests.</summary>
    public AuthClient NewClient() => new(Http);

    /// <inheritdoc />
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
            // A leftover temp file is not a product failure; the OS cleans it up.
        }
    }
}

/// <summary>
/// An HTTP client with an explicit cookie jar. It never lets the handler carry cookies: the
/// session cookie is <c>Secure</c> and a Secure cookie does not ride plain http, so a test that
/// wanted the automatic jar would silently send nothing back.
/// </summary>
public sealed class AuthClient
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    /// <summary>Shares the base address of <paramref name="http"/>.</summary>
    public AuthClient(HttpClient http) => _http = http;

    /// <summary>The session cookie value this jar would send.</summary>
    public string? Session => _cookies.GetValueOrDefault(AuthSessions.SessionCookie);

    /// <summary>The csrf cookie value this jar would send.</summary>
    public string? Csrf => _cookies.GetValueOrDefault(AuthSessions.CsrfCookie);

    /// <summary>Puts a value back into the jar — how a test replays a cookie the server revoked.</summary>
    public void Inject(string name, string value) => _cookies[name] = value;

    /// <summary>Sends the auth body with the cookies this jar holds.</summary>
    public Task<HttpResponseMessage> PostAsync(
        string path, object? body = null, Action<HttpRequestMessage>? configure = null) =>
        SendAsync(HttpMethod.Post, path, body, configure);

    /// <summary>Registers a handle; the response carries the session cookies of the new account.</summary>
    public Task<HttpResponseMessage> RegisterAsync(string handle, string password, string? displayName = null) =>
        PostAsync("/api/auth/register", new { handle, password, displayName });

    /// <summary>Logs in with handle and password.</summary>
    public Task<HttpResponseMessage> LoginAsync(string handle, string password) =>
        PostAsync("/api/auth/login", new { handle, password });

    /// <summary>Sends the request, then absorbs every Set-Cookie the response carries.</summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body = null, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        if (_cookies.Count > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie", string.Join("; ", _cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        configure?.Invoke(request);

        var response = await _http.SendAsync(request);
        Remember(response);
        return response;
    }

    /// <summary>The raw <c>Set-Cookie</c> of <paramref name="name"/>, attributes included (AC-01).</summary>
    public static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    private void Remember(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;

        foreach (var value in values)
        {
            var pair = value.Split(';', 2, StringSplitOptions.TrimEntries)[0];
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;

            var name = pair[..separator];
            var cookie = pair[(separator + 1)..];
            if (cookie.Length == 0) _cookies.Remove(name);
            else _cookies[name] = cookie;
        }
    }
}

/// <summary>Reads the rows of the SQLite grain storage, which is how "nothing was persisted" is proven.</summary>
internal static class AuthDb
{
    /// <summary>Rows of <paramref name="storeName"/> in the host's database.</summary>
    public static async Task<int> CountAsync(string storagePath, string storeName)
    {
        await using var connection = Open(storagePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM GrainState WHERE StoreName = @StoreName";
        command.Parameters.AddWithValue("@StoreName", storeName);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>The State column of one row — the document exactly as it is persisted.</summary>
    public static async Task<string?> ReadAsync(string storagePath, string storeName, string grainId)
    {
        await using var connection = Open(storagePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT State FROM GrainState WHERE StoreName = @StoreName AND GrainId = @GrainId";
        command.Parameters.AddWithValue("@StoreName", storeName);
        command.Parameters.AddWithValue("@GrainId", grainId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private static SqliteConnection Open(string storagePath)
    {
        var connection = new SqliteConnection($"Data Source={storagePath};Pooling=False");
        connection.Open();
        return connection;
    }
}
