using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Unfurl;

/// <summary>
/// The host of the TASK-0029 suites: <see cref="SquidServiceHost"/> with the unfurl seams —
/// the <see cref="HttpMessageHandler"/> and the <see cref="IUnfurlResolver"/> — replaced by
/// fakes through the GAP-17 configure, so the production <see cref="LinkUnfurler"/> runs its
/// real guard and budget against handlers that never open a socket or resolve a name (AC-05).
/// The fixture runs small on purpose: 500 ms of timeout (AC-02) and a 4 KB body cap (AC-04).
/// </summary>
public sealed class UnfurlHostFixture : IAsyncDisposable
{
    /// <summary>The application under test — the same code <c>Program</c> runs.</summary>
    public WebApplication App { get; }

    /// <summary>Base client the per-test cookie jars share.</summary>
    public HttpClient Http { get; }

    /// <summary>SQLite file behind the host, so a test can read the rows themselves.</summary>
    public string StoragePath { get; }

    /// <summary>The fake primary handler the unfurl fetches through.</summary>
    public FakeUnfurlHandler Handler { get; } = new();

    /// <summary>The fake resolver standing in for real DNS.</summary>
    public FakeUnfurlResolver Resolver { get; } = new();

    /// <summary>Starts the host on <c>http://127.0.0.1:0</c> with the unfurl fakes wired in.</summary>
    public UnfurlHostFixture()
    {
        StoragePath = Path.Combine(Path.GetTempPath(), $"squid-unfurl-{Guid.NewGuid():N}.db");
        App = SquidServiceHost.Build([
            "--Storage:Path=" + StoragePath,
            "--Logging:LogLevel:Default=Warning",
            "--Unfurl:TimeoutMs=500",
            "--Unfurl:MaxBytes=4096",
        ], builder =>
        {
            // The GAP-17 seam runs after every production registration — last one wins, so the
            // real LinkUnfurler resolves the fakes and nothing here can reach the network.
            builder.Services.AddSingleton<HttpMessageHandler>(Handler);
            builder.Services.AddSingleton<IUnfurlResolver>(Resolver);
        });
        App.Urls.Add("http://127.0.0.1:0");
        App.StartAsync().GetAwaiter().GetResult();

        Http = new HttpClient { BaseAddress = new Uri(App.Urls.Single()) };
    }

    /// <summary>A fresh jar: cookies are never shared between tests.</summary>
    public AuthClient NewClient() => new(Http);

    /// <summary>Puts both fakes back to the state every test starts from.</summary>
    public void Reset()
    {
        Handler.Reset();
        Resolver.Reset();
    }

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

/// <summary>The primary handler of the unfurl client: records every request, answers through the test's responder.</summary>
public sealed class FakeUnfurlHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder =
        (_, _) => throw new InvalidOperationException("no responder configured for this test");

    /// <summary>Every request the unfurler made, in order — the "never called" proofs count this.</summary>
    public ConcurrentQueue<Uri> Requests { get; } = new();

    /// <summary>Sets how this test's handler answers.</summary>
    public void RespondWith(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
        _responder = responder;

    /// <summary>Clears the recorded requests and restores the throwing tripwire.</summary>
    public void Reset()
    {
        _responder = (_, _) => throw new InvalidOperationException("no responder configured for this test");
        while (Requests.TryDequeue(out _)) { }
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request.RequestUri!);
        return _responder(request, cancellationToken);
    }
}

/// <summary>The resolver seam: mapped hosts answer their addresses, everything else answers nothing (refused).</summary>
public sealed class FakeUnfurlResolver : IUnfurlResolver
{
    private readonly ConcurrentDictionary<string, IPAddress[]> _map = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The answers <paramref name="host"/> should give — every A/AAAA record of the round.</summary>
    public void Map(string host, params string[] addresses) =>
        _map[host] = addresses.Select(IPAddress.Parse).ToArray();

    /// <summary>Forgets every mapping.</summary>
    public void Reset() => _map.Clear();

    /// <inheritdoc />
    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IPAddress>>(
            _map.TryGetValue(host, out var addresses) ? addresses : []);
}

/// <summary>House helpers of the TASK-0029 suites: a signed-in owner with a fresh group to publish into.</summary>
internal static class UnfurlHarness
{
    /// <summary>Registers an account and creates a group container it owns.</summary>
    public static async Task<(AuthClient Owner, string Slug)> ContainerAsync(UnfurlHostFixture host, string prefix)
    {
        var handle = PostRoutes.Handle(prefix);
        var client = host.NewClient();
        Assert.Equal(HttpStatusCode.Created,
            (await client.RegisterAsync(handle, PostRoutes.Password)).StatusCode);

        var slug = PostRoutes.Slug("unfurl");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(client, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Unfurl", slug }, client.Csrf)).StatusCode);
        return (client, slug);
    }

    /// <summary>POST /api/containers/{slug}/posts with the content under test.</summary>
    public static Task<HttpResponseMessage> PublishAsync(AuthClient owner, string slug, string content) =>
        PostRoutes.SendAsync(owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content }, owner.Csrf);
}
