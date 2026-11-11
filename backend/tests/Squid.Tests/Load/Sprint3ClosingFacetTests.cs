using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.ReadModels.Projections;
using Squid.Tests.Auth;
using Squid.Tests.Feed;
using Squid.Tests.Posts;
using Squid.Tests.Stream;

namespace Squid.Tests.Load;

/// <summary>
/// AC-03 of TASK-0032 — the Sprint 3 closing DoD facet in ONE end-to-end HTTP flow over the
/// routes the previous deliveries left behind: (a) a post in a community reaches the connected
/// SSE subscriber in under a second; (b) <c>?since=</c> answers exactly the delta — never a
/// repeat — on BOTH <c>GET feed</c> and <c>GET /api/timeline</c>; (c) once the container goes
/// unlisted it stops reaching the non-member everywhere it could have arrived: the byte-identical
/// 404 on the feed, no trace of it in the outsider's own timeline, and no frame on the outsider's
/// live stream — whose heartbeat proves that connection is alive while it hears nothing.
/// </summary>
[Collection("ServiceHost")]
public sealed class Sprint3ClosingFacetTests : IClassFixture<StreamHostFixture>
{
    private readonly StreamHostFixture _host;

    public Sprint3ClosingFacetTests(StreamHostFixture host) => _host = host;

    [Fact] // AC-03 — (a) + (b) + (c), um único fluxo HTTP
    public async Task Ac03_the_post_reaches_the_subscriber_under_a_second_since_answers_only_the_delta_and_unlisted_never_reaches_a_non_member()
    {
        var (author, _) = await StreamHarness.SignedInAsync(_host, "facet3dono");
        var (member, _) = await StreamHarness.SignedInAsync(_host, "facet3membro");
        var (outsider, _) = await StreamHarness.SignedInAsync(_host, "facet3fora");
        var slug = await StreamHarness.NewContainerAsync(author, "facet3");
        await StreamHarness.JoinAsync(member, slug);
        var origin = $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}";
        var containerId = $"{origin}/c/{slug}";

        using var memberStream = await SseConnection.OpenAsync(_host, member);
        using var outsiderStream = await SseConnection.OpenAsync(_host, outsider);

