using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Squid.Api.Crypto;
using Squid.Api.Federation;
using Squid.Grains.Actors;
using Squid.Protocols.ActivityPub;
using Squid.ReadModels.Projections;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Federation;

/// <summary>
/// One silo of the TASK-0047 facet: the production <see cref="SquidServiceHost"/> application,
/// started on its own loopback port with its OWN SQLite file and blob volume — the in-process
/// instance the contract §7 chain speaks to over real HTTP.
/// </summary>
public sealed class Silo : IAsyncDisposable
{
    private bool _stopped;

    private Silo(WebApplication app, HttpClient http, string storagePath, string blobRoot)
    {
        App = app;
        Http = http;
        StoragePath = storagePath;
        BlobRoot = blobRoot;
    }

    /// <summary>The application under test — the same code <c>Program</c> runs.</summary>
    public WebApplication App { get; }

    /// <summary>Client to this silo's origin.</summary>
    public HttpClient Http { get; }

    /// <summary>The SQLite file behind the silo, so a test can prove the two bases are distinct.</summary>
    public string StoragePath { get; }

    /// <summary>The media volume behind the silo — distinct per silo, the way two instances never share a volume.</summary>
    public string BlobRoot { get; }

    /// <summary>The origin this silo answers on — scheme + Host, the rule of B-09a.</summary>
    public string Origin => $"{Http.BaseAddress!.Scheme}://{Http.BaseAddress.Authority}";

    /// <summary>The delivery motor, driven by hand: Federation:DeliveryPumpSeconds=0 keeps the hosted loop idle.</summary>
    public PeerDeliveryPump Pump => App.Services.GetRequiredService<PeerDeliveryPump>();

    /// <summary>The outbound queue rows this silo owns.</summary>
    public IPeerDeliveryStore Deliveries => App.Services.GetRequiredService<IPeerDeliveryStore>();

    /// <summary>A fresh cookie jar over this silo's origin.</summary>
    public AuthClient NewClient() => new(Http);

    /// <summary>
    /// The product's own signer for a local actor (TASK-0051's double wrap): the very activation
    /// <c>OutboundPipeline</c> runs, so a harness-signed activity carries the key the actor document publishes.
    /// </summary>
    public async Task<ActorSigner> SignerAsync(string handle)
    {
        var signer = await App.Services.GetRequiredService<ActorGrain>().ActivateSigningKeyAsync(handle);
        Assert.NotNull(signer);
        return signer!;
    }

    /// <summary>Stops the HTTP+silo host early (the §7.6 "peer down" scenario); disposal then skips the second stop.</summary>
    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        await App.StopAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
        {
            await App.StopAsync();
        }

        await App.DisposeAsync();
        Http.Dispose();
        try
        {
            File.Delete(StoragePath);
            if (Directory.Exists(BlobRoot))
            {
                Directory.Delete(BlobRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp file is not a product failure; the OS cleans it up.
        }
    }

    /// <summary>Builds and STARTS one silo: dynamic HTTP port, own storage, own blobs, trusted loopback peers, idle pump.</summary>
    public static async Task<Silo> StartAsync(string name)
    {
        var storage = Path.Combine(Path.GetTempPath(), $"squid-silo-{name}-{Guid.NewGuid():N}.db");
        var blobRoot = Path.Combine(Path.GetTempPath(), $"squid-silo-{name}-{Guid.NewGuid():N}");
        var runtimeKey = RootKey.Create();
        var (siloPort, gatewayPort) = FreePorts();
        var app = SquidServiceHost.Build(
        [
                "--Storage:Path=" + storage,
                "--Logging:LogLevel:Default=Warning",
                // §3 step 5's quota never decides a fact here — only the AC-04 suite would meet 429.
                "--Federation:RateLimitPerMinute=10000",
                // The pump stays registered but idle: every pass is the suite's own RunOnceAsync (FR-036).
                "--Federation:DeliveryPumpSeconds=0",
                // GAP-49: the exact allowlist names the local pair, the way the runbook names it.
                "--Federation:TrustedPeerHosts=127.0.0.1,localhost",
                // data-model §2.6: a NAMED blob volume per instance — two silos never share one.
                "--Media:BlobRoot=" + blobRoot,
                "--Squid:RecoveryKey=" + Convert.ToBase64String(runtimeKey.AsSpan())],
        builder =>
        {
            // localhost clustering pins silo/gateway to 11111/30000 for EVERY silo, so two started
            // hosts collide; this seam runs AFTER the production registration (last Configure wins)
            // and moves only the ports — the rest of the composition is untouched production code.
            builder.Services.Configure<EndpointOptions>(options =>
            {
                options.SiloPort = siloPort;
                options.GatewayPort = gatewayPort;
            });
        });
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        return new Silo(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }, storage, blobRoot);
    }

