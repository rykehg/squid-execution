using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Squid.Api.Media;
using Squid.Tests.Auth;
using Squid.Tests.Media;
using Squid.Worker.Thumbnail;
using Xunit.Abstractions;

namespace Squid.Tests.Load;

/// <summary>
/// AC-01 of TASK-0032 — SPEC-0009 section J: the Worker's thumbnail pass invoked DIRECTLY (same
/// invocable style as the reconcile service, no sleeping) over four synthetic uploads — a real
/// JPEG carrying EXIF, a GIF, an MP4 and a WAV — registers a SkiaSharp thumbnail for the images
/// and the GAP-21 placeholder for BOTH video and audio, and every one of them is servable through
/// <c>GET /api/media/{contentHash}/thumb</c> behind the very session + visibility ladder of
/// TASK-0027 (its AC-04 rule intact: 401 without a session, byte-identical 404 for a non-reader).
/// </summary>
[Collection("ServiceHost")]
public sealed class ThumbnailTests : IClassFixture<MediaHostFixture>
{
    private readonly MediaHostFixture _host;

    private readonly ITestOutputHelper _output;

    public ThumbnailTests(MediaHostFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    [Fact] // AC-01
    public async Task Ac01_the_worker_pass_registers_a_skia_thumbnail_for_images_and_the_placeholders_for_video_and_audio()
    {
        var (uploader, _) = await MediaHarness.SignedInAsync(_host, "thumbac01");
        var jpeg = await HashOfAsync(await MediaHarness.UploadAsync(
            uploader, MediaHarness.JpegWithExif(640, 480), "foto grande", "image/jpeg", "foto.jpg"));
        var gif = await HashOfAsync(await MediaHarness.UploadAsync(
            uploader, MediaHarness.MinimalGif(), "gif minimo", "image/gif", "gif.gif"));
        var video = await HashOfAsync(await MediaHarness.UploadAsync(
            uploader, MediaHarness.Mp4WithDuration(1000, 7000), "video de sete segundos", "video/mp4", "video.mp4"));
        var audio = await HashOfAsync(await MediaHarness.UploadAsync(
            uploader, MinimalWav(), "audio sintetico", "audio/wav", "audio.wav"));

        // Antes do passe: nada registrado ainda — a rota serve o placeholder da GAP-21
        // ("sem thumbnail, serve placeholder"), nunca os bytes originais.
        var pending = await ThumbAsync(uploader, jpeg);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.Equal(IMediaStore.ThumbnailMimeType, pending.Content.Headers.ContentType?.MediaType);
        Assert.True(Decodes(await pending.Content.ReadAsByteArrayAsync()), "the placeholder must be a decodable image");

        // O passe do Worker, invocado direto — o mesmo estilo do serviço de quota, sem dormir.
        var service = new ThumbnailService(
            _host.BlobRoot, new ThumbnailGenerator(), TimeSpan.FromHours(1),
            NullLogger<ThumbnailService>.Instance);
        var generated = await service.RunOnceAsync();
        Assert.Equal(4, generated); // jpeg + gif + video + audio: toda mídia registrada na primeira passada

        // Idempotente: a segunda passada encontra a fila vazia e não reprocessa nada.
        Assert.Equal(0, await service.RunOnceAsync());

        // Imagem: thumbnail REAL do SkiaSharp — decodifica, escala sem ampliar e responde PNG.
        var thumbnail = await File.ReadAllBytesAsync(ThumbPath(jpeg));
        using (var bitmap = SKBitmap.Decode(thumbnail))
        {
            Assert.NotNull(bitmap);
            Assert.Equal(ThumbnailGenerator.MaxDimension, Math.Max(bitmap!.Width, bitmap.Height));
        }

        // GIF: registrado também (GAP-21 só exclui vídeo/áudio) — o PNG que o generator codou,
        // decodável e nunca acima do MaxDimension, não só um arquivo qualquer no lugar certo.
        var gifThumb = await File.ReadAllBytesAsync(ThumbPath(gif));
        Assert.True(IsPng(gifThumb), "the GIF thumbnail must be the PNG the generator encoded");
        using (var gifBitmap = SKBitmap.Decode(gifThumb))
        {
            Assert.NotNull(gifBitmap);
            Assert.True(
                Math.Max(gifBitmap!.Width, gifBitmap.Height) <= ThumbnailGenerator.MaxDimension,
                $"the GIF thumbnail must not be upscaled: {gifBitmap.Width}x{gifBitmap.Height}");
        }

        // Vídeo: o placeholder REGISTRADO pelo passe — os mesmos bytes da constante, nunca o vídeo.
        Assert.Equal(MediaPlaceholders.Bytes, await File.ReadAllBytesAsync(ThumbPath(video)));

        // Áudio: o outro lado da GAP-21 — o passe registra o MESMO placeholder, nunca pixels.
        Assert.Equal(MediaPlaceholders.Bytes, await File.ReadAllBytesAsync(ThumbPath(audio)));

        // Servir: a rota devolve exatamente o que o passe registrou.
        var served = await ThumbAsync(uploader, jpeg);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(IMediaStore.ThumbnailMimeType, served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(thumbnail, await served.Content.ReadAsByteArrayAsync());
        var servedVideo = await ThumbAsync(uploader, video);
        Assert.Equal(HttpStatusCode.OK, servedVideo.StatusCode);
        Assert.Equal(MediaPlaceholders.Bytes, await servedVideo.Content.ReadAsByteArrayAsync());
        var servedAudio = await ThumbAsync(uploader, audio);
        Assert.Equal(HttpStatusCode.OK, servedAudio.StatusCode);
        Assert.Equal(IMediaStore.ThumbnailMimeType, servedAudio.Content.Headers.ContentType?.MediaType);
        Assert.Equal(MediaPlaceholders.Bytes, await servedAudio.Content.ReadAsByteArrayAsync());

        // A visibilidade da TASK-0027 (AC-04) intacta na rota nova: 401 sem sessão…
        var anonymous = _host.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ThumbAsync(anonymous, jpeg)).StatusCode);

        // …e 404 byte-idêntico entre hash ausente e hash fora da vista do leitor (SC-005).
        var (outsider, _) = await MediaHarness.SignedInAsync(_host, "thumbac01fora");
        var hidden = await ThumbAsync(outsider, jpeg);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var absent = await ThumbAsync(outsider, new string('a', 64));
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await hidden.Content.ReadAsByteArrayAsync(), await absent.Content.ReadAsByteArrayAsync());

