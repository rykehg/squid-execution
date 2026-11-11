using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Posts;
using Squid.Tests.Feed;
using Squid.Tests.Media;
using Squid.Tests.Posts;

namespace Squid.Tests.Unfurl;

/// <summary>
/// The hard spots of TASK-0029 QA-0029: the SSRF perimeter against a name that RESOLVES to a
/// private address (not just a literal), the re-validation of every redirect hop against the
/// resolver, the manual hop budget, the fail-open cap of 10 attachments, the reply path that
/// stays parked, the media-index row a Link attachment must never create, and the AC-05 claim
/// that both unfurl seams are the fakes — so no scenario here can reach DNS or a socket.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0029Tests : IClassFixture<UnfurlHostFixture>
{
    private const string Page = """
        <html><head><title>fora do escopo</title>
        <meta property="og:title" content="Título do artigo">
        <meta property="og:description" content="Resumo &amp; mais">
        <meta property="og:image" content="https://cdn.example.com/capa.png">
        </head><body>corpo</body></html>
        """;

    private readonly UnfurlHostFixture _host;

    public QaTask0029Tests(UnfurlHostFixture host) => _host = host;

    [Theory] // AC-03 — o perímetro contra um IP RESOLVIDO, não só contra o literal
    [InlineData("10.1.2.3")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("172.16.0.9")]
    [InlineData("fd00::1")] // resposta AAAA sozinha: unique-local
    [InlineData("::1")]
    public async Task A_name_that_resolves_only_to_private_answers_never_fetches(string answer)
    {
        _host.Reset();
        _host.Resolver.Map("interno.test", answer);
        _host.Handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page, Encoding.UTF8, "text/html"),
        }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "qa03a");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "https://interno.test/pagina");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var view = await FeedHarness.JsonOfAsync(created);
        Assert.Empty(view.GetProperty("attachments").EnumerateArray());
        Assert.Empty(_host.Handler.Requests); // recusado antes de qualquer fetch
    }

    [Fact] // AC-03 — revalidação por salto contra IP resolvido: o 2º salto nunca é pedido
    public async Task A_redirect_to_a_name_that_resolves_private_is_refused_mid_chain()
    {
        _host.Reset();
        _host.Resolver.Map("public.test", "93.184.216.34");
        _host.Resolver.Map("interno.test", "10.0.0.5");
        _host.Handler.RespondWith((request, _) =>
        {
            if (request.RequestUri!.Host == "public.test")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("http://interno.test/segundo");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Page, Encoding.UTF8, "text/html"),
            });
        });

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "qa03b");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "http://public.test/primeiro");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var view = await FeedHarness.JsonOfAsync(created);
        Assert.Empty(view.GetProperty("attachments").EnumerateArray());
        Assert.Single(_host.Handler.Requests); // só o 1º salto; o alvo privado, nunca
        Assert.Equal("http://public.test/primeiro", _host.Handler.Requests.Single().ToString());
    }

    [Fact] // AC-03 — uma cadeia pública legítima é seguida e o card sai (par do cenário acima)
    public async Task A_public_redirect_chain_is_followed_and_the_card_comes_out()
    {
        _host.Reset();
        _host.Resolver.Map("a.test", "93.184.216.34");
        _host.Resolver.Map("b.test", "93.184.216.35");
        _host.Handler.RespondWith((request, _) =>
        {
            if (request.RequestUri!.Host == "a.test")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("http://b.test/final");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Page, Encoding.UTF8, "text/html"),
            });
        });

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "qa03c");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "http://a.test/primeiro");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var view = await FeedHarness.JsonOfAsync(created);
        var attachment = Assert.Single(view.GetProperty("attachments").EnumerateArray());
        Assert.Equal("link", attachment.GetProperty("kind").GetString());
        Assert.Equal("Título do artigo", attachment.GetProperty("alt").GetString());          // og:title
        Assert.Equal("Resumo & mais", attachment.GetProperty("blurHash").GetString());        // og:description
        Assert.Equal("https://cdn.example.com/capa.png", attachment.GetProperty("url").GetString()); // og:image
        Assert.Equal(2, _host.Handler.Requests.Count); // 1º salto + página final
    }

    [Fact] // decisão DEV: no máximo 3 redirects seguidos à mão; passando disso, fail-open
    public async Task A_chain_beyond_the_three_hop_budget_gives_up_and_publishes_the_url_alone()
    {
        _host.Reset();
        var hops = 0;
        _host.Handler.RespondWith((_, _) =>
        {
            hops++;
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri($"http://93.184.216.34/h{hops}");
            return Task.FromResult(redirect);
        });

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "qa04c");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "http://93.184.216.34/raiz");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var view = await FeedHarness.JsonOfAsync(created);
        Assert.Empty(view.GetProperty("attachments").EnumerateArray());
        Assert.Equal(4, _host.Handler.Requests.Count); // MaxRedirects = 3 → 4 fetches e desiste
    }

    [Fact] // AC-05 — as duas costuras do unfurl são os fakes: nada aqui pode sair na rede
    public async Task The_host_resolves_both_unfurl_seams_to_the_fakes()
    {
        _host.Reset();
        Assert.IsType<FakeUnfurlHandler>(_host.App.Services.GetRequiredService<HttpMessageHandler>());
        Assert.IsType<FakeUnfurlResolver>(_host.App.Services.GetRequiredService<IUnfurlResolver>());
        Assert.IsType<LinkUnfurler>(_host.App.Services.GetRequiredService<LinkUnfurler>());

        _host.Handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page, Encoding.UTF8, "text/html"),
        }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "qa05");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "https://nome-nunca-mapeado.test/x");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var view = await FeedHarness.JsonOfAsync(created);
        Assert.Empty(view.GetProperty("attachments").EnumerateArray());
        Assert.Empty(_host.Handler.Requests); // costura recusou sem consultar DNS nenhum
    }

    [Fact] // decisão DEV: SqliteObjectStore pula a linha do índice de mídia quando contentHash é vazio
    public async Task A_link_attachment_never_borns_a_media_index_row()
    {
        _host.Reset();
        _host.Resolver.Map("example.com", "93.184.216.34");
        _host.Handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page, Encoding.UTF8, "text/html"),
        }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "qa06");

        var linked = await UnfurlHarness.PublishAsync(owner, slug, "https://example.com/artigo");
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
        var linkedView = await FeedHarness.JsonOfAsync(linked);
        var link = Assert.Single(linkedView.GetProperty("attachments").EnumerateArray());
        Assert.Equal("link", link.GetProperty("kind").GetString());
        Assert.Equal(string.Empty, link.GetProperty("contentHash").GetString());

        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(7, 5), "qa0029");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var hash = FeedHarness.Text(await FeedHarness.JsonOfAsync(upload), "contentHash");
        Assert.False(string.IsNullOrEmpty(hash));

        var withMedia = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "post com mídia", contentHashes = new[] { hash } }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, withMedia.StatusCode);

        await using var connection = new SqliteConnection($"Data Source={_host.StoragePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GrainId FROM GrainState WHERE StoreName = 'post-media-ref'";
        var rows = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(0));
            }
        }

        // Exatamente UMA linha: a do post com mídia. Se a do Link existisse, seriam duas.
        var only = Assert.Single(rows);
        Assert.Contains(hash!, only, StringComparison.Ordinal);

        DeleteBlobs(hash!);
    }

    [Fact] // decisão DEV: o card só entra se couber no teto de 10 anexos (§2.5)
    public async Task A_post_already_full_of_media_does_not_gain_the_link_card()
    {
        _host.Reset();
        _host.Resolver.Map("example.com", "93.184.216.34");
        _host.Handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page, Encoding.UTF8, "text/html"),
        }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "qa07");
        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(11, 6), "qa0029cap");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var hash = FeedHarness.Text(await FeedHarness.JsonOfAsync(upload), "contentHash");
        Assert.False(string.IsNullOrEmpty(hash));

        var created = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "https://example.com/artigo", contentHashes = Enumerable.Repeat(hash!, 10) },
            owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var view = await FeedHarness.JsonOfAsync(created);
        var attachments = view.GetProperty("attachments").EnumerateArray().ToArray();
        Assert.Equal(10, attachments.Length);
        Assert.All(attachments, a => Assert.Equal("image", a.GetProperty("kind").GetString()));
        Assert.Single(_host.Handler.Requests); // o unfurl rodou; o teto derrubou o card

        DeleteBlobs(hash!);
    }

    [Fact] // decisão DEV: o unfurl roda só no create — replies ficam no MVP sem card
    public async Task A_reply_whose_content_is_a_url_stays_attachment_less()
    {
        _host.Reset();
        _host.Resolver.Map("example.com", "93.184.216.34");
        _host.Handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page, Encoding.UTF8, "text/html"),
        }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "qa08");
        var parent = await UnfurlHarness.PublishAsync(owner, slug, "https://example.com/pai");
        Assert.Equal(HttpStatusCode.Created, parent.StatusCode);
        var parentId = (await FeedHarness.JsonOfAsync(parent)).GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(parentId));

        var reply = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/posts/{Uri.EscapeDataString(parentId!)}/replies",
            new { content = "https://example.com/resposta" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);

        var view = await FeedHarness.JsonOfAsync(reply);
        Assert.Empty(view.GetProperty("attachments").EnumerateArray());
        Assert.Single(_host.Handler.Requests); // só o pai foi buscado; a resposta, nunca
    }

    /// <summary>The blob volume of the default <c>Media:BlobRoot</c> — the bytes this test wrote are its own to remove.</summary>
    private static void DeleteBlobs(string hash)
    {
        var blobRoot = Path.Combine(AppContext.BaseDirectory, "media-blobs");
        if (!Directory.Exists(blobRoot)) return;
        foreach (var file in Directory.EnumerateFiles(blobRoot, "*", SearchOption.AllDirectories))
        {
            if (file.Contains(hash, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
        }
    }
}
