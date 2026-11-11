using System.Net;
using System.Text.Json;
using Squid.Tests.Auth;
using Squid.Tests.Containers;
using Squid.Tests.Posts;

namespace Squid.Tests.Moderation;

/// <summary>
/// House helpers of the TASK-0031 suites: unique handles and slugs, a signed-in client per
/// account, the operator session of the host fixture, the publish/join ladder every scenario
/// needs and the JSON readers the route assertions speak — mirrors of <c>PostRoutes</c> and
/// <c>FeedHarness</c> bound to this fixture's own type.
/// </summary>
internal static class ReportRoutes
{
    /// <summary>The password the register endpoint accepts (house value).</summary>
    public const string Password = PostRoutes.Password;

    /// <summary>A unique handle inside <c>ActorHandle</c>'s rule.</summary>
    public static string Handle(string prefix) => PostRoutes.Handle(prefix);

    /// <summary>A unique container slug.</summary>
    public static string Slug(string prefix) => PostRoutes.Slug(prefix);

    /// <summary>The origin (scheme + host) the test host answers on.</summary>
    public static string Origin(PolicyAuditHostFixture host) =>
        $"{host.Http.BaseAddress!.Scheme}://{host.Http.BaseAddress!.Authority}";

    /// <summary>The canonical actor id of a local handle — the shape every trail row carries.</summary>
    public static string ActorOf(PolicyAuditHostFixture host, string handle) =>
        $"{Origin(host)}/users/{handle}";

    /// <summary>Registers a fresh account and returns its cookie jar.</summary>
    public static async Task<(AuthClient Client, string Handle)> SignedInAsync(
        PolicyAuditHostFixture host, string prefix)
    {
        var handle = Handle(prefix);
        var client = host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);
        return (client, handle);
    }

    /// <summary>An operator session: the handle the host was built with, logged in for this test.</summary>
    public static async Task<AuthClient> OperatorAsync(PolicyAuditHostFixture host)
    {
        var client = host.NewClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.LoginAsync(host.OperatorHandle, Password)).StatusCode);
        return client;
    }

    /// <summary>Sends the request with the CSRF header this jar holds (null for an anonymous jar).</summary>
    public static Task<HttpResponseMessage> SendAsync(
        AuthClient client, HttpMethod method, string path, object? body = null) =>
        PostRoutes.SendAsync(client, method, path, body, client.Csrf);

    /// <summary>A container the caller owns, created through the route like every other suite does.</summary>
    public static async Task<string> ContainerAsync(AuthClient owner, string kind, string name)
    {
        var slug = Slug("denuncia");
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(owner, HttpMethod.Post,
            "/api/containers", new { kind, name, slug }, owner.Csrf)).StatusCode);
        return slug;
    }

    /// <summary>Joins the caller to the container so the read ladder admits them.</summary>
    public static async Task JoinAsync(AuthClient client, string slug)
    {
        var joined = await PostRoutes.SendAsync(client, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, client.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");
    }

    /// <summary>Publishes one post and answers its canonical id, the route's key and the policy version.</summary>
    public static async Task<(string Id, string Key, int PolicyVersion)> PublishAsync(
        AuthClient author, string slug, string content)
    {
        var response = await PostRoutes.SendAsync(author, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content }, author.Csrf);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString()!;
        return (id, id[(id.LastIndexOf('/') + 1)..],
            json.RootElement.GetProperty("policyVersion").GetInt32());
    }

    /// <summary>The parsed body, cloned out of its document so the caller can hold it.</summary>
    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>One property of a view as text; a missing property fails the assertion, never defaults.</summary>
    public static string? Text(JsonElement element, string name) =>
        element.GetProperty(name).GetString();

    /// <summary>The <c>items</c> array of a queue page.</summary>
    public static List<JsonElement> Items(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();

    /// <summary>One page of <c>GET /api/admin/reports</c> — the operator's queue, asserted 200.</summary>
    public static async Task<List<JsonElement>> QueueAsync(
        PolicyAuditHostFixture host, AuthClient operatorClient, string query)
    {
        var response = await PostRoutes.SendAsync(operatorClient, HttpMethod.Get, "/api/admin/reports" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Items(await JsonAsync(response));
    }
}