        // Off the write path: a API host não registra o passe — ele pertence ao Squid.Worker.
        Assert.DoesNotContain(
            _host.App.Services.GetServices<IHostedService>(),
            hosted => hosted is ThumbnailService);

        _output.WriteLine(
            $"J thumbnail: passada=1 gerados=4 idempotente=0 thumb={thumbnail.Length} bytes "
            + $"max-side={ThumbnailGenerator.MaxDimension} placeholder={MediaPlaceholders.Bytes.Length} bytes "
            + "video=placeholder audio=placeholder");
    }

    /// <summary>GET /api/media/{contentHash}/thumb through the caller's cookie jar.</summary>
    private static Task<HttpResponseMessage> ThumbAsync(AuthClient client, string hash) =>
        client.SendAsync(HttpMethod.Get, $"/api/media/{hash}/thumb");

    /// <summary>The contentHash of an upload answer — the route's contract body.</summary>
    private static async Task<string> HashOfAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("contentHash").GetString()!;
    }

    private static bool Decodes(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        return bitmap is not null && bitmap.Width > 0 && bitmap.Height > 0;
    }

    /// <summary>Where the store reads the registered thumbnail of a hash (the volume convention).</summary>
    private string ThumbPath(string contentHash) =>
        Path.Combine(_host.BlobRoot, IMediaStore.ThumbnailFolder, contentHash);

    /// <summary>
    /// A synthetic 44-byte PCM WAV header: RIFF/WAVE at their offsets is all the magic-byte sniff
    /// and the thumbnail pass read of an audio blob (GAP-21 never looks at the samples).
    /// </summary>
    private static byte[] MinimalWav()
    {
        var bytes = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BitConverter.GetBytes(36u).CopyTo(bytes, 4); // bytes after this field (header only)
        Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(bytes, 12);
        BitConverter.GetBytes(16u).CopyTo(bytes, 16); // fmt chunk size
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, 20); // PCM
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, 22); // mono
        BitConverter.GetBytes(8000u).CopyTo(bytes, 24);
        BitConverter.GetBytes(8000u).CopyTo(bytes, 28);
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, 32); // block align
        BitConverter.GetBytes((ushort)16).CopyTo(bytes, 34); // bits per sample
        Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        // data size stays 0: the payload is the header, not samples.
        return bytes;
    }

    /// <summary>The 8-byte PNG signature — proof the bytes are the generator's PNG, not a rename.</summary>
    private static bool IsPng(byte[] bytes) =>
        bytes.Length > 8
        && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
        && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
}