    /// <summary>Two loopback ports the OS just freed — each silo binds its own, so pairs never overlap.</summary>
    private static (int Silo, int Gateway) FreePorts() => (FreePort(), FreePort());

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}

/// <summary>Two silos living in ONE process — A→B over real HTTP, each with its own database.</summary>
public sealed class TwoSilos : IAsyncDisposable
{
    private TwoSilos()
    {
    }

    /// <summary>The silo the publications leave from.</summary>
    public Silo A { get; private set; } = null!;

    /// <summary>The silo the deliveries land on.</summary>
    public Silo B { get; private set; } = null!;

    /// <summary>Starts A first, then B — sequential starts, one process, four distinct Orleans ports.</summary>
    public static async Task<TwoSilos> StartAsync()
    {
        var pair = new TwoSilos
        {
            A = await Silo.StartAsync("a"),
        };
        pair.B = await Silo.StartAsync("b");
        return pair;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await B.DisposeAsync();
        await A.DisposeAsync();
    }
}

/// <summary>The shared A→B pair of the facet class — one pair for every fact except the ones that stop a silo.</summary>
public sealed class TwoSilosFixture : IAsyncLifetime
{
    /// <summary>The pair under test; built once for the class.</summary>
    public TwoSilos Silos { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync() => Silos = await TwoSilos.StartAsync();

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (Silos is not null)
        {
            await Silos.DisposeAsync();
        }
    }
}

/// <summary>House helpers of the two-silo suites: setup, delivery, projection and the readers every AC asserts with.</summary>
internal static class TwoSiloHarness
{
    /// <summary>Registers an account on one silo; the very path that births a signable actor (GAP-50).</summary>
    public static async Task<(AuthClient Client, string Handle)> RegisterAsync(Silo silo, string prefix)
    {
        var handle = PostRoutes.Handle(prefix);
        var client = silo.NewClient();
        Assert.Equal(System.Net.HttpStatusCode.Created,
            (await client.RegisterAsync(handle, PostRoutes.Password)).StatusCode);
        return (client, handle);
    }

    /// <summary>One account owning a community container whose slug the test chose — the SAME slug can be created on both silos.</summary>
    public static async Task<(AuthClient Owner, string Handle, string Slug)> CommunityAsync(Silo silo, string prefix, string slug)
    {
        var (owner, handle) = await RegisterAsync(silo, prefix);
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Comunidade " + prefix, slug, summary = "facet de dois silos" }, owner.Csrf);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        return (owner, handle, slug);
    }

    /// <summary>
    /// The follow hand-shake over the real wire: the follower's route enqueues, its own pump signs
    /// and POSTs to the target's inbox (§5), and the target's 202 leaves the follower row Sent.
    /// </summary>
    public static async Task FollowAsync(Silo follower, AuthClient client, string targetActorUri)
    {
        var routed = await PostRoutes.SendAsync(
            client, HttpMethod.Post, "/api/federation/follow", new { targetActorUri }, client.Csrf);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, routed.StatusCode);

