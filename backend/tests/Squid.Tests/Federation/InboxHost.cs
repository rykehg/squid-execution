using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Federation;
using Squid.Api.Posts;
using Squid.Protocols.ActivityPub;
using Squid.ReadModels.Projections;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Federation;

/// <summary>
/// The host of the TASK-0044 suites: <see cref="SquidServiceHost"/> with the two inbox seams
/// replaced through the GAP-17 configure — the remote actor client that resolves a foreign
/// <c>keyId</c> and sender (§3 steps 1/7) and the media download of GAP-44 (AC-07) — so every
/// production rule runs against fakes that never open a socket or resolve a name. The quota starts
/// high (10 000/min) so only the AC-04 suite ever meets the 429; that suite builds its own host
/// with <c>rateLimit: 2</c> instead of poisoning this one.
/// </summary>
public sealed class InboxHostFixture : IAsyncDisposable
{
    /// <summary>The application under test — the same code <c>Program</c> runs.</summary>
    public WebApplication App { get; }

    /// <summary>Base client the per-test cookie jars share.</summary>
    public HttpClient Http { get; }

    /// <summary>SQLite file behind the host, so a test can read the rows themselves.</summary>
    public string StoragePath { get; }

    /// <summary>The fake that answers foreign key material and senders.</summary>
    public FakeActorClient Actors { get; } = new();

    /// <summary>The fake that answers remote attachment bytes.</summary>
    public FakeMediaClient Media { get; } = new();

    /// <summary>Starts the host on <c>http://127.0.0.1:0</c> with the inbox fakes wired in.</summary>
    public InboxHostFixture()
        : this(10_000)
    {
    }

    /// <summary>Starts the host with an explicit per-domain quota (AC-04 builds its own at 2) — internal so the class fixture keeps the single public constructor xUnit requires.</summary>
    internal InboxHostFixture(int rateLimit)
    {
        StoragePath = Path.Combine(Path.GetTempPath(), $"squid-inbox-{Guid.NewGuid():N}.db");
        App = SquidServiceHost.Build([
            "--Storage:Path=" + StoragePath,
            "--Logging:LogLevel:Default=Warning",
            "--Federation:RateLimitPerMinute=" + rateLimit,
        ], builder =>
        {
            // The seam runs after every production registration — last one wins, so the real
            // pipeline resolves the fakes and nothing here can reach the network (AC-01, AC-07).
            builder.Services.AddSingleton<IFederatedActorClient>(Actors);
            builder.Services.AddSingleton<IRemoteMediaClient>(Media);
        });
        App.Urls.Add("http://127.0.0.1:0");
        App.StartAsync().GetAwaiter().GetResult();

        Http = new HttpClient { BaseAddress = new Uri(App.Urls.Single()) };
    }

    /// <summary>A fresh jar: cookies are never shared between tests.</summary>
    public AuthClient NewClient() => new(Http);

    /// <summary>The origin the host answers on — the same rule the documents themselves publish.</summary>
    public string Origin => $"{Http.BaseAddress!.Scheme}://{Http.BaseAddress.Authority}";

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
/// The <c>IFederatedActorClient</c> of the tests: an actor it <see cref="Publish"/>es answers its
/// key and its existence, everything else answers nothing — the production fetch, its SSRF
/// perimeter and its TTL stay unit-tested on their own, and no suite waits on DNS.
/// </summary>
public sealed class FakeActorClient : IFederatedActorClient
{
    private readonly ConcurrentDictionary<string, byte[]> _keys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _actors = new(StringComparer.Ordinal);

    /// <summary>Registers <paramref name="actorUri"/> with <paramref name="rsa"/>'s SPKI; returns the <c>#main-key</c> keyId to sign with.</summary>
    public string Publish(string actorUri, RSA rsa)
    {
        _actors[actorUri] = true;
        var keyId = actorUri + "#main-key";
        _keys[keyId] = rsa.ExportSubjectPublicKeyInfo();
        return keyId;
    }

    /// <summary>Makes <paramref name="actorUri"/> resolvable without publishing a key (step 7 alone).</summary>
    public void Acknowledge(string actorUri) => _actors[actorUri] = true;

    /// <inheritdoc />
    public Task<FederatedKey?> ResolveKeyAsync(string keyId, CancellationToken cancellationToken) =>
        Task.FromResult(_keys.TryGetValue(keyId, out var spki)
            ? new FederatedKey(spki, null)
            : null);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string actorUri, CancellationToken cancellationToken) =>
        Task.FromResult(_actors.ContainsKey(actorUri));
}

