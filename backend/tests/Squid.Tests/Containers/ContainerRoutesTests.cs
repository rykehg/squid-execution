using System.Net;
using System.Text.Json;
using Squid.Api.Auth;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>
/// AC-01..AC-05 of TASK-0017, proved over HTTP against the host <see cref="SquidServiceHost"/>
/// builds: both presets in this one file (AC-01/AC-02), the owner membership and the duplicate
/// slug that creates nothing (AC-03), the byte-identical 404 of FR-018/SC-005 (AC-04), and the
/// patch that moves only the state version, refused when stale (AC-05).
/// </summary>
[Collection("ServiceHost")]
public sealed class ContainerRoutesTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public ContainerRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task A_group_is_created_with_the_members_only_preset_and_needs_session_and_csrf()
    {
        var (owner, _) = await SignedInAsync("ac01");
        var slug = Slug("turma");

        var created = await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Turma", slug }, owner.Csrf);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("\"1\"", ETag(created));

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal($"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress!.Authority}/c/{slug}", root.GetProperty("id").GetString());
        // AC-07 (human gate 2026-11-08 15:57:34): this host speaks http, so the id is born http —
        // scheme + Host of the request, never a fixed https.
        Assert.StartsWith("http://127.0.0.1:", root.GetProperty("id").GetString());
        Assert.Equal(slug, root.GetProperty("slug").GetString());
        Assert.Equal("group", root.GetProperty("kind").GetString());
        Assert.Equal("Turma", root.GetProperty("name").GetString());
        Assert.False(root.GetProperty("listed").GetBoolean());
        Assert.Equal(1, root.GetProperty("memberCount").GetInt64());

        var policy = root.GetProperty("policy");
        Assert.Equal(1, policy.GetProperty("version").GetInt32());
        Assert.Equal("membersOnly", policy.GetProperty("visibility").GetString());
        Assert.Equal("invite", policy.GetProperty("join").GetString());
        Assert.Equal("members", policy.GetProperty("posting").GetString());
        var interaction = policy.GetProperty("interaction");
        Assert.Equal("members", interaction.GetProperty("canComment").GetString());
        Assert.Equal("members", interaction.GetProperty("canReact").GetString());
        Assert.Equal("members", interaction.GetProperty("canShare").GetString());
        Assert.Equal("members", interaction.GetProperty("canAttach").GetString());

        // "autenticado (sessão + CSRF)": no session is 401, a session without the token is 403.
        var anonymous = _host.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.SendAsync(HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Sem sessão", slug = Slug("anon") })).StatusCode);

        var (withoutCsrf, _) = await SignedInAsync("ac01b");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await withoutCsrf.SendAsync(HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Sem csrf", slug = Slug("semcsrf") })).StatusCode);
    }

    [Fact] // AC-02
    public async Task A_community_is_created_with_the_public_preset_in_the_same_file_as_the_group()
    {
        var (owner, _) = await SignedInAsync("ac02");
        var slug = Slug("rust");

        var created = await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Rust", slug }, owner.Csrf);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal($"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress!.Authority}/c/{slug}", root.GetProperty("id").GetString());
        Assert.True(root.GetProperty("listed").GetBoolean());

        var policy = root.GetProperty("policy");
        Assert.Equal(1, policy.GetProperty("version").GetInt32());
        Assert.Equal("public", policy.GetProperty("visibility").GetString());
        Assert.Equal("open", policy.GetProperty("join").GetString());
        Assert.Equal("members", policy.GetProperty("posting").GetString());

        // Public is public: the container reads back without any session at all.
        var anonymous = _host.NewClient();
        var read = await anonymous.SendAsync(HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact] // AC-03
    public async Task The_creator_is_the_active_owner_and_a_duplicated_slug_creates_nothing()
    {
        var (owner, handle) = await SignedInAsync("ac03");
        var slug = Slug("grupo");
        Assert.Equal(HttpStatusCode.Created,
            (await SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Grupo", slug }, owner.Csrf)).StatusCode);

        var members = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}/members");
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        using var json = JsonDocument.Parse(await members.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(element => element.GetProperty("handle").GetString() == handle);
        Assert.Equal("owner", item.GetProperty("role").GetString());
        Assert.Equal("active", item.GetProperty("state").GetString());
        Assert.Equal($"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress!.Authority}/users/{handle}",
            item.GetProperty("actorId").GetString());

        // Duplicate slug of the same owner: 400 validation_failed with nothing created (GAP-16).
        var containersBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteContainerStore.StoreName);
        var membersBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteContainerStore.MembersStoreName);

        var duplicate = await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Outro nome", slug }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var duplicateBody = await duplicate.Content.ReadAsStringAsync();
        Assert.Contains("validation_failed", duplicateBody, StringComparison.Ordinal);
        Assert.Contains(slug, duplicateBody, StringComparison.Ordinal);

        Assert.Equal(containersBefore,
            await AuthDb.CountAsync(_host.StoragePath, SqliteContainerStore.StoreName));
        Assert.Equal(membersBefore,
            await AuthDb.CountAsync(_host.StoragePath, SqliteContainerStore.MembersStoreName));

        // The original container survived untouched: still readable, still one owner.
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}")).StatusCode);
        var membersAfter = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}/members");
        using var after = JsonDocument.Parse(await membersAfter.Content.ReadAsStringAsync());
        Assert.Single(after.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact] // AC-04
    public async Task An_unseen_container_answers_the_same_404_as_one_that_does_not_exist()
    {
        var (owner, _) = await SignedInAsync("ac04");
        var slug = Slug("fechado");
        Assert.Equal(HttpStatusCode.Created,
            (await SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Fechado", slug }, owner.Csrf)).StatusCode);

        // Visible to the reader who owns it: container plus the policy in force.
        var seen = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        using var seenJson = JsonDocument.Parse(await seen.Content.ReadAsStringAsync());
        var policy = seenJson.RootElement.GetProperty("policy");
        Assert.Equal("membersOnly", policy.GetProperty("visibility").GetString());
        Assert.Equal(1, policy.GetProperty("version").GetInt32());

        // A reader without membership and an absent slug: same status, byte-identical bodies.
        var stranger = await SignedInAsync("ac04stranger");
        var invisible = await SendAsync(stranger.Client, HttpMethod.Get, $"/api/containers/{slug}");
        var absent = await SendAsync(stranger.Client, HttpMethod.Get, "/api/containers/que-nao-existe");
        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        var invisibleBody = await invisible.Content.ReadAsStringAsync();
        var absentBody = await absent.Content.ReadAsStringAsync();
        Assert.Equal(invisibleBody, absentBody);
        Assert.Contains("not_found", invisibleBody, StringComparison.Ordinal);

        // No session at all lands on the very same body (SC-005: existence never leaks).
        var anonymous = _host.NewClient();
        var anonymousBody = await (await anonymous.SendAsync(HttpMethod.Get, $"/api/containers/{slug}"))
            .Content.ReadAsStringAsync();
        Assert.Equal(invisibleBody, anonymousBody);
    }

    [Fact] // AC-05
    public async Task Patch_edits_only_the_editable_fields_and_a_stale_version_conflicts_untouched()
    {
        var (owner, _) = await SignedInAsync("ac05");
        var slug = Slug("edicao");
        Assert.Equal(HttpStatusCode.Created,
            (await SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Original", slug, summary = "antes" }, owner.Csrf)).StatusCode);

        var read = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal("\"1\"", ETag(read));

        // Without If-Match the write is unconditional: 200, state version 1 → 2.
        var patched = await SendAsync(owner, HttpMethod.Patch, $"/api/containers/{slug}",
            new { name = "Editado", summary = "depois", listed = true }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal("\"2\"", ETag(patched));

        using var patchedJson = JsonDocument.Parse(await patched.Content.ReadAsStringAsync());
        var root = patchedJson.RootElement;
        Assert.Equal("Editado", root.GetProperty("name").GetString());
        Assert.Equal("depois", root.GetProperty("summary").GetString());
        Assert.True(root.GetProperty("listed").GetBoolean());
        Assert.Equal(slug, root.GetProperty("slug").GetString());   // immutable
        Assert.Equal("group", root.GetProperty("kind").GetString());
        Assert.Equal(1, root.GetProperty("policy").GetProperty("version").GetInt32()); // only the state version moved

        // The state as it stands: the hash the stale write must not move.
        var before = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");
        var beforeBody = await before.Content.ReadAsStringAsync();

        // The same If-Match again, now stale: 409 version_conflict, current version reported.
        var stale = await SendAsync(owner, HttpMethod.Patch, $"/api/containers/{slug}",
            new { name = "Nao deve entrar" }, owner.Csrf, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("version_conflict", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("\"2\"", ETag(stale));

        var after = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal(beforeBody, await after.Content.ReadAsStringAsync());

        // The current version is accepted, and the policy version still has not moved.
        var fresh = await SendAsync(owner, HttpMethod.Patch, $"/api/containers/{slug}",
            new { name = "Editado de novo" }, owner.Csrf, ifMatch: "\"2\"");
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Equal("\"3\"", ETag(fresh));
        using var freshJson = JsonDocument.Parse(await fresh.Content.ReadAsStringAsync());
        Assert.Equal("Editado de novo", freshJson.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, freshJson.RootElement.GetProperty("policy").GetProperty("version").GetInt32());
    }

    private async Task<(AuthClient Client, string Handle)> SignedInAsync(string prefix)
    {
        var handle = Handle(prefix);
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);
        return (client, handle);
    }

    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client,
        HttpMethod method,
        string path,
        object? body = null,
        string? csrf = null,
        string? ifMatch = null) =>
        client.SendAsync(method, path, body, request =>
        {
            if (csrf is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }

            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }
        });

    private static string? ETag(HttpResponseMessage response) =>
        response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null;

    private static string Handle(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    private static string Slug(string prefix) =>
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