        await follower.Pump.RunOnceAsync();
        var row = Assert.Single(await follower.Deliveries.ForRecipientAsync(targetActorUri));
        Assert.Equal(PeerDeliveryState.Sent, row.State);
    }

    /// <summary>Projects every event past the projections' high-water — the Worker pump's tick, by hand (R-18).</summary>
    public static async Task ProjectAsync(Silo silo)
    {
        var projector = silo.App.Services.GetRequiredService<IActivityProjector>();
        var log = silo.App.Services.GetRequiredService<SqliteActivityLog>();
        foreach (var entry in await log.ReadAsync(await projector.LastProjectedSeqAsync()))
        {
            await projector.ProjectAsync(entry);
        }
    }

    /// <summary>How many events the append-only log holds — the "nothing was reprocessed" proof.</summary>
    public static async Task<int> LogCountAsync(Silo silo)
    {
        var log = silo.App.Services.GetRequiredService<SqliteActivityLog>();
        return (await log.ReadAsync(0)).Count;
    }

    /// <summary>
    /// One draft-cavage delivery of contract §5 signed with whatever key the caller holds — the
    /// product's <see cref="ActorSigner"/> or an RSA under test — <paramref name="sentBody"/>
    /// defaulting to the signed one, which is exactly how a divergent Digest is built.
    /// </summary>
    public static Task<HttpResponseMessage> SendSignedAsync(
        HttpClient http, string inbox, string body, Func<byte[], byte[]> sign, string keyId,
        string? sentBody = null, DateTimeOffset? at = null)
    {
        var signed = HttpSignatureSigner.Sign(
            new SignRequest(
                "post", inbox, http.BaseAddress!.Authority, keyId,
                Encoding.UTF8.GetBytes(body), at ?? DateTimeOffset.UtcNow),
            sign);

        var request = new HttpRequestMessage(HttpMethod.Post, inbox)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(sentBody ?? body)),
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/activity+json");
        request.Headers.TryAddWithoutValidation("Signature", signed.Signature);
        request.Headers.TryAddWithoutValidation("Digest", signed.Digest);
        request.Headers.TryAddWithoutValidation("Date", signed.Date);
        return http.SendAsync(request);
    }

    /// <summary>Signs with the silo's own actor key — the key its actor document publishes.</summary>
    public static async Task<HttpResponseMessage> SendSignedAsAsync(
        Silo signer, string handle, HttpClient http, string inbox, string body,
        string? sentBody = null, DateTimeOffset? at = null)
    {
        var key = await signer.SignerAsync(handle);
        return await SendSignedAsync(
            http, inbox, body, bytes => key.Sign(bytes), $"{signer.Origin}/users/{handle}#main-key",
            sentBody, at);
    }

    /// <summary>Publishes one local post and answers its canonical id (optionally with uploaded media) — the client's jar already says which silo.</summary>
    public static async Task<string> PublishAsync(
        AuthClient owner, string slug, string content, params string[] contentHashes)
    {
        var path = $"/api/containers/{slug}/posts";
        var response = contentHashes.Length == 0
            ? await PostRoutes.SendAsync(owner, HttpMethod.Post, path, new { content }, owner.Csrf)
            : await PostRoutes.SendAsync(owner, HttpMethod.Post, path, new { content, contentHashes }, owner.Csrf);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Publishes a reply under <paramref name="parentId"/> (contract §7.15's federated answer) — the raw URI spelling of the {*target} route, as the reply suite spells it.</summary>
    public static async Task<string> PublishReplyAsync(AuthClient owner, string parentId, string content)
    {
        var response = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/posts/{parentId}/replies",
            new { content }, owner.Csrf);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Changes the container policy (R-05's precondition: If-Match of the current version) — the owner's jar says which silo.</summary>
    public static async Task VisibilityAsync(AuthClient owner, string slug, string visibility, string etag)
    {
        var response = await PostRoutes.SendAsync(
            owner, HttpMethod.Put, $"/api/containers/{slug}/policy", new { visibility }, owner.Csrf, ifMatch: etag);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>One page of a container's feed, parsed and cloned for the caller to hold.</summary>
    public static async Task<JsonElement> FeedAsync(Silo silo, string slug)
    {
        using var response = await silo.Http.GetAsync($"/api/containers/{slug}/feed");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    /// <summary>The feed item of one post id, or the default JsonElement (ValueKind Undefined) when the post never arrived.</summary>
    public static JsonElement ItemOf(JsonElement feed, string postId)
    {
        foreach (var item in feed.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("id").GetString() == postId) return item.Clone();
        }

        return default; // ValueKind Undefined: readers assert with Assert.Equal(JsonValueKind.Undefined, ...)
    }

    /// <summary>One document of the federated surface, parsed and cloned.</summary>
    public static async Task<JsonElement> GetAsync(Silo silo, string path)
    {
        using var response = await silo.Http.GetAsync(path);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
