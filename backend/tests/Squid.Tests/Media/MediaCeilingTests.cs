using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Squid.Api.Auth;
using Squid.Tests.Auth;
using Squid.Tests.Feed;

namespace Squid.Tests.Media;

/// <summary>
/// AC-02 sub-case corrected after QA-0026 (achado 1): with the default limits the video/audio
/// limit (50 MiB) used to EQUAL the body ceiling, so a file above its own type never reached the
/// route — Kestrel answered <c>413 Payload Too Large</c> with <c>Content-Length: 0</c> (no §1
/// envelope) and a streaming client saw only a connection reset. The ceiling now carries headroom
/// over the largest per-type limit, so the route reads the file and answers the contract's
/// envelope; past the absolute backstop the route catches Kestrel's refusal and answers the same
/// envelope. <c>Media:MaxBytes:*</c> semantics are untouched: the limit itself still decides.
/// </summary>
[Collection("ServiceHost")]
public sealed class MediaCeilingTests : IClassFixture<MediaHostFixture>
{
    private readonly MediaHostFixture _host;

    public MediaCeilingTests(MediaHostFixture host) => _host = host;

    [Fact] // AC-02 — a file AT the per-type limit uploads; a file just above it is 413 with the envelope
    public async Task VideoAtTheLimit_Uploads_VideoAboveIt_Is413WithEnvelope()
    {
        var (client, _) = await MediaHarness.SignedInAsync(_host, "ceil1");

        // AT the limit: exactly 50 MiB of MP4 is a valid video — the ceiling must have room for
        // the file plus its multipart envelope (this upload died at Kestrel's empty 413 before).
        var noLimite = MediaHarness.Mp4WithDuration(600, 6000, 50 * 1024 * 1024);
        Assert.Equal(50 * 1024 * 1024, noLimite.Length);
        var subiu = await MediaHarness.UploadAsync(client, noLimite, "vídeo no limite", "video/mp4", "limite.mp4");
        Assert.Equal(HttpStatusCode.OK, subiu.StatusCode);
        Assert.Equal(10, (await FeedHarness.JsonOfAsync(subiu)).GetProperty("durationSeconds").GetInt32());

        // ABOVE the limit: 51 MiB must arrive at the route and answer 413 with the §1 envelope.
        // An empty Kestrel 413 (Content-Length: 0) or a dropped connection fails both asserts.
        var acima = MediaHarness.Mp4WithDuration(600, 6000, (50 * 1024 * 1024) + (1024 * 1024));
        var recusa = await MediaHarness.UploadAsync(client, acima, "vídeo acima do limite", "video/mp4", "acima.mp4");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, recusa.StatusCode);
        var corpo = await FeedHarness.JsonOfAsync(recusa);
        Assert.Equal("payload_too_large", corpo.GetProperty("error").GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("error").GetProperty("traceId").GetString()));
    }

    [Fact] // AC-02 — past the absolute backstop the route still answers the envelope, never an empty 413
    public async Task BodyAboveTheAbsoluteCeiling_StillAnswersTheEnvelope413()
    {
        var (client, _) = await MediaHarness.SignedInAsync(_host, "ceil2");

        // The raw probe of QA-0026, corrected: a request that DECLARES 1 GiB (far past the
        // ceiling) and sends no body byte. Kestrel refuses the body before reading it; the route
        // must put the §1 envelope on the wire instead of Kestrel's empty "413 + Content-Length: 0".
        var baseUri = _host.Http.BaseAddress!;
        using var socket = new TcpClient();
        await socket.ConnectAsync(baseUri.Host, baseUri.Port);
        var stream = socket.GetStream();

        var pedido = string.Join("\r\n",
            "POST /api/media HTTP/1.1",
            $"Host: {baseUri.Authority}",
            "Content-Type: multipart/form-data; boundary=ceiling",
            "Content-Length: 1073741824",
            $"Cookie: {AuthSessions.SessionCookie}={client.Session}; {AuthSessions.CsrfCookie}={client.Csrf}",
            $"{AuthSessions.CsrfHeader}: {client.Csrf}",
            "Connection: close",
            string.Empty,
            string.Empty);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(pedido));

        // Read until the server closes: a hang here (no answer at all) fails the test on timeout
        // instead of passing silently — the envelope has to arrive on the wire.
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var resposta = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var lidos = await stream.ReadAsync(buffer, limite.Token);
            if (lidos == 0) break;
            resposta.Write(buffer, 0, lidos);
        }

        var texto = Encoding.UTF8.GetString(resposta.ToArray());
        Assert.StartsWith("HTTP/1.1 413", texto);

        // The envelope is the whole point: Kestrel frames small responses as chunked, so the JSON
        // is taken from its first { to its last } — an empty body (the old Kestrel 413, the
        // regression this test guards) has no brace and fails with the raw response attached.
        var inicio = texto.IndexOf('{');
        var fim = texto.LastIndexOf('}');
        Assert.True(inicio >= 0 && fim > inicio, $"a resposta 413 não carrega o envelope JSON: {texto}");
        JsonDocument? documento = null;
        try
        {
            documento = JsonDocument.Parse(texto[inicio..(fim + 1)]);
        }
        catch (JsonException)
        {
            // the raw response below goes into the failure message
        }

        Assert.True(documento is not null, $"a resposta 413 não carrega o envelope JSON: {texto}");
        var erro = documento!.RootElement.GetProperty("error");
        Assert.Equal("payload_too_large", erro.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(erro.GetProperty("traceId").GetString()));
    }
}