/// <summary>The <c>IRemoteMediaClient</c> of the tests: bytes only for URLs a test served, null otherwise (GAP-44's failure half).</summary>
public sealed class FakeMediaClient : IRemoteMediaClient
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    /// <summary>Every URL the pipeline asked for, in order — the "never fetched" proofs count this.</summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    /// <summary>Serves <paramref name="bytes"/> for <paramref name="url"/>.</summary>
    public void Serve(string url, byte[] bytes) => _blobs[url] = bytes;

    /// <inheritdoc />
    public Task<byte[]?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        Requests.Enqueue(url);
        return Task.FromResult(_blobs.TryGetValue(url, out var bytes) ? bytes : null);
    }
}

/// <summary>House helpers of the TASK-0044 suites: signing, setup and the projection step the pump would do.</summary>
internal static class InboxHarness
{
    public const string ActivityJson = "application/activity+json";

    public const string ActivityStreams = "https://www.w3.org/ns/activitystreams";

    public const string Public = "https://www.w3.org/ns/activitystreams#Public";

    /// <summary>A unique id inside the test's prefix, so no window or row collides between facts.</summary>
    public static string Unique(string prefix) =>
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    /// <summary>Registers an owner and creates the community container every scenario lands its posts in.</summary>
    public static async Task<(AuthClient Owner, string Handle, string Slug)> CommunityAsync(
        InboxHostFixture host, string prefix)
    {
        var handle = PostRoutes.Handle(prefix);
        var owner = host.NewClient();
        Assert.Equal(System.Net.HttpStatusCode.Created,
            (await owner.RegisterAsync(handle, PostRoutes.Password)).StatusCode);

        var slug = PostRoutes.Slug(prefix);
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Comunidade", slug, summary = "inbox de teste" }, owner.Csrf);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        return (owner, handle, slug);
    }

    /// <summary>A remote actor the fake resolver knows: its URI, the keyId and the key to sign with.</summary>
    public static (string Actor, string KeyId, RSA Rsa) Remote(InboxHostFixture host, string name)
    {
        var actor = $"https://remote.example/users/{name}";
        var rsa = RSA.Create(2048);
        return (actor, host.Actors.Publish(actor, rsa), rsa);
    }

    /// <summary>
    /// One draft-cavage inbox delivery of contract §5: signature over (request-target) host date
    /// digest, Digest of the signed body, Date from the clock the test chose. <paramref name="sentBody"/>
    /// defaults to the signed one — pointing it elsewhere is exactly how a divergent Digest is built.
    /// </summary>
    public static Task<HttpResponseMessage> SendSignedAsync(
        HttpClient http, string inbox, string body, RSA rsa, string keyId,
        string? sentBody = null, DateTimeOffset? at = null)
    {
        var signed = HttpSignatureSigner.Sign(
            new SignRequest(
                "post", inbox, http.BaseAddress!.Authority, keyId,
                Encoding.UTF8.GetBytes(body), at ?? DateTimeOffset.UtcNow),
            signingString => rsa.SignData(signingString, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        var request = new HttpRequestMessage(HttpMethod.Post, inbox)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(sentBody ?? body)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ActivityJson);
        request.Headers.TryAddWithoutValidation("Signature", signed.Signature);
        request.Headers.TryAddWithoutValidation("Digest", signed.Digest);
        request.Headers.TryAddWithoutValidation("Date", signed.Date);
        return http.SendAsync(request);
    }

    /// <summary>An unsigned delivery — §3 step 1 with nothing to verify.</summary>
    public static Task<HttpResponseMessage> SendUnsignedAsync(HttpClient http, string inbox, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, inbox)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ActivityJson);
        return http.SendAsync(request);
    }

    /// <summary>Projects every event past the projections' high-water — the Worker pump's tick, by hand (R-18).</summary>
    public static async Task ProjectPendingAsync(InboxHostFixture host)
    {
        var projector = host.App.Services.GetRequiredService<IActivityProjector>();
        var log = host.App.Services.GetRequiredService<SqliteActivityLog>();
        var pending = await log.ReadAsync(await projector.LastProjectedSeqAsync());
        foreach (var entry in pending)
        {
            await projector.ProjectAsync(entry);
        }
    }

    /// <summary>How many events the append-only log holds — the "nothing was reprocessed" proof.</summary>
    public static async Task<int> LogCountAsync(InboxHostFixture host)
    {
        var log = host.App.Services.GetRequiredService<SqliteActivityLog>();
        return (await log.ReadAsync(0)).Count;
    }
}
