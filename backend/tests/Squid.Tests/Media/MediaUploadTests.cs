using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Media;
using Squid.Tests.Auth;
using Squid.Tests.Feed;

namespace Squid.Tests.Media;

/// <summary>
/// AC-01, AC-02 and AC-05 of TASK-0027: the <c>POST /api/media</c> contract itself — a real JPEG
/// carrying real EXIF answers the seven fields and serves a blob with no metadata left in it
/// (byte scan), the refusals answer 400/415/413 with the contract codes, and Image/Gif always
/// carry a blurHash computed after the strip.
/// </summary>
[Collection("ServiceHost")]
public sealed class MediaUploadTests : IClassFixture<MediaHostFixture>
{
    private readonly MediaHostFixture _host;

    public MediaUploadTests(MediaHostFixture host) => _host = host;

    [Fact] // AC-01 + AC-05
    public async Task Upload_JpegWithRealExif_AnswersTheContractAndServesABlobWithoutExif()
    {
        var (client, _) = await MediaHarness.SignedInAsync(_host, "up1");
        var original = MediaHarness.JpegWithExif(8, 4);
        Assert.True(MediaHarness.Contains(original, MediaHarness.Bytes("Exif\0\0")), "o jpeg de teste deve nascer com EXIF");
        Assert.True(MediaHarness.Contains(original, MediaHarness.Bytes(MediaHarness.ExifMarker)), "o jpeg de teste deve nascer com GPS");

        var response = await MediaHarness.UploadAsync(client, original, "imagem de teste com EXIF");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await FeedHarness.JsonOfAsync(response);
        var hash = FeedHarness.Text(body, "contentHash");
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal("image/jpeg", FeedHarness.Text(body, "mimeType"));
        Assert.Equal(8, body.GetProperty("width").GetInt32());
        Assert.Equal(4, body.GetProperty("height").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("durationSeconds").ValueKind);
        Assert.False(string.IsNullOrEmpty(FeedHarness.Text(body, "blurHash")));

        var sizeBytes = body.GetProperty("sizeBytes").GetInt64();
        Assert.True(sizeBytes > 0);
        Assert.True(sizeBytes < original.Length, "o blob sem EXIF deve ser menor que o arquivo enviado");

        // The served file: same length as the record says, no EXIF, no GPS, and it hashes to its own id.
        var servedResponse = await client.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        Assert.Equal(HttpStatusCode.OK, servedResponse.StatusCode);
        Assert.Equal("image/jpeg", servedResponse.Content.Headers.ContentType?.MediaType);
        var served = await servedResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(sizeBytes, served.Length);
        Assert.False(MediaHarness.Contains(served, MediaHarness.Bytes("Exif\0\0")), "o servido não pode carregar APP1 de EXIF");
        Assert.False(MediaHarness.Contains(served, MediaHarness.Bytes(MediaHarness.ExifMarker)), "o servido não pode carregar GPS");
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(served)), hash);

        // AC-05: blurHash over the served file == blurHash over the blob without metadata, and ==
        // what an upload of the ORIGINAL bytes computes — the strip happens before the encoder.
        var processor = _host.App.Services.GetRequiredService<IMediaProcessor>();
        var blurHash = FeedHarness.Text(body, "blurHash");
        Assert.Equal(blurHash, processor.Process(served, "image/jpeg")!.BlurHash);
        Assert.Equal(blurHash, processor.Process(original, "image/jpeg")!.BlurHash);

        // Content-addressed: identical bytes are the same hash.
        var again = await MediaHarness.UploadAsync(client, original, "imagem de teste com EXIF");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(hash, FeedHarness.Text(await FeedHarness.JsonOfAsync(again), "contentHash"));

        // The whole volume, scanned file by file: nothing stored ever carries the marker again.
        foreach (var file in Directory.EnumerateFiles(_host.BlobRoot, "*", SearchOption.AllDirectories))
        {
            Assert.False(
                MediaHarness.Contains(await File.ReadAllBytesAsync(file), MediaHarness.Bytes(MediaHarness.ExifMarker)),
                $"o blob {file} guarda metadados de localização");
        }
    }

    [Fact] // AC-02
    public async Task Upload_EmptyAlt_OnAnImage_Is400()
    {
        var (client, _) = await MediaHarness.SignedInAsync(_host, "up2");
        var response = await MediaHarness.UploadAsync(client, MediaHarness.JpegWithExif(), string.Empty);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await FeedHarness.JsonOfAsync(response);
        Assert.Equal("validation_failed", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact] // AC-02, contract §9.5
    public async Task Upload_ExecutableDisguisedAsAnImage_Is415()
    {
        var (client, _) = await MediaHarness.SignedInAsync(_host, "up3");
        var executable = new byte[512];
        Encoding.ASCII.GetBytes("MZ\0\0This program cannot be run in DOS mode").CopyTo(executable, 0);

        var response = await MediaHarness.UploadAsync(
            client, executable, "disfarçado de imagem", declaredType: "image/png", fileName: "foto.png");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var body = await FeedHarness.JsonOfAsync(response);
        Assert.Equal("unsupported_media_type", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact] // AC-02 — declared vs actual
    public async Task Upload_DeclaredPng_WithJpegBytes_Is415()
    {
        var (client, _) = await MediaHarness.SignedInAsync(_host, "up4");
        var response = await MediaHarness.UploadAsync(
            client, MediaHarness.JpegWithExif(), "jpeg dentro de um png", declaredType: "image/png");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact] // AC-02 — per-type limit
    public async Task Upload_AboveTheImageLimit_Is413()
    {
        var (client, _) = await MediaHarness.SignedInAsync(_host, "up5");
        var oversized = new byte[(10 * 1024 * 1024) + 1024 * 1024]; // 11 MiB > 10 MiB default
        oversized[0] = 0xFF;
        oversized[1] = 0xD8;
        oversized[2] = 0xFF;

        var response = await MediaHarness.UploadAsync(client, oversized, "grande demais");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var body = await FeedHarness.JsonOfAsync(response);
        Assert.Equal("payload_too_large", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact] // AC-05 — Gif carries blurHash, duration only exists for MP4/MOV (GAP-22)
    public async Task Upload_Gif_CarriesBlurHashAndNoDuration()
    {
        var (client, _) = await MediaHarness.SignedInAsync(_host, "up6");
        var response = await MediaHarness.UploadAsync(
            client, MediaHarness.MinimalGif(), "gif de um pixel", declaredType: "image/gif");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await FeedHarness.JsonOfAsync(response);
        Assert.Equal("image/gif", FeedHarness.Text(body, "mimeType"));
        Assert.False(string.IsNullOrEmpty(FeedHarness.Text(body, "blurHash")));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("durationSeconds").ValueKind);
        Assert.Equal(1, body.GetProperty("width").GetInt32());
        Assert.Equal(1, body.GetProperty("height").GetInt32());
    }

    [Fact] // GAP-22 — duration only for MP4/MOV; video never decodes here (GAP-21)
    public async Task Upload_MinimalMp4_AnswersItsDurationAndNoBlurHash()
    {
        var (client, _) = await MediaHarness.SignedInAsync(_host, "up7");
        var response = await MediaHarness.UploadAsync(
            client, MediaHarness.Mp4WithDuration(1000, 7000), "vídeo de 7 s", declaredType: "video/mp4");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await FeedHarness.JsonOfAsync(response);
        Assert.Equal("video/mp4", FeedHarness.Text(body, "mimeType"));
        Assert.Equal(7, body.GetProperty("durationSeconds").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("blurHash").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("width").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("height").ValueKind);
    }

    [Fact] // AC-04 — the session the GAP-25 route demands, on both media routes
    public async Task WithoutSession_BothMediaRoutes_Are401()
    {
        var anonymous = _host.NewClient();

        var upload = await MediaHarness.UploadAsync(anonymous, MediaHarness.JpegWithExif(), "sem sessão");
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);

        var read = await anonymous.SendAsync(HttpMethod.Get, $"/api/media/{new string('a', 64)}");
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
    }
}
