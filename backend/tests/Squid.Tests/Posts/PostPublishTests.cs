using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Posts;

/// <summary>
/// AC-01, AC-02 and AC-06 of TASK-0023 over HTTP against the host <see cref="SquidServiceHost"/>
/// builds: the 201 with the four frozen fields plus its <c>Activity: Create</c> (AC-01), the
/// policy denial that answers 403 with a Deny row and the byte-identical 404 of an invisible
/// container (AC-02), and the three refusals of AC-06 — a suspended actor, a payload above the
/// cap, and HTML/C0 controls sanitized on the server.
/// </summary>
[Collection("ServiceHost")]
public sealed class PostPublishTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public PostPublishTests(AuthHostFixture host) => _host = host;

    private IPolicyAuditStore Audit =>
        _host.App.Services.GetRequiredService<IPolicyAuditStore>();

    private IActivityStore Activities =>
        _host.App.Services.GetRequiredService<IActivityStore>();

    private IActorStatusStore Statuses =>
        _host.App.Services.GetRequiredService<IActorStatusStore>();

    [Fact] // AC-01
    public async Task Publishing_answers_the_frozen_fields_and_appends_one_create_activity()
    {
        var (owner, handle) = await PostRoutes.SignedInAsync(_host, "ac01");
        var slug = PostRoutes.Slug("grupo");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Turma", slug }, owner.Csrf)).StatusCode);

        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "primeiro post" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var origin = PostRoutes.Origin(_host);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var id = root.GetProperty("id").GetString();
        Assert.StartsWith($"{origin}/o/", id, StringComparison.Ordinal);
        Assert.Equal(1, root.GetProperty("seq").GetInt64());                        // seq do contêiner
        Assert.Equal(1, root.GetProperty("policyVersion").GetInt32());              // a versão avaliada
        Assert.Equal("membersOnly", root.GetProperty("visibilityAtPublish").GetString()); // congelada
        Assert.Equal("post", root.GetProperty("kind").GetString());
        Assert.Equal($"{origin}/users/{handle}", root.GetProperty("author").GetString());
        Assert.Equal($"{origin}/c/{slug}", root.GetProperty("containerId").GetString());
        Assert.Equal("primeiro post", root.GetProperty("content").GetString());
        Assert.Equal(0, root.GetProperty("depth").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("replyToId").ValueKind);
        Assert.False(string.IsNullOrEmpty(root.GetProperty("createdAt").GetString()));

        // The event behind the projections (SPEC-0009 A): one Create, the global Seq, raw JSON.
        var events = await Activities.ReadAsync();
        var activity = Assert.Single(events, candidate => candidate.ObjectId == id);
        Assert.Equal("Create", activity.Type);
        Assert.Equal($"{origin}/users/{handle}", activity.ActorId);
        Assert.StartsWith("urn:squid:activity:", activity.Id, StringComparison.Ordinal);

        // The Seq is GLOBAL: whatever container an event belongs to, the log numbers it from 1
        // with no gaps and in append order (GAP-23 — unlike Object.Seq, which restarts per container).
        Assert.Equal(
            Enumerable.Range(1, events.Count).Select(position => (long)position),
            events.Select(candidate => candidate.Seq));

        using var payload = JsonDocument.Parse(activity.Payload);
        Assert.Equal(id, payload.RootElement.GetProperty("id").GetString());
        Assert.Equal("primeiro post", payload.RootElement.GetProperty("content").GetString());
    }

    [Fact] // AC-01 preconditions: sessão + CSRF
    public async Task Publishing_and_replying_need_a_session_and_the_csrf_token()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac01pre");
        var slug = PostRoutes.Slug("pre");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Pre", slug }, owner.Csrf)).StatusCode);

        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "existe" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString()!;

        // No session at all: the endpoint's own 401, on both write routes of this slice.
        var anonymous = _host.NewClient();
        var publishWithoutSession = await PostRoutes.SendAsync(anonymous, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "sem sessão" });
        Assert.Equal(HttpStatusCode.Unauthorized, publishWithoutSession.StatusCode);
        Assert.Contains("not_authenticated",
            await publishWithoutSession.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var replyWithoutSession = await PostRoutes.SendAsync(anonymous, HttpMethod.Post,
            $"/api/posts/{id}/replies", new { content = "sem sessão" });
        Assert.Equal(HttpStatusCode.Unauthorized, replyWithoutSession.StatusCode);
        Assert.Contains("not_authenticated",
            await replyWithoutSession.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A session without the double-submission token: the middleware's 403, before the route runs.
        var withoutCsrf = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "sem csrf" });
        Assert.Equal(HttpStatusCode.Forbidden, withoutCsrf.StatusCode);
        Assert.Contains("csrf_failed", await withoutCsrf.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact] // AC-02, first half
    public async Task A_member_denied_by_the_posting_policy_gets_403_and_a_deny_row_on_the_trail()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac02a");
        var slug = PostRoutes.Slug("comunidade");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "community", name = "Comunidade", slug }, owner.Csrf)).StatusCode);

        // A policy that denies Posting: the moderators gate, version 1 -> 2 (nothing else moves).
        var policy = await PostRoutes.SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/policy", new { posting = "moderators" }, owner.Csrf, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);

        // A plain member — entry is open on the community preset.
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "ac02amembro");
        var joined = await PostRoutes.SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");

        var denied = await PostRoutes.SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "não publico" }, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        Assert.Contains("policy_denied", deniedBody, StringComparison.Ordinal);

        // The refusal is a decision: one Deny row with the version the evaluation used (FR-019).
        var rows = await Audit.QueryAsync(new PolicyAuditFilter(Action: "Post"));
        var deny = Assert.Single(rows, row =>
            row.Result == PolicyAudit.Deny
            && row.ActorId.EndsWith($"/users/{memberHandle}", StringComparison.Ordinal));
        Assert.Equal(PolicyAudit.Deny, deny.Result);
        Assert.Equal(2, deny.PolicyVersion);
        Assert.False(string.IsNullOrEmpty(deny.Reason));
    }

    [Fact] // AC-02, second half
    public async Task Publishing_to_an_invisible_container_answers_the_404_of_an_absent_slug()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac02b");
        var slug = PostRoutes.Slug("fechado");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Fechado", slug }, owner.Csrf)).StatusCode);

        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "ac02bestr");
        var invisible = await PostRoutes.SendAsync(stranger, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "de fora" }, stranger.Csrf);
        var absent = await PostRoutes.SendAsync(stranger, HttpMethod.Post,
            "/api/containers/que-nao-existe/posts", new { content = "de fora" }, stranger.Csrf);

        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        // Two bodies, one answer: existence never leaks (SC-005), trace id included.
        var invisibleBody = await invisible.Content.ReadAsStringAsync();
        var absentBody = await absent.Content.ReadAsStringAsync();
        Assert.Equal(invisibleBody, absentBody);
        Assert.Contains("not_found", invisibleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", invisibleBody, StringComparison.Ordinal);
    }

    [Fact] // AC-06, first refusal
    public async Task A_suspended_actor_is_denied_and_audited()
    {
        var (owner, handle) = await PostRoutes.SignedInAsync(_host, "ac06a");
        var slug = PostRoutes.Slug("suspenso");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Suspenso", slug }, owner.Csrf)).StatusCode);

        // The status has no route yet (TASK-0031 births it); the rule is active all the same.
        await Statuses.SuspendAsync(handle);

        var denied = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "não pode" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var rows = await Audit.QueryAsync(new PolicyAuditFilter(Action: "Post"));
        var deny = Assert.Single(rows, row =>
            row.Result == PolicyAudit.Deny
            && row.ActorId.EndsWith($"/users/{handle}", StringComparison.Ordinal));
        Assert.Contains("suspenso", deny.Reason, StringComparison.Ordinal); // §3.1 named in the trail itself
    }

    [Fact] // AC-06, second refusal
    public async Task Content_above_the_cap_is_refused_and_a_fitting_one_publishes()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac06b");
        var slug = PostRoutes.Slug("tamanho");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Tamanho", slug }, owner.Csrf)).StatusCode);

        var before = await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName);

        var tooLong = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = new string('a', 5001) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("validation_failed", await tooLong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName));

        // The cap is > 5000 refused, not >=: exactly 5000 is still a post (AC-06 words it "maior que").
        var atTheCap = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = new string('b', 5000) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, atTheCap.StatusCode);
        Assert.Equal(before + 1, await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName));
    }

    [Fact] // AC-06, sanitization (GAP-24)
    public async Task Html_and_c0_controls_are_sanitized_on_the_server_before_anything_is_stored()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac06c");
        var slug = PostRoutes.Slug("limpo");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Limpo", slug }, owner.Csrf)).StatusCode);

        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts",
            new { content = "<b>oi</b> & tá\x07\x00 ok\nlinha" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var content = json.RootElement.GetProperty("content").GetString()!;
        Assert.DoesNotContain("<b>", content, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;oi&lt;/b&gt;", content, StringComparison.Ordinal);
        Assert.Contains("&amp;", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\x07", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\x00", content, StringComparison.Ordinal);
        Assert.Contains("\nlinha", content, StringComparison.Ordinal);

        // And it is the stored form, not just the response: a read returns exactly the same text.
        var id = json.RootElement.GetProperty("id").GetString();
        var read = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(content, readJson.RootElement.GetProperty("content").GetString());
    }
}