        // (a) O DoD do Sprint 3: post em comunidade chega ao assinante conectado em < 1 s.
        var live = StreamHarness.Unique("facet-ao-vivo");
        var watch = Stopwatch.StartNew();
        var livePost = await StreamHarness.PublishAsync(author, slug, live);
        var delivered = await memberStream.ReadUntilAsync(Matches(live), TimeSpan.FromSeconds(5));
        watch.Stop();
        var frame = Assert.Single(delivered, Matches(live));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1),
            $"o DoD do Sprint 3 pede menos de 1 s no fio, esta entrega levou {watch.Elapsed.TotalMilliseconds:F0} ms");
        Assert.Equal("post", frame.Event);
        Assert.Equal(
            (await StreamHarness.GlobalSeqAsync(_host, livePost)).ToString(CultureInfo.InvariantCulture),
            frame.Id);

        // (b) O delta de ?since= lê as projeções — o passo do pump à mão, porque o host de teste
        // não roda pump (R-18: projeção fora do caminho de escrita).
        await ProjectPendingAsync();

        var feedPage = await ReadOkAsync(member, $"/api/containers/{slug}/feed");
        var feedCursor = feedPage.GetProperty("nextSince").GetInt64();
        Assert.Equal(new[] { live }, FeedHarness.Contents(FeedHarness.Items(feedPage)));

        var timelinePage = await ReadOkAsync(member, "/api/timeline");
        var timelineCursor = timelinePage.GetProperty("nextSince").GetInt64();
        Assert.Equal(new[] { live }, FeedHarness.Contents(FeedHarness.Items(timelinePage)));

        var delta = StreamHarness.Unique("facet-delta");
        var deltaPost = await StreamHarness.PublishAsync(author, slug, delta);
        var deltaSeq = await StreamHarness.GlobalSeqAsync(_host, deltaPost);
        await ProjectPendingAsync();

        var feedDelta = await ReadOkAsync(member, $"/api/containers/{slug}/feed?since={feedCursor}");
        var feedDeltaItems = FeedHarness.Items(feedDelta);
        Assert.Equal(new[] { delta }, FeedHarness.Contents(feedDeltaItems)); // só o delta…
        Assert.Equal(new[] { deltaSeq }, FeedHarness.Seqs(feedDeltaItems)); // …na ordem, sem repetir

        var timelineDelta = await ReadOkAsync(member, $"/api/timeline?since={timelineCursor}");
        Assert.Equal(new[] { delta }, FeedHarness.Contents(FeedHarness.Items(timelineDelta)));

        // (c) Pré-condição: o leitor de fora LÊ o feed público AGORA — o 404 abaixo nasce do
        // unlisted, nunca de uma invisibilidade que já existia antes da virada de política.
        var beforeHiding = await PostRoutes.SendAsync(outsider, HttpMethod.Get, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, beforeHiding.StatusCode);
        Assert.Contains(
            FeedHarness.Contents(FeedHarness.Items(await FeedHarness.JsonOfAsync(beforeHiding))),
            text => text == live);

        // Unlisted: o contêiner some para o não-membro — em toda rota por onde chegaria.
        await StreamHarness.SetVisibilityAsync(author, slug, "unlisted");
        var hidden = StreamHarness.Unique("facet-unlisted");
        await StreamHarness.PublishAsync(author, slug, hidden);
        await ProjectPendingAsync();

        // Feed: o 404 byte-idêntico ao contêiner inexistente (SC-005), nunca um corpo diferente.
        var refused = await PostRoutes.SendAsync(outsider, HttpMethod.Get, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        var absent = await PostRoutes.SendAsync(outsider, HttpMethod.Get,
            $"/api/containers/{PostRoutes.Slug("facet3ausente")}/feed");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await FeedHarness.BytesOf(absent), await FeedHarness.BytesOf(refused));

        // Timeline: o próprio terceiro tem timeline viva, e ela nunca carrega o unlisted.
        var ownSlug = await StreamHarness.NewContainerAsync(outsider, "facet3terceiro");
        var own = StreamHarness.Unique("facet-do-terceiro");
        await StreamHarness.PublishAsync(outsider, ownSlug, own);
        await ProjectPendingAsync();
        var outsiderPage = await ReadOkAsync(outsider, "/api/timeline");
        var outsiderItems = FeedHarness.Items(outsiderPage);
        Assert.Contains(FeedHarness.Contents(outsiderItems), text => text == own);
        Assert.DoesNotContain(FeedHarness.ContainerIds(outsiderItems), id => id == containerId);
        Assert.DoesNotContain(FeedHarness.Contents(outsiderItems), text => text == hidden);

        // Stream: o unlisted nunca chega ao não-membro — e os pings provam que a conexão vive.
        // Janela de 1,5 s, não 1500 s: o hidden foi publicado ANTES desta leitura, então um
        // vazamento já estaria enfileirado no fio do outsider; os pings de 0,2 s do host de
        // teste chegam folgados nessa janela e é a prova de vida da conexão.
        var frames = await outsiderStream.ReadUntilAsync(Matches(hidden), TimeSpan.FromMilliseconds(1500));
        Assert.DoesNotContain(frames, Matches(hidden));
        Assert.Contains(frames, seen => seen.Event == "ping");

        // E o membro continua lendo: a invisibilidade atinge o não-membro, não o contêiner.
        var memberFeed = await PostRoutes.SendAsync(member, HttpMethod.Get, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, memberFeed.StatusCode);
        Assert.Contains(
            FeedHarness.Contents(FeedHarness.Items(await FeedHarness.JsonOfAsync(memberFeed))),
            text => text == hidden);
    }

    /// <summary>The pump's tick by hand: the test host registers the projector but never starts it (R-18).</summary>
    private async Task ProjectPendingAsync()
    {
        var projector = _host.App.Services.GetRequiredService<IActivityProjector>();
        var log = _host.App.Services.GetRequiredService<SqliteActivityLog>();
        var pending = await log.ReadAsync(await projector.LastProjectedSeqAsync());
        foreach (var entry in pending)
        {
            await projector.ProjectAsync(entry);
        }
    }

    /// <summary>One route answer that must be a 200 page body.</summary>
    private static async Task<JsonElement> ReadOkAsync(AuthClient reader, string path)
    {
        var response = await PostRoutes.SendAsync(reader, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await FeedHarness.JsonOfAsync(response);
    }

    /// <summary>The frame carrying one unique content — heartbeats and foreign posts never match.</summary>
    private static Predicate<SseFrame> Matches(string content) =>
        seen => seen.Data is not null && seen.Data.Contains(content, StringComparison.Ordinal);
}
