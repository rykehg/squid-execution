using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Squid.Tests.Posts;

namespace Squid.Tests.Unfurl;

/// <summary>
/// AC-01..AC-04 of TASK-0029 over HTTP against the host the fixture builds: the OpenGraph card
/// of a lone-URL publish (AC-01), the fail-open on timeout and on a failed fetch (AC-02), the
/// SSRF perimeter — literals, schemes, redirects and resolver answers (AC-03) — and the body
/// cap (AC-04). Every fetch goes through the fake handler; nothing here opens a socket (AC-05).
/// </summary>
[Collection("ServiceHost")]
public sealed class UnfurlPublishTests : IClassFixture<UnfurlHostFixture>
{
    private const string Page = """
        <html><head><title>fora do escopo</title>
        <meta property="og:title" content="Título do artigo">
        <meta property="og:description" content="Resumo &amp; mais">
        <meta property="og:image" content="https://cdn.example.com/capa.png">
        </head><body>corpo</body></html>
        """;

    private readonly UnfurlHostFixture _host;

    public UnfurlPublishTests(UnfurlHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task A_post_whose_content_is_one_url_publishes_the_opengraph_card()
    {
        _host.Reset();
        _host.Resolver.Map("example.com", "93.184.216.34");
        _host.Handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page, Encoding.UTF8, "text/html"),
        }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "ac01");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "https://example.com/artigo?x=1&y=2");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var root = json.RootElement;

        // The stored text keeps the sanitizer's form (GAP-24) — the RAW url is what got fetched.
        Assert.Equal("https://example.com/artigo?x=1&amp;y=2", root.GetProperty("content").GetString());

        var attachment = Assert.Single(root.GetProperty("attachments").EnumerateArray());
        Assert.Equal("link", attachment.GetProperty("kind").GetString());
        Assert.Equal("Título do artigo", attachment.GetProperty("alt").GetString());          // og:title
        Assert.Equal("Resumo & mais", attachment.GetProperty("blurHash").GetString());        // og:description
        Assert.Equal("https://cdn.example.com/capa.png", attachment.GetProperty("url").GetString()); // og:image, external
        Assert.Equal(string.Empty, attachment.GetProperty("contentHash").GetString());        // §2.6: Link has no blob
        Assert.Equal("text/html", attachment.GetProperty("mimeType").GetString());

        // Exactly one fetch, to the url as written — the escaped stored form is never requested.
        var sent = Assert.Single(_host.Handler.Requests);
        Assert.Equal("https://example.com/artigo?x=1&y=2", sent.ToString());

        // The card survives the round trip through the store (GET /api/posts/{id}).
        var id = root.GetProperty("id").GetString();
        var read = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var stored = Assert.Single(readJson.RootElement.GetProperty("attachments").EnumerateArray());
        Assert.Equal("Título do artigo", stored.GetProperty("alt").GetString());
        Assert.Equal("Resumo & mais", stored.GetProperty("blurHash").GetString());
        Assert.Equal("https://cdn.example.com/capa.png", stored.GetProperty("url").GetString());
    }

    [Fact] // AC-02 — a handler that never answers
    public async Task A_fetch_that_never_answers_publishes_the_url_alone()
    {
        _host.Reset();
        _host.Handler.RespondWith(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable: the delay only returns on cancellation");
        });

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "ac02a");
        var watch = Stopwatch.StartNew();
        var created = await UnfurlHarness.PublishAsync(owner, slug, "http://93.184.216.34/pagina");
        watch.Stop();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("attachments").EnumerateArray());
        Assert.Single(_host.Handler.Requests); // the fetch was attempted, then given up
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10),
            $"fail-open must not hold the publish: took {watch.Elapsed}");
    }

    [Fact] // AC-02 — a fetch that fails
    public async Task A_failed_fetch_publishes_the_url_alone()
    {
        _host.Reset();
        _host.Handler.RespondWith((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "ac02b");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "http://93.184.216.34/quebrado");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("attachments").EnumerateArray());
        Assert.Single(_host.Handler.Requests);
    }

    [Theory] // AC-03 — perimetro: literais privados e esquemas fora de http/https
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.1.2.3/")]
    [InlineData("http://192.168.1.10/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/arquivo")]
    public async Task A_url_outside_the_perimeter_never_reaches_the_fetch(string content)
    {
        _host.Reset();
        _host.Handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<meta property=\"og:title\" content=\"nunca\">",
                Encoding.UTF8, "text/html"),
        }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "ac03a");
        var created = await UnfurlHarness.PublishAsync(owner, slug, content);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("attachments").EnumerateArray());
        Assert.Empty(_host.Handler.Requests); // AC-03: nenhum fetch — o handler nunca é chamado
    }

    [Theory] // AC-03 — revalidação por salto: o segundo salto nunca é pedido
    [InlineData("http://192.168.0.10/second")]
    [InlineData("http://[::1]/second")]
    [InlineData("file:///etc/passwd")]
    public async Task A_redirect_outside_the_perimeter_is_never_followed(string location)
    {
        _host.Reset();
        _host.Handler.RespondWith((_, _) =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri(location, UriKind.Absolute);
            return Task.FromResult(redirect);
        });

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "ac03b");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "http://93.184.216.34/primeiro");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("attachments").EnumerateArray());
        Assert.Single(_host.Handler.Requests); // só o primeiro salto; o alvo privado, nunca
    }

    [Fact] // AC-03 — TODAS as respostas do resolver são validadas: uma privada recusa a rodada
    public async Task A_host_with_even_one_private_answer_never_fetches()
    {
        _host.Reset();
        _host.Resolver.Map("interno.test", "93.184.216.34", "127.0.0.1");
        _host.Handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page, Encoding.UTF8, "text/html"),
        }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "ac03c");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "https://interno.test/pagina");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("attachments").EnumerateArray());
        Assert.Empty(_host.Handler.Requests);
    }

    [Fact] // AC-04 — Content-Length acima do teto: nem um byte do corpo é lido
    public async Task A_declared_body_above_the_cap_is_never_read()
    {
        _host.Reset();
        var content = new CountingContent(new byte[8192], declareLength: true); // teto do host: 4096
        _host.Handler.RespondWith((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "ac04a");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "http://93.184.216.34/grande");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("attachments").EnumerateArray());
        Assert.Equal(0, content.BytesRead);
    }

    [Fact] // AC-04 — corpo sem Content-Length: a leitura para no teto e o fetch aborta
    public async Task A_streamed_body_above_the_cap_is_cut_at_the_limit()
    {
        _host.Reset();
        var content = new CountingContent(new byte[8192], declareLength: false);
        _host.Handler.RespondWith((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "ac04b");
        var created = await UnfurlHarness.PublishAsync(owner, slug, "http://93.184.216.34/chunked");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("attachments").EnumerateArray());
        Assert.True(content.BytesRead <= 4096, $"read {content.BytesRead} bytes past the 4096 cap");
        Assert.True(content.BytesRead < 8192, "the whole body must not have been read");
    }

    [Fact] // gap row: texto com URL embutida — e texto puro — ficam sem attachment
    public async Task A_text_with_an_embedded_url_publishes_without_a_card()
    {
        _host.Reset();
        _host.Handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page, Encoding.UTF8, "text/html"),
        }));

        var (owner, slug) = await UnfurlHarness.ContainerAsync(_host, "gap26");
        var embedded = await UnfurlHarness.PublishAsync(owner, slug, "veja https://example.com/artigo agora");
        Assert.Equal(HttpStatusCode.Created, embedded.StatusCode);
        using var embeddedJson = JsonDocument.Parse(await embedded.Content.ReadAsStringAsync());
        Assert.Empty(embeddedJson.RootElement.GetProperty("attachments").EnumerateArray());

        var plain = await UnfurlHarness.PublishAsync(owner, slug, "texto comum, sem url nenhuma");
        Assert.Equal(HttpStatusCode.Created, plain.StatusCode);
        using var plainJson = JsonDocument.Parse(await plain.Content.ReadAsStringAsync());
        Assert.Empty(plainJson.RootElement.GetProperty("attachments").EnumerateArray());

        Assert.Empty(_host.Handler.Requests); // neither text ever left the host
    }

    /// <summary>Bytes the unfurler actually pulled, proven by the stream it reads from (AC-04).</summary>
    private sealed class CountingContent : HttpContent
    {
        private readonly byte[] _bytes;
        private readonly bool _declareLength;

        public CountingContent(byte[] bytes, bool declareLength)
        {
            _bytes = bytes;
            _declareLength = declareLength;
        }

        public long BytesRead { get; private set; }

        // `System.IO.Stream` spelled out: the sibling namespace Squid.Tests.Stream would otherwise win.
        protected override Task<System.IO.Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult<System.IO.Stream>(new CountingStream(_bytes, this));

        protected override Task SerializeToStreamAsync(System.IO.Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(_bytes, 0, _bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return _declareLength; // false = no Content-Length on the wire (a streamed body)
        }

        /// <summary>The live stream over the payload: every byte the reader takes is counted.</summary>
        private sealed class CountingStream(byte[] bytes, CountingContent owner) : System.IO.Stream
        {
            private int _position;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                var read = Math.Min(buffer.Length, bytes.Length - _position);
                bytes.AsSpan(_position, read).CopyTo(buffer);
                _position += read;
                owner.BytesRead += read;
                return read;
            }

            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(Read(buffer.Span));

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
