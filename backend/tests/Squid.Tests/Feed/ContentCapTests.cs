using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Feed;

/// <summary>
/// TASK-0025 AC-08 — the F1 correction of QA-0022: the content cap is held against the SANITIZED
/// text as well, because escaping multiplies it and data-model §2.5 caps the PERSISTED
/// <c>Content</c> at 5000. The three cases of the criterion — raw body above the cap (the early
/// check keeps answering), a raw body at or below the cap whose escaped form crosses it (400 with
/// nothing written, on both the publish and the reply route), and a body that escapes to at most
/// 5000 (publishes normally) — each proven on the persisted rows, not just on the status line.
/// </summary>
[Collection("ServiceHost")]
public sealed class ContentCapTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ContentCapTests(AuthHostFixture host) => _host = host;

    private IActivityStore Activities =>
        _host.App.Services.GetRequiredService<IActivityStore>();

    [Fact] // AC-08, case 1 — corpo bruto > 5000 continua 400 na checagem cedo
    public async Task Ac08a_raw_body_above_the_cap_is_400_and_persists_nothing()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "fac08bruto");
        var slug = await NewContainerAsync(owner);
        var objectsBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName);
        var eventsBefore = (await Activities.ReadAsync()).Count;

        var refused = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = new string('x', 5001) }, owner.Csrf);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("validation_failed",
            await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertNothingPersistedAsync(objectsBefore, eventsBefore);
    }

    [Fact] // AC-08, case 2 — corpo bruto ≤ 5000 que escapa para cima do cap: 400, nada gravado
    public async Task Ac08b_body_escaping_above_the_cap_is_400_and_persists_nothing()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "fac08escapa");
        var slug = await NewContainerAsync(owner);

        // O pai da resposta existe; os contadores são capturados DEPOIS dele, porque os três
        // recusos abaixo não podem mover linha alguma (data-model §2.5: Content ≤ 5000 guardado).
        var parent = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "pai do teste" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, parent.StatusCode);
        var parentId = (await FeedHarness.JsonOfAsync(parent)).GetProperty("id").GetString()!;
        var objectsBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName);
        var eventsBefore = (await Activities.ReadAsync()).Count;

        // O repro da QA-0022/F1: 5000 "&" cabem no cap bruto e viram 25000 escapados.
        var amplified = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = new string('&', 5000) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, amplified.StatusCode);
        Assert.Contains("validation_failed",
            await amplified.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertNothingPersistedAsync(objectsBefore, eventsBefore);

        // Fronteira degrau: 1001 "&" (< 5000 brutos) escapam para 5005 — também recusado.
        var stepped = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = new string('&', 1001) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, stepped.StatusCode);
        await AssertNothingPersistedAsync(objectsBefore, eventsBefore);

        // A mesma regra no segundo call site: a resposta também é recusada, sem gravar.
        var reply = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/posts/{Uri.EscapeDataString(parentId)}/replies",
            new { content = new string('<', 4000) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        Assert.Contains("validation_failed",
            await reply.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertNothingPersistedAsync(objectsBefore, eventsBefore);

        // E nada apareceu no feed: os recusos não projetam linha alguma.
        await FeedHarness.ProjectPendingAsync(_host);
        var feed = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}/feed");
        var items = FeedHarness.Items(await FeedHarness.JsonOfAsync(feed));
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("pai do teste", FeedHarness.Contents(items)[0]);
    }

    [Fact] // AC-08, case 3 — corpo que escapa para ≤ 5000 publica normal
    public async Task Ac08c_body_escaping_to_the_cap_publishes_normally()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "fac08limite");
        var slug = await NewContainerAsync(owner);

        // A fronteira exata: 1000 "&" escapam para EXATAMENTE 5000 — ainda publica.
        var atCap = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = new string('&', 1000) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, atCap.StatusCode);

        var view = await FeedHarness.JsonOfAsync(atCap);
        var stored = view.GetProperty("content").GetString()!;
        Assert.Equal(5000, stored.Length);
        Assert.Equal(string.Concat(Enumerable.Repeat("&amp;", 1000)), stored);

        // O campo PERSISTIDO é o que passou do cap (data-model §2.5): a leitura devolve o mesmo texto.
        var read = await PostRoutes.SendAsync(owner, HttpMethod.Get,
            $"/api/posts/{Uri.EscapeDataString(view.GetProperty("id").GetString()!)}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBack = await FeedHarness.JsonOfAsync(read);
        Assert.Equal(stored, readBack.GetProperty("content").GetString());

        // Abaixo da fronteira com folga: também publica, com o texto escapado intacto.
        var below = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts",
            new { content = new string('&', 999) + "fim" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, below.StatusCode);
        var belowView = await FeedHarness.JsonOfAsync(below);
        var belowStored = belowView.GetProperty("content").GetString()!;
        Assert.Equal(4998, belowStored.Length);
        Assert.EndsWith("fim", belowStored, StringComparison.Ordinal);
    }

    /// <summary>The proof of "nada gravado": the object rows and the activity log both unmoved.</summary>
    private async Task AssertNothingPersistedAsync(int objects, int events)
    {
        Assert.Equal(objects, await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName));
        Assert.Equal(events, (await Activities.ReadAsync()).Count);
    }

    /// <summary>One group container, owned by the caller (born MembersOnly + Invite, unlisted).</summary>
    private static async Task<string> NewContainerAsync(AuthClient owner)
    {
        var slug = PostRoutes.Slug("fac08");
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = slug, slug }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return slug;
    }
}
