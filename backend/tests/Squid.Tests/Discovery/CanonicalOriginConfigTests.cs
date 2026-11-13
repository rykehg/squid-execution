using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Auth;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Discovery;

/// <summary>
/// TASK-0041 AC-01..AC-04 against SPEC-0011 section A (I-11, GAP-45): one canonical helper
/// behind the four origin points; <c>Instance:Domain</c> opt-in — with no configuration every id
/// stays on scheme + Host byte-identically; <c>X-Forwarded-Proto/Host</c> honoured only when
/// <c>Instance:ForwardedHeaders</c> declares a trusted proxy; and an error answer that never
/// carries the configured domain or a stack trace (FR-053). Each fact starts its own host on a
/// dynamic port; the <c>ServiceHost</c> collection is what keeps the fixed silo ports from
/// overlapping.
/// </summary>
[Collection("ServiceHost")]
public sealed class CanonicalOriginConfigTests
{
    /// <summary>The configured public domain of AC-02/AC-04.</summary>
    private const string Domain = "squid.test";

    /// <summary>AC-02's "Host da request é outro": an internal authority, never the public one.</summary>
    private const string InternalHost = "localhost:5001";

    /// <summary>The proxy's authority of AC-03 — distinct from <see cref="Domain"/>, so nothing coincides.</summary>
    private const string ProxyHost = "proxy.squid.test";

    private const string ActivityJson = "application/activity+json";

    [Fact] // AC-01
    public async Task Ac01_without_configuration_every_point_stays_scheme_plus_host()
    {
        await using var host = await StartAsync();

        await AssertCanonicalIdsAsync(host, host.Origin, Authority(host));
    }

    [Fact] // AC-02
    public async Task Ac02_with_instance_domain_every_published_id_uses_the_configured_domain()
    {
        await using var host = await StartAsync("--Instance:Domain=" + Domain);

        // Every request carries Host: localhost:5001 — the ids must still be born on squid.test.
        await AssertCanonicalIdsAsync(host, $"https://{Domain}", Domain,
            request => request.Headers.Host = InternalHost);

        // The internal authority asking for the same handle is answered with the CANONICAL
        // subject and href: with the key present the subject never carries the request Host
        // (SPEC-0011 section A — webfinger.subject usa {Instance:Domain}).
        var client = new AuthClient(host.Http);
        var handle = await RegisterAsync(client, request => request.Headers.Host = InternalHost);
        var (subject, href) = await WebfingerAsync(client, $"acct:{handle}@{InternalHost}",
            request => request.Headers.Host = InternalHost);
        Assert.Equal($"acct:{handle}@{Domain}", subject);
        Assert.Equal($"https://{Domain}/users/{handle}", href);
    }

    [Fact] // AC-03
    public async Task Ac03_forwarded_headers_are_ignored_unless_the_trust_key_is_set()
    {
        // (a) No Instance:ForwardedHeaders: the headers are not read at all — origin stays the
        // asking scheme + Host, exactly as before this task.
        await using (var plain = await StartAsync())
        {
            var client = new AuthClient(plain.Http);
            var handle = await RegisterAsync(client, Forward);
            Assert.Equal($"{plain.Origin}/users/{handle}", await ProfileIdAsync(client, handle, Forward));
            var (subject, href) = await WebfingerAsync(client, $"acct:{handle}@{Authority(plain)}", Forward);
            Assert.Equal($"acct:{handle}@{Authority(plain)}", subject);
            Assert.Equal($"{plain.Origin}/users/{handle}", href);
        }

        // (b) Instance:ForwardedHeaders=true: the very same headers are now the origin, and a
        // request WITHOUT them still answers on its own scheme + Host.
        await using (var proxied = await StartAsync("--Instance:ForwardedHeaders=true"))
        {
            var client = new AuthClient(proxied.Http);
            var handle = await RegisterAsync(client, Forward);
            Assert.Equal($"https://{ProxyHost}/users/{handle}", await ProfileIdAsync(client, handle, Forward));
            var (subject, href) = await WebfingerAsync(client, $"acct:{handle}@{ProxyHost}", Forward);
            Assert.Equal($"acct:{handle}@{ProxyHost}", subject);
            Assert.Equal($"https://{ProxyHost}/users/{handle}", href);

            Assert.Equal($"{proxied.Origin}/users/{handle}", await ProfileIdAsync(client, handle));
        }
    }

