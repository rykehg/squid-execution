using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Squid.Api.Auth;
using Squid.Tests.Auth;
using Squid.Tests.Feed;
using Squid.Tests.Posts;
using Squid.Tests.Stream;
using Xunit.Abstractions;

namespace Squid.Tests.Media;

/// <summary>
/// QA-0026 (TASK-0027): the six acceptance criteria executed by QA, not re-read from the DEV
/// suites. Every fixture is built by this file — a real Skia-encoded JPEG carrying a synthetic,
/// structurally walkable EXIF/GPS APP1 with its own GPS canary, a hand-assembled GIF89a, an MP4
/// skeleton with a known <c>mvhd</c> — and AC-05 compares the server's blurHash against an
/// INDEPENDENT encoder written here from the reference algorithm, over the pixels of the bytes
/// the server actually served. No <c>MediaHarness</c> value is reused as an expectation: every
/// expected number comes from what this test itself produced or what the server answered.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0027Tests : IClassFixture<MediaHostFixture>
{
    /// <summary>The ASCII GPS canary this QA splices into its own EXIF payload — never DEV's marker.</summary>
    private const string Canario = "QA0026-GPS-CANARY-42";

    private readonly MediaHostFixture _host;

    private readonly ITestOutputHelper _output;

    public QaTask0027Tests(MediaHostFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    // ---------------------------------------------------------------- AC-01

    [Fact] // AC-01 — contrato §5 + blob por SHA-256 sem nenhuma tag EXIF/GPS
    public async Task Ac01_UploadDeJpegComExifReal_RespondeOContratoEServeBlobSemMetadados()
    {
        var dono = await RegistrarAsync("ac01");
        var original = JpegComExifSintetico(12, 6);

        // The fixture must be born dirty, and structurally real: IFD0 with Make/Orientation and a
        // GPS IFD behind the GPSInfo pointer — otherwise the byte scan below would pass for free.
        ValidarExifReal(original);
        Assert.True(Contem(original, Ascii("Exif\0\0")), "o jpeg de teste deve nascer com APP1 Exif");
        Assert.True(Contem(original, Ascii(Canario)), "o jpeg de teste deve nascer com o canário GPS");
        Assert.True(Contem(original, [0xFF, 0xE1]), "o jpeg de teste deve nascer com o marcador APP1");

        var resposta = await EnviarAsync(dono, original, "foto com GPS no EXIF");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var corpo = await FeedHarness.JsonOfAsync(resposta);
        var nomes = corpo.EnumerateObject().Select(propriedade => propriedade.Name)
            .OrderBy(nome => nome, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "blurHash", "contentHash", "durationSeconds", "height", "mimeType", "sizeBytes", "width" },
            nomes);

        var hash = FeedHarness.Text(corpo, "contentHash")!;
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal("image/jpeg", FeedHarness.Text(corpo, "mimeType"));
        Assert.Equal(12, corpo.GetProperty("width").GetInt32());
        Assert.Equal(6, corpo.GetProperty("height").GetInt32());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("durationSeconds").ValueKind);
        Assert.False(string.IsNullOrEmpty(FeedHarness.Text(corpo, "blurHash")));
        var tamanho = corpo.GetProperty("sizeBytes").GetInt64();
        Assert.True(tamanho > 0, "sizeBytes deve ser positivo");

        var servida = await dono.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        Assert.Equal(HttpStatusCode.OK, servida.StatusCode);
        Assert.Equal("image/jpeg", servida.Content.Headers.ContentType?.MediaType);
        var servido = await servida.Content.ReadAsByteArrayAsync();

        // Content-addressed: the id IS the SHA-256 of the bytes served back (strip before hash).
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(servido)), hash);
        Assert.Equal(tamanho, servido.Length);
        Assert.NotEqual(Convert.ToHexStringLower(SHA256.HashData(original)), hash);
        Assert.True(servido.Length < original.Length, "o blob sem EXIF deve ser menor que o arquivo enviado");

        // Byte scan of the served file: no EXIF, no GPS canary, no APP1 marker, no TIFF header.
        Assert.False(Contem(servido, Ascii(Canario)), "o servido não pode carregar o canário GPS");
        Assert.False(Contem(servido, Ascii("Exif\0\0")), "o servido não pode carregar Exif\\0\\0");
        Assert.False(Contem(servido, [0xFF, 0xE1]), "o servido não pode carregar o marcador APP1");
        Assert.False(Contem(servido, [0x49, 0x49, 0x2A, 0x00]), "o servido não pode carregar cabeçalho TIFF LE");
        Assert.False(Contem(servido, [0x4D, 0x4D, 0x00, 0x2A]), "o servido não pode carregar cabeçalho TIFF BE");

        // The whole volume, file by file: flat 64-hex names, and nothing carries the canary.
        foreach (var arquivo in Directory.EnumerateFiles(_host.BlobRoot, "*", SearchOption.AllDirectories))
        {
            Assert.Matches("^[0-9a-f]{64}$", Path.GetFileName(arquivo));
            Assert.False(
                Contem(await File.ReadAllBytesAsync(arquivo), Ascii(Canario)),
                $"o blob {arquivo} guarda metadados de localização");
        }

        _output.WriteLine(
            "AC-01 observacao: contentHash={0} sha256Original={1} sha256Servido={2} sizeOriginal={3} " +
            "sizeServido={4} width=12 height=6 blurHash={5} durationSeconds=null",
            hash,
            Convert.ToHexStringLower(SHA256.HashData(original)),
            Convert.ToHexStringLower(SHA256.HashData(servido)),
            original.Length, servido.Length,
            FeedHarness.Text(corpo, "blurHash"));
    }

    // ---------------------------------------------------------------- AC-02

    [Fact] // AC-02 — alt vazio 400, executável/mismatch 415 (§9.5), limite por tipo 413
    public async Task Ac02_Rejeicoes_AlternoVazio400_Executavel415_AcimaDoLimite413_PorTipo()
    {
        var dono = await RegistrarAsync("ac02");

        // alt obrigatório e não vazio para Image, Video e Gif (FR-022, §2.6).
        var recusas = new (byte[] Bytes, string Tipo, string Nome)[]
        {
            (JpegComExifSintetico(4, 4), "image/jpeg", "a.jpg"),
            (GifMinimo(), "image/gif", "a.gif"),
            (Mp4Esqueleto(600, 6000), "video/mp4", "a.mp4"),
        };
        var status400 = new List<int>();
        foreach (var (bytes, tipo, nome) in recusas)
        {
            var resposta = await EnviarAsync(dono, bytes, string.Empty, tipo, nome);
            status400.Add((int)resposta.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            var corpo = await FeedHarness.JsonOfAsync(resposta);
            Assert.Equal("validation_failed", corpo.GetProperty("error").GetProperty("code").GetString());
        }

        // §9.5: magic bytes decidem — executável disfarçado de imagem → 415.
        var elf = new byte[128];
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        Encoding.ASCII.GetBytes("QA0026 fake elf payload").CopyTo(elf, 32);
        var elfResposta = await EnviarAsync(dono, elf, "disfarçado de imagem", "image/jpeg", "foto.jpg");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, elfResposta.StatusCode);
        Assert.Equal("unsupported_media_type", (await FeedHarness.JsonOfAsync(elfResposta))
            .GetProperty("error").GetProperty("code").GetString());

        var mz = new byte[64];
        Encoding.ASCII.GetBytes("MZ\0\0This program cannot be run in DOS mode").CopyTo(mz, 0);
        var mzResposta = await EnviarAsync(dono, mz, "MZ disfarçado", "image/png", "foto.png");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, mzResposta.StatusCode);

        // Declared vs actual: real JPEG bytes claiming to be a PNG → 415.
        var mentira = await EnviarAsync(dono, JpegComExifSintetico(4, 4), "jpeg dentro de um png", "image/png", "x.png");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, mentira.StatusCode);

        // Above the per-type limit: 11 MiB of JPEG magic > 10 MiB (image) → 413.
        var grande = new byte[11 * 1024 * 1024];
        grande[0] = 0xFF; grande[1] = 0xD8; grande[2] = 0xFF; grande[3] = 0xE0;
        var acima = await EnviarAsync(dono, grande, "grande demais", "image/jpeg", "grande.jpg");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, acima.StatusCode);
        Assert.Equal("payload_too_large", (await FeedHarness.JsonOfAsync(acima))
            .GetProperty("error").GetProperty("code").GetString());

        // The limit is PER TYPE: 12 MiB of video is above the image limit but under the video
        // limit (50 MiB), so it must be accepted — a global cap would answer 413 here.
        var videoGrande = Mp4Esqueleto(600, 6000, 12 * 1024 * 1024);
        var videoResposta = await EnviarAsync(dono, videoGrande, "vídeo de 12 MiB", "video/mp4", "v.mp4");
        Assert.Equal(HttpStatusCode.OK, videoResposta.StatusCode);
        var videoCorpo = await FeedHarness.JsonOfAsync(videoResposta);
        Assert.Equal(10, videoCorpo.GetProperty("durationSeconds").GetInt32());

        _output.WriteLine(
            "AC-02 observacao: altVazio image/gif/video=[{0}] elf415={1} mz415={2} mismatch415={3} " +
            "413imagem11MiB={4} video12MiB={5} (limite por tipo: imagem 10 MiB, vídeo 50 MiB) duracaoVideo=10",
            string.Join(",", status400),
            (int)elfResposta.StatusCode, (int)mzResposta.StatusCode, (int)mentira.StatusCode,
            (int)acima.StatusCode, (int)videoResposta.StatusCode);
    }

    // ---------------------------------------------------------------- AC-03

    [Fact] // AC-03 — anexos no post: kind do mime, alt herdado, 400 inexistente e > 10
    public async Task Ac03_AnexosNoPost_KindDoMimeAltHerdado_DesconhecidoEOnzeSao400()
    {
        var dono = await RegistrarAsync("ac03");
        var slug = await NovoContainerAsync(dono);

        var jpegBytes = JpegComExifSintetico(14, 7);
        var hashJpeg = await SubirAsync(dono, jpegBytes, "descrição do jpeg");
        var hashGif = await SubirAsync(dono, GifMinimo(), "descrição do gif");
        var hashVideo = await SubirAsync(dono, Mp4Esqueleto(600, 6000), "descrição do vídeo");

        // Re-upload of the SAME bytes with another alt: the content-addressed record keeps the
        // FIRST one (documented shortcut) — the attachment must inherit the first alt.
        var deNovo = await EnviarAsync(dono, jpegBytes, "segunda descrição");
        Assert.Equal(HttpStatusCode.OK, deNovo.StatusCode);
        Assert.Equal(hashJpeg, FeedHarness.Text(await FeedHarness.JsonOfAsync(deNovo), "contentHash"));

        var publicado = await PostRoutes.SendAsync(
            dono, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "três anexos", contentHashes = new[] { hashJpeg, hashGif, hashVideo } }, dono.Csrf);
        Assert.Equal(HttpStatusCode.Created, publicado.StatusCode);

        var view = await FeedHarness.JsonOfAsync(publicado);
        var anexos = view.GetProperty("attachments");
        Assert.Equal(3, anexos.GetArrayLength());

        Assert.Equal("image", anexos[0].GetProperty("kind").GetString());
        Assert.Equal("image/jpeg", anexos[0].GetProperty("mimeType").GetString());
        Assert.Equal("descrição do jpeg", anexos[0].GetProperty("alt").GetString());
        Assert.False(string.IsNullOrEmpty(anexos[0].GetProperty("blurHash").GetString()));
        Assert.EndsWith($"/api/media/{hashJpeg}", anexos[0].GetProperty("url").GetString());

        Assert.Equal("gif", anexos[1].GetProperty("kind").GetString());
        Assert.Equal("descrição do gif", anexos[1].GetProperty("alt").GetString());
        Assert.False(string.IsNullOrEmpty(anexos[1].GetProperty("blurHash").GetString()));

        Assert.Equal("video", anexos[2].GetProperty("kind").GetString());
        Assert.Equal("descrição do vídeo", anexos[2].GetProperty("alt").GetString());
        Assert.Equal(JsonValueKind.Null, anexos[2].GetProperty("blurHash").ValueKind);
        Assert.Equal(10, anexos[2].GetProperty("durationSeconds").GetInt32());

        // Round trip: the stored post carries the same three attachments (§2.6).
        var relido = await PostRoutes.SendAsync(
            dono, HttpMethod.Get, $"/api/posts/{Uri.EscapeDataString(view.GetProperty("id").GetString()!)}");
        Assert.Equal(HttpStatusCode.OK, relido.StatusCode);
        var deNovoCorpo = await FeedHarness.JsonOfAsync(relido);
        var guardados = deNovoCorpo.GetProperty("attachments");
        Assert.Equal(3, guardados.GetArrayLength());
        Assert.Equal(hashJpeg, guardados[0].GetProperty("contentHash").GetString());
        Assert.Equal("descrição do jpeg", guardados[0].GetProperty("alt").GetString());

        // Unknown hash → 400, nothing published.
        var inexistente = await PostRoutes.SendAsync(
            dono, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "hash que não existe", contentHashes = new[] { new string('0', 64) } }, dono.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.Equal("validation_failed", (await FeedHarness.JsonOfAsync(inexistente))
            .GetProperty("error").GetProperty("code").GetString());

        // 11 attachments → 400; exactly 10 → still publishes (the cap counts LIST entries).
        var onze = await PostRoutes.SendAsync(
            dono, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "onze anexos", contentHashes = Enumerable.Repeat(hashJpeg, 11).ToArray() }, dono.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, onze.StatusCode);

        var dezHashes = new string[10];
        for (var indice = 0; indice < dezHashes.Length; indice++)
        {
            dezHashes[indice] = await SubirAsync(dono, JpegComExifSintetico(20 + indice, 5), $"anexo {indice}");
        }

        var dez = await PostRoutes.SendAsync(
            dono, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "dez anexos", contentHashes = dezHashes }, dono.Csrf);
        Assert.Equal(HttpStatusCode.Created, dez.StatusCode);
        Assert.Equal(10, (await FeedHarness.JsonOfAsync(dez)).GetProperty("attachments").GetArrayLength());

        _output.WriteLine(
            "AC-03 observacao: kinds=image/gif/video alts herdados=[descrição do jpeg,descrição do gif,descrição do vídeo] " +
            "duracaoVideo=10 desconhecido=400 onze=400 dez={0} reuploadMesmosBytes=200 altPrimeiroRegistro=descrição do jpeg",
            (int)dez.StatusCode);
    }

    // ---------------------------------------------------------------- AC-04

    [Fact] // AC-04 — GAP-25: sessão, uploader, leitor do post, 404 byte-idêntico (SC-005)
    public async Task Ac04_GetMedia_SessaoUploaderLeitor_E404IdenticoForaDeVista()
    {
        var anonimo = _host.NewClient();
        var semSessaoAusente = await anonimo.SendAsync(HttpMethod.Get, $"/api/media/{new string('a', 64)}");
        Assert.Equal(HttpStatusCode.Unauthorized, semSessaoAusente.StatusCode);
        Assert.Equal("not_authenticated", (await FeedHarness.JsonOfAsync(semSessaoAusente))
            .GetProperty("error").GetProperty("code").GetString());

        var dono = await RegistrarAsync("ac04d");
        var leitor = await RegistrarAsync("ac04l");

        var hash = await SubirAsync(dono, JpegComExifSintetico(9, 5), "mídia da AC-04");

        // Session comes first: an anonymous request never learns whether the hash exists.
        var semSessaoExiste = await anonimo.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        Assert.Equal(HttpStatusCode.Unauthorized, semSessaoExiste.StatusCode);

        // Logged in, but neither the uploader nor a reader of a referencing post → 404.
        var foraDeVista = await leitor.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        Assert.Equal(HttpStatusCode.NotFound, foraDeVista.StatusCode);

        // The uploader always reads their own upload — and gets exactly the stored bytes.
        var meu = await dono.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        Assert.Equal(HttpStatusCode.OK, meu.StatusCode);
        var bytesDono = await meu.Content.ReadAsByteArrayAsync();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytesDono)), hash);

        // A post referencing the hash opens the blob to anyone who may see that post.
        var slug = await NovoContainerAsync(dono); // born Public
        var publicado = await PostRoutes.SendAsync(
            dono, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "post com a mídia", contentHashes = new[] { hash } }, dono.Csrf);
        Assert.Equal(HttpStatusCode.Created, publicado.StatusCode);

        var veuPost = await leitor.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        Assert.Equal(HttpStatusCode.OK, veuPost.StatusCode);
        Assert.Equal(bytesDono, await veuPost.Content.ReadAsByteArrayAsync());

        // Current visibility: the container goes private and the reader is out — the 404 of an
        // out-of-sight hash must be byte-identical to the 404 of a hash that never existed.
        await StreamHarness.SetVisibilityAsync(dono, slug, "private");
        var oculto = await leitor.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        Assert.Equal(HttpStatusCode.NotFound, oculto.StatusCode);
        var ausente = await leitor.SendAsync(HttpMethod.Get, $"/api/media/{new string('0', 64)}");
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);

        var bytesOculto = await oculto.Content.ReadAsByteArrayAsync();
        var bytesAusente = await ausente.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytesAusente, bytesOculto);

        foreach (var corpo in new[] { bytesOculto, bytesAusente })
        {
            using var documento = JsonDocument.Parse(corpo);
            Assert.Equal("not_found", documento.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.False(
                documento.RootElement.GetProperty("error").TryGetProperty("traceId", out _),
                "o 404 da mídia não carrega traceId (SC-005)");
        }

        // …and the private container changes nothing for the uploader.
        Assert.Equal(HttpStatusCode.OK, (await dono.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);

        _output.WriteLine(
            "AC-04 observacao: anonimoAusente={0} anonimoExiste={1} leitorSemPost={2} uploader={3} " +
            "leitorComPost={4} leitorPrivado={5}404IdenticoAoAusente={6} traceIdPresente={7}",
            (int)semSessaoAusente.StatusCode, (int)semSessaoExiste.StatusCode,
            (int)foraDeVista.StatusCode, (int)meu.StatusCode, (int)veuPost.StatusCode,
            (int)oculto.StatusCode, bytesAusente.SequenceEqual(bytesOculto), false);
    }

    // ---------------------------------------------------------------- AC-05

    [Fact] // AC-05 — ordem §2.6: EXIF removido antes do blurHash; Image/Gif sempre com blurHash
    public async Task Ac05_Ordem_ExifRemovidoAntesDoBlurHash_BlurHashDoServidoConfere()
    {
        var dono = await RegistrarAsync("ac05");
        var original = JpegComExifSintetico(16, 9);

        var resposta = await EnviarAsync(dono, original, "ordem EXIF antes de blurHash");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await FeedHarness.JsonOfAsync(resposta);
        var blurHashResposta = FeedHarness.Text(corpo, "blurHash")!;
        Assert.False(string.IsNullOrEmpty(blurHashResposta), "Image sempre carrega blurHash");

        var hash = FeedHarness.Text(corpo, "contentHash")!;
        var servida = await dono.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        Assert.Equal(HttpStatusCode.OK, servida.StatusCode);
        var servido = await servida.Content.ReadAsByteArrayAsync();

        // The AC-05 equality, derived from the artifact: an INDEPENDENT encoder (this file's own
        // implementation of the reference algorithm) over the pixels of the SERVED bytes must
        // reproduce exactly what the upload answered — i.e. the value was computed over the
        // blob without metadata, not over the file that still carried EXIF/GPS.
        using (var pixelsServido = Decodificar(servido))
        {
            var meuEncoder = QaBlurHash.Encode(pixelsServido);
            Assert.Equal(blurHashResposta, meuEncoder);

            // The served bytes are the metadata-free blob itself (the scan AC-01 also runs).
            Assert.False(Contem(servido, Ascii(Canario)), "o servido não pode carregar o canário GPS");
            Assert.False(Contem(servido, Ascii("Exif\0\0")), "o servido não pode carregar Exif\\0\\0");

            // Observation (not an assertion): what the SAME encoder computes over the ORIGINAL
            // bytes — whether EXIF changes the decode is what tells the order apart at runtime.
            using var pixelsOriginal = Decodificar(original);
            var meuEncoderOriginal = QaBlurHash.Encode(pixelsOriginal);
            _output.WriteLine(
                "AC-05 observacao: blurHashResposta={0} meuEncoderServido={1} igualServido={2} " +
                "meuEncoderOriginal={3} igualOriginal={4} decodeOriginal={5}x{6} decodeServido={7}x{8}",
                blurHashResposta, meuEncoder, blurHashResposta == meuEncoder,
                meuEncoderOriginal, blurHashResposta == meuEncoderOriginal,
                pixelsOriginal.Width, pixelsOriginal.Height, pixelsServido.Width, pixelsServido.Height);
        }

        // Gif always carries a blurHash too (§2.6: obrigatório para Image/Gif).
        var gifResposta = await EnviarAsync(dono, GifMinimo(), "gif da AC-05", "image/gif", "q.gif");
        Assert.Equal(HttpStatusCode.OK, gifResposta.StatusCode);
        var gifCorpo = await FeedHarness.JsonOfAsync(gifResposta);
        Assert.False(string.IsNullOrEmpty(FeedHarness.Text(gifCorpo, "blurHash")), "Gif sempre carrega blurHash");
        Assert.Equal(JsonValueKind.Null, gifCorpo.GetProperty("durationSeconds").ValueKind);
    }

    // ---------------------------------------------------------------- harness

    /// <summary>Registers a fresh account on this host; the cookie jar is this test's own.</summary>
    private async Task<AuthClient> RegistrarAsync(string prefixo)
    {
        var client = _host.NewClient();
        var registro = await client.RegisterAsync(PostRoutes.Handle(prefixo), PostRoutes.Password);
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> EnviarAsync(
        AuthClient client, byte[] bytes, string alt, string? tipoDeclarado = null, string nome = "arquivo.bin")
    {
        var arquivo = new ByteArrayContent(bytes);
        if (tipoDeclarado is not null)
        {
            arquivo.Headers.ContentType = MediaTypeHeaderValue.Parse(tipoDeclarado);
        }

        var multipart = new MultipartFormDataContent();
        multipart.Add(arquivo, "file", nome);
        multipart.Add(new StringContent(alt), "alt");

        return client.SendAsync(HttpMethod.Post, "/api/media", null, request =>
        {
            request.Content = multipart;
            if (client.Csrf is { Length: > 0 } csrf)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }
        });
    }

    private async Task<string> SubirAsync(AuthClient client, byte[] bytes, string alt, string? tipo = null, string nome = "arquivo.bin")
    {
        var resposta = await EnviarAsync(client, bytes, alt, tipo, nome);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return FeedHarness.Text(await FeedHarness.JsonOfAsync(resposta), "contentHash")!;
    }

    private static async Task<string> NovoContainerAsync(AuthClient dono)
    {
        var slug = PostRoutes.Slug("qa26");
        var criado = await PostRoutes.SendAsync(
            dono, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = slug, slug }, dono.Csrf);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        return slug;
    }

    // ---------------------------------------------------------------- fixtures (built here)

    /// <summary>A real JPEG (Skia-encoded by this test) with a synthetic, structurally valid EXIF
    /// APP1 spliced right after SOI: IFD0 with Make (the canary), Orientation and the GPSInfo
    /// pointer, the GPS IFD itself with latitude refs and rationals, canary again after both.</summary>
    private static byte[] JpegComExifSintetico(int largura, int altura)
    {
        using var bitmap = new SKBitmap(largura, altura);
        for (var y = 0; y < altura; y++)
        {
            for (var x = 0; x < largura; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(
                    (byte)((x * 29) + 11), (byte)((y * 43) + 19), (byte)((x * 7) + (y * 17) + 60)));
            }
        }

        using var imagem = SKImage.FromBitmap(bitmap);
        using var codificada = imagem.Encode(SKEncodedImageFormat.Jpeg, 92);
        var jpeg = codificada.ToArray();

        var app1 = App1ExifSintetico();
        var montado = new byte[2 + app1.Length + jpeg.Length - 2];
        jpeg.AsSpan(0, 2).CopyTo(montado); // SOI
        app1.CopyTo(montado, 2);
        jpeg.AsSpan(2).CopyTo(montado.AsSpan(2 + app1.Length));
        return montado;
    }

    /// <summary>The APP1 <c>Exif\0\0</c> segment with the TIFF structure this test spells out.</summary>
    private static byte[] App1ExifSintetico()
    {
        using var tiff = new MemoryStream();
        tiff.Write(Ascii("II")); // little-endian TIFF
        tiff.WriteByte(0x2A);
        tiff.WriteByte(0x00);
        EscreverU32Le(tiff, 8); // IFD0 starts right after the TIFF header

        var makeOffset = 8 + 2 + (3 * 12) + 4;              // IFD0: 3 entries, then the data area
        var gpsOffset = makeOffset + Canario.Length + 1;    // the GPS IFD after the Make string
        var racionaisOffset = gpsOffset + 2 + (3 * 12) + 4; // rationals after the GPS IFD

        tiff.Write(new byte[] { 0x03, 0x00 }); // IFD0: 3 entries (ascending tag order)

        // Make (0x010F, ASCII): value = offset of the canary string.
        tiff.Write(U16Le(0x010F)); tiff.Write(U16Le(2)); tiff.Write(U32Le(Canario.Length + 1)); tiff.Write(U32Le(makeOffset));

        // Orientation (0x0112, SHORT): 6 = rotate 90°, so a decoder that honours EXIF would show it.
        tiff.Write(U16Le(0x0112)); tiff.Write(U16Le(3)); tiff.Write(U32Le(1)); tiff.Write(U32Le(6));

        // GPSInfo IFD pointer (0x8825, LONG).
        tiff.Write(U16Le(0x8825)); tiff.Write(U16Le(4)); tiff.Write(U32Le(1)); tiff.Write(U32Le(gpsOffset));
        EscreverU32Le(tiff, 0); // no next IFD

        tiff.Write(Ascii(Canario));
        tiff.WriteByte(0); // Make string (NUL-terminated) at makeOffset

        tiff.Write(new byte[] { 0x03, 0x00 }); // GPS IFD: 3 entries
        tiff.Write(U16Le(0x0001)); tiff.Write(U16Le(2)); tiff.Write(U32Le(2)); // GPSLatitudeRef, ASCII 2
        tiff.Write(Ascii("S")); tiff.WriteByte(0); tiff.WriteByte(0); tiff.WriteByte(0);
        tiff.Write(U16Le(0x0003)); tiff.Write(U16Le(2)); tiff.Write(U32Le(2)); // GPSLongitudeRef, ASCII 2
        tiff.Write(Ascii("W")); tiff.WriteByte(0); tiff.WriteByte(0); tiff.WriteByte(0);
        tiff.Write(U16Le(0x0002)); tiff.Write(U16Le(5)); tiff.Write(U32Le(3)); // GPSLatitude, RATIONAL 3
        tiff.Write(U32Le(racionaisOffset));
        EscreverU32Le(tiff, 0); // no next IFD

        foreach (var (numerator, denominator) in new[] { (10, 1), (30, 1), (50_000, 1_000) })
        {
            EscreverU32Le(tiff, numerator);
            EscreverU32Le(tiff, denominator);
        }

        tiff.Write(Ascii(Canario)); // marker after both IFDs

        var payload = Ascii("Exif\0\0").Concat(tiff.ToArray()).ToArray();
        var segmento = new byte[4 + payload.Length];
        segmento[0] = 0xFF;
        segmento[1] = 0xE1;
        segmento[2] = (byte)((payload.Length + 2) >> 8);
        segmento[3] = (byte)(payload.Length + 2);
        payload.CopyTo(segmento, 4);
        return segmento;
    }

    /// <summary>Walks the EXIF this test built — proof the payload is structurally real EXIF/GPS,
    /// not bytes that merely contain the words the scan looks for.</summary>
    private static void ValidarExifReal(byte[] jpeg)
    {
        Assert.True(jpeg.Length > 8 && jpeg[0] == 0xFF && jpeg[1] == 0xD8, "o fixture começa por SOI");
        Assert.True(jpeg[2] == 0xFF && jpeg[3] == 0xE1, "o APP1 vem logo após o SOI");
        var comprimento = (jpeg[4] << 8) | jpeg[5];
        Assert.True(comprimento > 8 && 6 + comprimento - 2 <= jpeg.Length, "APP1 com tamanho coerente");
        var payload = jpeg.AsSpan(6, comprimento - 2).ToArray();
        Assert.Equal("Exif\0\0", Encoding.ASCII.GetString(payload, 0, 6));
        var tiff = payload.AsSpan(6).ToArray(); // the TIFF structure after the Exif\0\0 magic

        Assert.Equal(new byte[] { 0x49, 0x49, 0x2A, 0x00 }, tiff.AsSpan(0, 4).ToArray());
        Assert.Equal(8L, LerU32Le(tiff, 4));

        const int ifd0 = 8;
        var entradas = LerU16Le(tiff, ifd0);
        Assert.Equal(3, entradas);

        var achouGps = false;
        var achouMake = false;
        var achouOrient = false;
        long gpsIfd = 0;
        for (var indice = 0; indice < entradas; indice++)
        {
            var entrada = ifd0 + 2 + (indice * 12);
            var tag = LerU16Le(tiff, entrada);
            var tipo = LerU16Le(tiff, entrada + 2);
            var contagem = LerU32Le(tiff, entrada + 4);
            var valor = LerU32Le(tiff, entrada + 8);

            if (tag == 0x8825)
            {
                achouGps = true;
                Assert.Equal(4, tipo);
                Assert.Equal(1L, contagem);
                gpsIfd = valor;
            }

            if (tag == 0x010F)
            {
                achouMake = true;
                Assert.Equal(2, tipo);
                Assert.Equal((long)(Canario.Length + 1), contagem);
                Assert.Equal(Canario, Encoding.ASCII.GetString(tiff, (int)valor, Canario.Length));
            }

            if (tag == 0x0112)
            {
                achouOrient = true;
                Assert.Equal(6L, valor);
            }
        }

        Assert.True(achouGps && achouMake && achouOrient, "IFD0 carrega Make, Orientation e ponteiro GPS");

        var entradasGps = LerU16Le(tiff, (int)gpsIfd);
        Assert.Equal(3, entradasGps);
        var tagsGps = new List<int>();
        for (var indice = 0; indice < entradasGps; indice++)
        {
            tagsGps.Add(LerU16Le(tiff, (int)gpsIfd + 2 + (indice * 12)));
        }

        Assert.Contains(0x0001, tagsGps); // GPSLatitudeRef
        Assert.Contains(0x0003, tagsGps); // GPSLongitudeRef
        Assert.Contains(0x0002, tagsGps); // GPSLatitude (RATIONAL)
    }

    /// <summary>A valid 1×1 GIF89a assembled byte by byte: header, palette, GCE, image descriptor,
    /// LZW block and trailer — the Gif kind without borrowing anyone's fixture.</summary>
    private static byte[] GifMinimo() =>
    [
        (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
        0x01, 0x00, 0x01, 0x00,       // 1×1
        0x80, 0x00, 0x00,             // global palette, 2 colors
        0x00, 0x00, 0x00,             // color 0
        0xFF, 0xFF, 0xFF,             // color 1
        0x21, 0xF9, 0x04,             // graphic control extension
        0x01,                         //   packed: transparent index valid
        0x00, 0x00,                   //   delay 0
        0x00,                         //   transparent index 0
        0x00,                         //   block terminator
        0x2C,                         // image descriptor
        0x00, 0x00, 0x00, 0x00,       // left, top
        0x01, 0x00, 0x01, 0x00,       // 1×1
        0x00,                         // no local color table
        0x02,                         // LZW minimum code size
        0x02, 0x44, 0x01,             // one data sub-block
        0x00,                         // block terminator
        0x3B,                         // trailer
    ];

    /// <summary>An MP4 skeleton — ftyp + moov{ mvhd (version 0) } — whose mvhd says exactly
    /// <paramref name="duracao"/> seconds; optionally padded with zero bytes to a minimum size.</summary>
    private static byte[] Mp4Esqueleto(int timescale, long duracao, int tamanhoMinimo = 0)
    {
        using var mvhd = new MemoryStream();
        mvhd.WriteByte(0);                 // version 0
        mvhd.Write(new byte[3]);           // flags
        EscreverU32Be(mvhd, 0);            // creation
        EscreverU32Be(mvhd, 0);            // modification
        EscreverU32Be(mvhd, (uint)timescale);
        EscreverU32Be(mvhd, (uint)duracao);
        var payload = mvhd.ToArray();      // 20 bytes: the parser reads [12..20)

        using var arquivo = new MemoryStream();
        EscreverU32Be(arquivo, 16);        // ftyp box: header + brand + minor
        arquivo.Write("ftyp"u8);
        arquivo.Write("isom"u8);
        EscreverU32Be(arquivo, 0);

        var mvhdBox = 8 + payload.Length;
        EscreverU32Be(arquivo, (uint)(8 + mvhdBox)); // moov holds exactly one mvhd box
        arquivo.Write("moov"u8);
        EscreverU32Be(arquivo, (uint)mvhdBox);
        arquivo.Write("mvhd"u8);
        arquivo.Write(payload);

        var bytes = arquivo.ToArray();
        return tamanhoMinimo > bytes.Length ? [.. bytes, .. new byte[tamanhoMinimo - bytes.Length]] : bytes;
    }

    /// <summary>Decodes bytes to pixels with the same Skia entry point the product uses.</summary>
    private static SKBitmap Decodificar(byte[] bytes)
    {
        var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        return bitmap!;
    }

    // ---------------------------------------------------------------- byte helpers

    private static byte[] Ascii(string texto) => Encoding.ASCII.GetBytes(texto);

    private static bool Contem(byte[] aceitar, byte[] procurar)
    {
        for (var indice = 0; indice + procurar.Length <= aceitar.Length; indice++)
        {
            if (aceitar.AsSpan(indice, procurar.Length).SequenceEqual(procurar)) return true;
        }

        return false;
    }

    private static byte[] U16Le(int valor) => [(byte)valor, (byte)(valor >> 8)];

    private static byte[] U32Le(long valor) =>
        [(byte)valor, (byte)(valor >> 8), (byte)(valor >> 16), (byte)(valor >> 24)];

    private static void EscreverU32Le(System.IO.Stream stream, long valor) => stream.Write(U32Le(valor));

    private static void EscreverU32Be(System.IO.Stream stream, long valor)
    {
        stream.WriteByte((byte)(valor >> 24));
        stream.WriteByte((byte)(valor >> 16));
        stream.WriteByte((byte)(valor >> 8));
        stream.WriteByte((byte)valor);
    }

    private static int LerU16Le(byte[] dados, int offset) => dados[offset] | (dados[offset + 1] << 8);

    private static long LerU32Le(byte[] dados, int offset) =>
        dados[offset]
        | (dados[offset + 1] << 8)
        | (dados[offset + 2] << 16)
        | ((long)dados[offset + 3] << 24);

    // ---------------------------------------------------------------- independent encoder

    /// <summary>
    /// The blurHash encoder of QA-0026, written here from the reference algorithm (4×3 DCT,
    /// base83 over the official alphabet) — it never calls the product's <c>BlurHashEncoder</c>.
    /// AC-05 compares the server's answer against what THIS code computes over the pixels of the
    /// served bytes, so a wrong value on either side breaks the assertion. Images in this suite
    /// fit 32×32, so no resize (and no canvas filter) stands between pixels and the transform.
    /// </summary>
    private static class QaBlurHash
    {
        private const string Alfabeto = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

        public static string Encode(SKBitmap fonte)
        {
            Assert.True(Math.Max(fonte.Width, fonte.Height) <= 32,
                "as imagens desta QA cabem em 32×32: sem redimensionamento");

            var largura = fonte.Width;
            var altura = fonte.Height;
            var fatores = new float[12, 3];
            for (var y = 0; y < 3; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    var normalizacao = (x == 0 && y == 0) ? 1f : 2f;
                    float r = 0, g = 0, b = 0;
                    for (var py = 0; py < altura; py++)
                    {
                        var linha = MathF.Cos(MathF.PI * y * py / altura);
                        for (var px = 0; px < largura; px++)
                        {
                            var baseCosseno = linha * MathF.Cos(MathF.PI * x * px / largura);
                            var pixel = fonte.GetPixel(px, py);
                            r += baseCosseno * Linear(pixel.Red);
                            g += baseCosseno * Linear(pixel.Green);
                            b += baseCosseno * Linear(pixel.Blue);
                        }
                    }

                    var escala = normalizacao / (largura * altura);
                    fatores[(y * 4) + x, 0] = r * escala;
                    fatores[(y * 4) + x, 1] = g * escala;
                    fatores[(y * 4) + x, 2] = b * escala;
                }
            }

            var maximoReal = 0f;
            for (var indice = 1; indice < 12; indice++)
            {
                for (var canal = 0; canal < 3; canal++)
                {
                    maximoReal = Math.Max(maximoReal, MathF.Abs(fatores[indice, canal]));
                }
            }

            var quantizado = Math.Clamp((long)Math.Floor((maximoReal * 166f) - 0.5f), 0, 82);
            var maximo = (quantizado + 1) / 166f;

            var texto = new StringBuilder(27);
            texto.Append(B83(3 + (2 * 9), 1)); // (4-1) + (3-1)*9 component counts
            texto.Append(B83(quantizado, 1));
            texto.Append(B83(
                (LinearSrgb(fatores[0, 0]) << 16) + (LinearSrgb(fatores[0, 1]) << 8) + LinearSrgb(fatores[0, 2]),
                4));
            for (var indice = 1; indice < 12; indice++)
            {
                var quantR = QuantizarAc(fatores[indice, 0], maximo);
                var quantG = QuantizarAc(fatores[indice, 1], maximo);
                var quantB = QuantizarAc(fatores[indice, 2], maximo);
                texto.Append(B83((quantR * 19 * 19) + (quantG * 19) + quantB, 2));
            }

            return texto.ToString();
        }

        private static long QuantizarAc(float valor, float maximo) =>
            Math.Clamp((long)MathF.Floor(SignPow(valor / maximo, 0.5f) * 9 + 9.5f), 0, 18);

        private static float SignPow(float valor, float expoente) =>
            MathF.Sign(valor) * MathF.Pow(MathF.Abs(valor), expoente);

        private static float Linear(byte valor)
        {
            var v = valor / 255f;
            return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        private static int LinearSrgb(float valor)
        {
            var v = Math.Clamp(valor, 0f, 1f);
            return v <= 0.0031308f
                ? (int)((v * 12.92f * 255f) + 0.5f)
                : (int)((((1.055f * MathF.Pow(v, 1f / 2.4f)) - 0.055f) * 255f) + 0.5f);
        }

        private static string B83(long valor, int casas)
        {
            var digitos = new char[casas];
            for (var indice = 0; indice < casas; indice++)
            {
                var divisor = (long)Math.Pow(83, casas - 1 - indice);
                digitos[indice] = Alfabeto[(int)((valor / divisor) % 83)];
            }

            return new string(digitos);
        }
    }
}
