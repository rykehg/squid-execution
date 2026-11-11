using System.Net;
using System.Text.Json;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Posts;

/// <summary>
/// AC-04 and AC-05 of TASK-0023: reading a post answers it or the one 404 body shared with an
/// absent id (AC-04), a thread nests three answers while the fourth level answers
/// <c>422 invalid_reply_target</c>, and a target in another reader's container or gone entirely
/// is the same byte-identical 404 (AC-05).
/// </summary>
[Collection("ServiceHost")]
public sealed class PostReadAndReplyTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public PostReadAndReplyTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-04
    public async Task Reading_a_post_answers_it_or_the_shared_404_of_absent_and_invisible()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac04");
        var slug = PostRoutes.Slug("leitura");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Leitura", slug }, owner.Csrf)).StatusCode);

        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "post visível" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("id").GetString()!;
        var key = id[(id.LastIndexOf('/') + 1)..];

        // One row, both spellings: the opaque id of the route and the whole URI a client pastes back.
        var byKey = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{key}");
        var byUri = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{id}");
        Assert.Equal(HttpStatusCode.OK, byKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byUri.StatusCode);
        var visibleBody = await byKey.Content.ReadAsStringAsync();
        Assert.Equal(visibleBody, await byUri.Content.ReadAsStringAsync());
        using var visibleJson = JsonDocument.Parse(visibleBody);
        Assert.Equal("post visível", visibleJson.RootElement.GetProperty("content").GetString());
        Assert.Equal(1, visibleJson.RootElement.GetProperty("seq").GetInt64());

        // Invisible, absent and no session at all: one body, no traceId (FR-018, SC-005).
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "ac04estr");
        var invisible = await PostRoutes.SendAsync(stranger, HttpMethod.Get, $"/api/posts/{key}");
        var absent = await PostRoutes.SendAsync(stranger, HttpMethod.Get, "/api/posts/que-nao-existe");
        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        var invisibleBody = await invisible.Content.ReadAsStringAsync();
        var absentBody = await absent.Content.ReadAsStringAsync();
        Assert.Equal(invisibleBody, absentBody);
        Assert.Contains("not_found", invisibleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", invisibleBody, StringComparison.Ordinal);

        var anonymous = _host.NewClient();
        var anonymousBody = await (await anonymous.SendAsync(HttpMethod.Get, $"/api/posts/{key}"))
            .Content.ReadAsStringAsync();
        Assert.Equal(invisibleBody, anonymousBody);
    }

    [Fact] // AC-05, first half
    public async Task A_thread_publishes_three_answers_and_the_fourth_level_is_422()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac05a");
        var slug = PostRoutes.Slug("thread");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Thread", slug }, owner.Csrf)).StatusCode);

        var root = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "raiz" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, root.StatusCode);
        using var rootJson = JsonDocument.Parse(await root.Content.ReadAsStringAsync());
        var parentId = rootJson.RootElement.GetProperty("id").GetString()!;
        var containerId = rootJson.RootElement.GetProperty("containerId").GetString();

        // Depth 1, 2 and 3 publish — "profundidade 3 funciona" (AC-05).
        var parent = parentId;
        long expectedSeq = 2;
        for (var depth = 1; depth <= 3; depth++)
        {
            var answer = await PostRoutes.SendAsync(owner, HttpMethod.Post,
                $"/api/posts/{parent}/replies", new { content = $"resposta {depth}" }, owner.Csrf);
            Assert.Equal(HttpStatusCode.Created, answer.StatusCode);

            using var answerJson = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
            Assert.Equal(depth, answerJson.RootElement.GetProperty("depth").GetInt32());
            Assert.Equal(parent, answerJson.RootElement.GetProperty("replyToId").GetString());
            Assert.Equal(containerId, answerJson.RootElement.GetProperty("containerId").GetString());
            Assert.Equal(expectedSeq++, answerJson.RootElement.GetProperty("seq").GetInt64());
            parent = answerJson.RootElement.GetProperty("id").GetString()!;
        }

        // The fourth level — "4º nível → 422 invalid_reply_target" (AC-05, gap row of the task).
        var tooDeep = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/posts/{parent}/replies", new { content = "quarta" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooDeep.StatusCode);
        Assert.Contains("invalid_reply_target", await tooDeep.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact] // AC-05, second half
    public async Task Replying_to_a_post_of_another_container_or_a_gone_one_answers_the_same_404()
    {
        var (first, _) = await PostRoutes.SignedInAsync(_host, "ac05b1");
        var slugA = PostRoutes.Slug("um");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(first, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Um", slug = slugA }, first.Csrf)).StatusCode);
        var postA = await PostRoutes.SendAsync(first, HttpMethod.Post,
            $"/api/containers/{slugA}/posts", new { content = "do um" }, first.Csrf);
        Assert.Equal(HttpStatusCode.Created, postA.StatusCode);
        using var postAJson = JsonDocument.Parse(await postA.Content.ReadAsStringAsync());
        var idA = postAJson.RootElement.GetProperty("id").GetString()!;
        var keyA = idA[(idA.LastIndexOf('/') + 1)..];

        // Another author with their own container: the post of that first container is not theirs
        // to see (members-only, no membership), so it answers like nothing at all.
        var (second, _) = await PostRoutes.SignedInAsync(_host, "ac05b2");
        var foreign = await PostRoutes.SendAsync(second, HttpMethod.Post,
            $"/api/posts/{keyA}/replies", new { content = "de fora" }, second.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var absent = await PostRoutes.SendAsync(second, HttpMethod.Post,
            "/api/posts/que-nao-existe/replies", new { content = "de fora" }, second.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        // "Invisible": the container itself is gone behind its soft delete — even its owner stops
        // reaching the post, and the body stays the one an absent id answers with.
        var slugB = PostRoutes.Slug("dois");
        Assert.Equal(HttpStatusCode.Created,
            (await PostRoutes.SendAsync(second, HttpMethod.Post, "/api/containers",
                new { kind = "group", name = "Dois", slug = slugB }, second.Csrf)).StatusCode);
        var postB = await PostRoutes.SendAsync(second, HttpMethod.Post,
            $"/api/containers/{slugB}/posts", new { content = "do dois" }, second.Csrf);
        Assert.Equal(HttpStatusCode.Created, postB.StatusCode);
        using var postBJson = JsonDocument.Parse(await postB.Content.ReadAsStringAsync());
        var keyB = postBJson.RootElement.GetProperty("id").GetString()!;
        keyB = keyB[(keyB.LastIndexOf('/') + 1)..];

        var deleted = await PostRoutes.SendAsync(second, HttpMethod.Delete,
            $"/api/containers/{slugB}?confirm={slugB}", null, second.Csrf);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var hidden = await PostRoutes.SendAsync(second, HttpMethod.Post,
            $"/api/posts/{keyB}/replies", new { content = "sumiu" }, second.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        var foreignBody = await foreign.Content.ReadAsStringAsync();
        Assert.Equal(foreignBody, await absent.Content.ReadAsStringAsync());
        Assert.Equal(foreignBody, await hidden.Content.ReadAsStringAsync());
        Assert.Contains("not_found", foreignBody, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", foreignBody, StringComparison.Ordinal);
    }
}