    [Fact] // AC-04
    public async Task Ac04_error_answers_carry_neither_the_configured_domain_nor_a_stack_trace()
    {
        await using var plain = await StartAsync();
        await using var configured = await StartAsync("--Instance:Domain=" + Domain);

        var plainClient = new AuthClient(plain.Http);
        var configuredClient = new AuthClient(configured.Http);
        await RegisterAsync(plainClient);
        await RegisterAsync(configuredClient);

        // The 404 of an unknown actor on webfinger: byte-identical on both hosts.
        var missing = PostRoutes.Handle("nao");
        var plainBody = await NotFoundAsync(plainClient, Webfinger($"acct:{missing}@{Authority(plain)}"));
        var configuredBody = await NotFoundAsync(configuredClient, Webfinger($"acct:{missing}@{Domain}"));
        Assert.Equal(plainBody, configuredBody);
        AssertClean(plainBody);
        AssertClean(configuredBody);

        // The 404 of an absent profile: same rule, same shape.
        var absent = PostRoutes.Handle("sumiu");
        plainBody = await NotFoundAsync(plainClient, $"/users/{absent}");
        configuredBody = await NotFoundAsync(configuredClient, $"/users/{absent}");
        Assert.Equal(plainBody, configuredBody);
        AssertClean(plainBody);
        AssertClean(configuredBody);

        // A refusal WITH a body (401): the same envelope on both, no domain, no stack. Fresh
        // clients — the registered jars above would carry a session and answer 200.
        using var plainDenied = await new AuthClient(plain.Http).SendAsync(HttpMethod.Get, "/api/timeline");
        using var configuredDenied = await new AuthClient(configured.Http).SendAsync(HttpMethod.Get, "/api/timeline");
        Assert.Equal(HttpStatusCode.Unauthorized, plainDenied.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, configuredDenied.StatusCode);
        var plainError = ErrorOf(await plainDenied.Content.ReadAsStringAsync());
        var configuredError = ErrorOf(await configuredDenied.Content.ReadAsStringAsync());
        Assert.Equal(plainError.Code, configuredError.Code);
        Assert.Equal(plainError.Message, configuredError.Message);
        AssertClean(await plainDenied.Content.ReadAsStringAsync());
        AssertClean(await configuredDenied.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The four points of SPEC-0011 section A through one signed-in client: Discovery's profile id
    /// and webfinger subject/href, the container (collection), the post (object) with the activity
    /// row it appends, and the thread with its message — every published id must sit on
    /// <paramref name="origin"/>. <paramref name="configure"/> is where a test forges the Host or
    /// adds the forwarded headers of AC-03.
    /// </summary>
    private static async Task AssertCanonicalIdsAsync(
        Host host, string origin, string webfingerDomain, Action<HttpRequestMessage>? configure = null)
    {
        var client = new AuthClient(host.Http);
        var handle = await RegisterAsync(client, configure);

        // Discovery: the actor id and the webfinger pair.
        Assert.Equal($"{origin}/users/{handle}", await ProfileIdAsync(client, handle, configure));
        var (subject, href) = await WebfingerAsync(client, $"acct:{handle}@{webfingerDomain}", configure);
        Assert.Equal($"acct:{handle}@{webfingerDomain}", subject);
        Assert.Equal($"{origin}/users/{handle}", href);

        // Containers: the canonical collection id.
        var slug = PostRoutes.Slug("orig");
        var created = await SendAsync(client, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Origem", slug }, client.Csrf, configure);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"{origin}/c/{slug}", await IdOfAsync(created));

        // Posts: object id, author, container, and the activity row those ids feed (the
        // activity's own id is a urn:squid:activity: token — it never derived from a Host).
        var published = await SendAsync(client, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "post da origem" }, client.Csrf, configure);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var postId = await IdOfAsync(published);
        Assert.StartsWith($"{origin}/o/", postId, StringComparison.Ordinal);
        Assert.Equal($"{origin}/users/{handle}", await FieldOfAsync(published, "author"));
        Assert.Equal($"{origin}/c/{slug}", await FieldOfAsync(published, "containerId"));

        var activities = host.App.Services.GetRequiredService<IActivityStore>();
        var create = Assert.Single(await activities.ReadAsync(), candidate => candidate.ObjectId == postId);
        Assert.Equal($"{origin}/users/{handle}", create.ActorId);

        // Threads: thread id and message id.
        var thread = await SendAsync(client, HttpMethod.Post, "/api/threads",
            new { kind = "direct", participantActorIds = new[] { PostRoutes.Handle("par") } },
            client.Csrf, configure);
        Assert.Equal(HttpStatusCode.Created, thread.StatusCode);
        var threadId = await IdOfAsync(thread);
        Assert.StartsWith($"{origin}/t/", threadId, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post,
            AckPath(threadId), null, client.Csrf, configure)).StatusCode);
        var message = await SendAsync(client, HttpMethod.Post, MessagesPath(threadId),
            new { content = "mensagem da origem" }, client.Csrf, configure);
        Assert.Equal(HttpStatusCode.OK, message.StatusCode);
        Assert.StartsWith($"{origin}/m/", await IdOfAsync(message), StringComparison.Ordinal);
    }

