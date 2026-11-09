using System.Net;
using System.Text.Json;
using Squid.Api.Auth;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>
/// QA-0016, the "somente" half of AC-05: the patch body of contracts/http-api.md §4 carries
/// <c>name</c>, <c>summary</c> and <c>listed</c> — and nothing else the client sends may become
/// writable by accident. DEV's route test proves the three move; this one proves the other fields
/// do not move even when the request explicitly asks for it.
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0016ContainerPatchScopeTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public Qa0016ContainerPatchScopeTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-05 (QA half — the writable set is closed)
    public async Task A_patch_that_also_carries_slug_kind_and_policy_moves_only_the_three_writable_fields()
    {
        var owner = _host.NewClient();
        // ActorHandle.Rule is [a-z0-9_]{3,30}: the handle carries no separator, the slug may.
        var handle = "qa16" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
        Assert.Equal(HttpStatusCode.Created, (await owner.RegisterAsync(handle, Password)).StatusCode);
        var slug = Unique("escopo");

        var created = await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Escopo", slug, summary = "antes" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // The body asks for everything: the three writable fields plus the three that must not move.
        var patched = await SendAsync(owner, HttpMethod.Patch, $"/api/containers/{slug}",
            new
            {
                name = "Escopo II",
                summary = "depois",
                listed = true,
                slug = "slug-roubado",
                kind = "community",
                policy = new { visibility = "public", join = "open", version = 99 },
            });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        using var json = JsonDocument.Parse(await patched.Content.ReadAsStringAsync());
        var root = json.RootElement;

        // The three writable fields moved.
        Assert.Equal("Escopo II", root.GetProperty("name").GetString());
        Assert.Equal("depois", root.GetProperty("summary").GetString());
        Assert.True(root.GetProperty("listed").GetBoolean());

        // The rest of the aggregate is untouched: identity, kind and the policy in force.
        Assert.Equal(slug, root.GetProperty("slug").GetString());
        Assert.Equal("group", root.GetProperty("kind").GetString());
        Assert.Equal($"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress!.Authority}/c/{slug}", root.GetProperty("id").GetString());
        Assert.Equal(1, root.GetProperty("memberCount").GetInt64());
        var policy = root.GetProperty("policy");
        Assert.Equal("membersOnly", policy.GetProperty("visibility").GetString());
        Assert.Equal("invite", policy.GetProperty("join").GetString());
        Assert.Equal(1, policy.GetProperty("version").GetInt32());

        // A fresh read agrees with the response — nothing moved behind it either.
        var read = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var reread = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(slug, reread.RootElement.GetProperty("slug").GetString());
        Assert.Equal("group", reread.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, reread.RootElement.GetProperty("policy").GetProperty("version").GetInt32());
    }

    private Task<HttpResponseMessage> SendAsync(
        AuthClient client, HttpMethod method, string path, object? body = null) =>
        client.SendAsync(method, path, body, request =>
        {
            if (client.Csrf is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, client.Csrf);
            }
        });

    private static string Unique(string prefix) =>
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
