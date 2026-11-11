using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Auth;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Stream;

/// <summary>
/// The host of the TASK-0026 suites: <see cref="SquidServiceHost"/> with its own temp storage and
/// the heartbeat configured down to a fraction of a second (<c>Stream:PingSeconds</c> — the gap
/// row that keeps the tests from sleeping 15 s), started once per test class. Hosts never overlap,
/// which is why every suite belongs to the <c>ServiceHost</c> collection (localhost clustering
/// fixes the silo ports).
/// </summary>
public sealed class StreamHostFixture : IAsyncDisposable
{
    /// <summary>The application under test — the same code <c>Program</c> runs.</summary>
    public WebApplication App { get; }

    /// <summary>Base client the per-test cookie jars share.</summary>
    public HttpClient Http { get; }

    /// <summary>SQLite file behind the host, so a test can read the rows themselves.</summary>
    public string StoragePath { get; }

    /// <summary>Starts the host on <c>http://127.0.0.1:0</c> with a 0.2 s heartbeat.</summary>
    public StreamHostFixture()
    {
        StoragePath = Path.Combine(Path.GetTempPath(), $"squid-stream-{Guid.NewGuid():N}.db");
        App = SquidServiceHost.Build([
            "--Storage:Path=" + StoragePath,
            "--Logging:LogLevel:Default=Warning",
            "--Stream:PingSeconds=0.2",
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

/// <summary>One parsed SSE frame: the event name, the id (only a <c>post</c> frame carries one) and the data.</summary>
public sealed record SseFrame(string? Event, string? Id, string? Data);

/// <summary>
/// One open <c>GET /api/stream</c> connection read frame by frame, the way a client reads it.
/// A frame is the block between blank lines; a read that times out ends the connection (the next
/// read answers null) so a test can never race an abandoned line read against a fresh one.
/// </summary>
public sealed class SseConnection : IDisposable
{
    private readonly HttpResponseMessage _response;

    private readonly StreamReader _reader;

    private bool _ended;

    private SseConnection(HttpResponseMessage response, StreamReader reader)
    {
        _response = response;
        _reader = reader;
    }

    /// <summary>The status the handshake answered — 200 for a stream, 401 without a session.</summary>
    public HttpStatusCode Status => _response.StatusCode;

    /// <summary>The media type the handshake answered with — a refusal never says <c>event-stream</c>.</summary>
    public string? ContentType => _response.Content.Headers.ContentType?.MediaType;

    /// <summary>Opens the stream with this jar's session cookie (and the reconnect header when given).</summary>
    public static async Task<SseConnection> OpenAsync(
        StreamHostFixture host, AuthClient? who = null, string? lastEventId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/stream");
        if (who?.Session is { Length: > 0 } cookie)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{AuthSessions.SessionCookie}={cookie}");
        }

        if (lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        var response = await host.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStreamAsync();
        return new SseConnection(response, new StreamReader(body, Encoding.UTF8));
    }

    /// <summary>The whole (non-streamed) answer — the 401 body of AC-05.</summary>
    public async Task<string> ReadBodyAsync()
    {
        _ended = true;
        return await _reader.ReadToEndAsync();
    }

    /// <summary>One frame, or null when the timeout expires or the stream ends.</summary>
    public async Task<SseFrame?> ReadFrameAsync(TimeSpan timeout)
    {
        if (_ended) return null;

        string? eventName = null, id = null, data = null;
        var sawField = false;
        while (true)
        {
            string? line;
            try
            {
                line = await _reader.ReadLineAsync().WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                _ended = true; // no frame in the window: the connection is done for this test
                return null;
            }

            if (line is null)
            {
                _ended = true;
                return sawField ? new SseFrame(eventName, id, data) : null;
            }

            if (line.Length == 0)
            {
                if (!sawField) continue; // a blank line only closes a frame that has fields
                return new SseFrame(eventName, id, data);
            }

            sawField = true;
            if (line.StartsWith("event:", StringComparison.Ordinal)) eventName = line["event:".Length..].TrimStart();
            else if (line.StartsWith("id:", StringComparison.Ordinal)) id = line["id:".Length..].TrimStart();
            else if (line.StartsWith("data:", StringComparison.Ordinal)) data = line["data:".Length..].TrimStart();
        }
    }

    /// <summary>
    /// Every frame until <paramref name="match"/> shows up (inclusive) or the window closes —
    /// so "this frame never arrived" is proven by the frames that DID arrive before the match,
    /// never by a bare sleep.
    /// </summary>
    public async Task<List<SseFrame>> ReadUntilAsync(Predicate<SseFrame> match, TimeSpan timeout)
    {
        var seen = new List<SseFrame>();
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            var frame = await ReadFrameAsync(timeout - watch.Elapsed);
            if (frame is null) break;
            seen.Add(frame);
            if (match(frame)) break;
        }

        return seen;
    }

    /// <summary>Closes the connection; the server sees the abort and ends the stream.</summary>
    public void Dispose()
    {
        _reader.Dispose();
        _response.Dispose();
    }
}

/// <summary>House helpers of the TASK-0026 suites: sessions, containers, publishes and the global seq.</summary>
internal static class StreamHarness
{
    /// <summary>Registers a fresh account and returns its cookie jar with the handle.</summary>
    public static async Task<(AuthClient Client, string Handle)> SignedInAsync(StreamHostFixture host, string prefix)
    {
        var handle = PostRoutes.Handle(prefix);
        var client = host.NewClient();
        var registered = await client.RegisterAsync(handle, PostRoutes.Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return (client, handle);
    }

    /// <summary>One fresh container (all of them are born Public — SPEC-0008 A).</summary>
    public static async Task<string> NewContainerAsync(AuthClient owner, string prefix = "sse")
    {
        var slug = PostRoutes.Slug(prefix);
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = slug, slug }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return slug;
    }

    /// <summary>Publishes one text and answers the new post's canonical id.</summary>
    public static async Task<string> PublishAsync(AuthClient author, string slug, string content)
    {
        var response = await PostRoutes.SendAsync(author, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content }, author.Csrf);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Joins an open community: membership Active from the entry route (SPEC-0008 F).</summary>
    public static async Task JoinAsync(AuthClient joiner, string slug)
    {
        var joined = await PostRoutes.SendAsync(joiner, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, joiner.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");
    }

    /// <summary>Moves the visibility of a container (the PUT carries the version or it is a 409).</summary>
    public static async Task SetVisibilityAsync(AuthClient owner, string slug, string visibility)
    {
        var version = await PolicyVersionAsync(owner, slug);
        var moved = await PostRoutes.SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/policy", new { visibility }, owner.Csrf, ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
    }

    /// <summary>Bans a member (§3.3 — the moderation route of TASK-0019).</summary>
    public static async Task BanAsync(AuthClient owner, string slug, string handle)
    {
        var banned = await PostRoutes.SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{handle}",
            new { state = "ban", justification = "teste do filtro do stream" }, owner.Csrf);
        Assert.True(banned.IsSuccessStatusCode, $"ban failed: {banned.StatusCode}");
    }

    /// <summary>The policy version a PUT must answer with (If-Match → 409 otherwise).</summary>
    public static async Task<int> PolicyVersionAsync(AuthClient reader, string slug)
    {
        var response = await PostRoutes.SendAsync(reader, HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("policy").GetProperty("version").GetInt32();
    }

    /// <summary>
    /// The GLOBAL Activity.Seq of the event about one post (GAP-23) — the number the stream's
    /// <c>id</c> must carry, which the publish answer never gives (it answers the Object.Seq).
    /// </summary>
    public static async Task<long> GlobalSeqAsync(StreamHostFixture host, string postId)
    {
        var store = host.App.Services.GetRequiredService<IActivityStore>();
        var events = await store.ReadAsync();
        return events.Single(activity => activity.ObjectId == postId).Seq;
    }

    /// <summary>A content no other test can collide with — assertions match on the stream text.</summary>
    public static string Unique(string prefix) =>
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