    /// <summary>The one call every scenario makes: session CSRF when asked, then the test's own headers.</summary>
    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client, HttpMethod method, string path, object? body = null,
        string? csrf = null, Action<HttpRequestMessage>? configure = null) =>
        client.SendAsync(method, path, body, request =>
        {
            if (csrf is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }

            configure?.Invoke(request);
        });

    /// <summary>Registers a fresh account (the house password) and returns its handle.</summary>
    private static async Task<string> RegisterAsync(
        AuthClient client, Action<HttpRequestMessage>? configure = null)
    {
        var handle = PostRoutes.Handle("orig");
        var registered = await SendAsync(client, HttpMethod.Post, "/api/auth/register",
            new { handle, password = PostRoutes.Password }, configure: configure);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return handle;
    }

    /// <summary>The <c>id</c> of the profile document this client asked for.</summary>
    private static async Task<string> ProfileIdAsync(
        AuthClient client, string handle, Action<HttpRequestMessage>? configure = null)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"/users/{handle}", configure: configure);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ActivityJson, response.Content.Headers.ContentType?.MediaType);
        return await FieldOfAsync(response, "id");
    }

    /// <summary>The webfinger <c>subject</c> and the <c>self</c> href it publishes.</summary>
    private static async Task<(string Subject, string Href)> WebfingerAsync(
        AuthClient client, string resource, Action<HttpRequestMessage>? configure = null)
    {
        using var response = await SendAsync(client, HttpMethod.Get, Webfinger(resource), configure: configure);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/jrd+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var self = json.RootElement.GetProperty("links").EnumerateArray()
            .Single(link => link.GetProperty("rel").GetString() == "self");
        return (json.RootElement.GetProperty("subject").GetString()!,
            self.GetProperty("href").GetString()!);
    }

    /// <summary>The body of the one 404 every refusal of this endpoint answers with.</summary>
    private static async Task<string> NotFoundAsync(AuthClient client, string path)
    {
        using var response = await client.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>FR-053: no configured domain, no stack trace, no internal type name in a refusal.</summary>
    private static void AssertClean(string body)
    {
        Assert.DoesNotContain(Domain, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    private static (string Code, string Message) ErrorOf(string body)
    {
        using var json = JsonDocument.Parse(body);
        var error = json.RootElement.GetProperty("error");
        return (error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!);
    }

    private static async Task<string> IdOfAsync(HttpResponseMessage response) =>
        await FieldOfAsync(response, "id");

    private static async Task<string> FieldOfAsync(HttpResponseMessage response, string name)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty(name).GetString()!;
    }

    /// <summary>The proxy headers of AC-03, added to a request that must see them.</summary>
    private static void Forward(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", ProxyHost);
    }

    private static string Webfinger(string resource) =>
        "/.well-known/webfinger?resource=" + Uri.EscapeDataString(resource);

    private static string AckPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/ack-no-e2ee";

    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";

    private static string Authority(Host host) => new Uri(host.Origin).Authority;

    /// <summary>One host under test, on a dynamic port, with its own temp storage file.</summary>
    private sealed record Host(WebApplication App, HttpClient Http, string Origin, string StoragePath)
        : IAsyncDisposable
    {
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
                // a leftover temp file is not a product failure; the OS cleans it up
            }
        }
    }

    private static async Task<Host> StartAsync(params string[] extra)
    {
        var storage = Path.Combine(Path.GetTempPath(), $"squid-origin-{Guid.NewGuid():N}.db");
        var app = SquidServiceHost.Build(
            ["--Storage:Path=" + storage, "--Logging:LogLevel:Default=Warning", .. extra]);
        app.Urls.Add("http://127.0.0.1:0"); // dynamic port: tests never fight over a fixed one
        await app.StartAsync();

        var baseAddress = new Uri(app.Urls.Single());
        var origin = $"{baseAddress.Scheme}://{baseAddress.Authority}";
        return new Host(app, new HttpClient { BaseAddress = baseAddress }, origin, storage);
    }
}
