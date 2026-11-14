using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Media;
using Squid.Tests.Feed;

namespace Squid.Tests.Media;

/// <summary>
/// AC-02/AC-03 of TASK-0053 — the published blob route of contract §1 (SPEC-0011 section D,
/// GAP-51): <c>GET /media/{hash}</c> serves the content-addressed blob WITHOUT a session with a
/// long <c>Cache-Control</c> and the hash as <c>ETag</c>, refuses a hash whose bytes do not
/// answer it (the SHA-256 check) and exposes no metadata — while <c>GET /api/media/{contentHash}</c>
/// keeps the session + GAP-25 rule byte-identical (AC-03, whose full ladder the TASK-0027 suites
/// already prove; this file pins the pair for the same hash).
/// </summary>
[Collection("ServiceHost")]
public sealed class MediaBlobRouteTests : IClassFixture<MediaHostFixture>
{
    private readonly MediaHostFixture _host;

    public MediaBlobRouteTests(MediaHostFixture host) => _host = host;

    [Fact] // AC-02 — anônimo, cache longo, ETag do hash, corpo = o blob puro
    public async Task Ac02_the_contract_blob_route_serves_the_blob_anonymously_with_a_long_cache_and_the_hash_as_etag()
    {
        var hash = await UploadAsync("blob");

        // O jar base do host nunca fez login: a rota do contrato não pede sessão (contrato §1).
        var response = await _host.Http.GetAsync($"/media/{hash}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(body))); // serve exatamente o blob do hash
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cache));
        Assert.Equal("public, max-age=31536000, immutable", Assert.Single(cache));
        Assert.Equal($"\"{hash}\"", Assert.Single(response.Headers.GetValues("ETag")));
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);

        // Nenhum metadado: o corpo é o blob cru (JPEG SOI), nunca um registro JSON da mídia.
        Assert.Equal(0xFF, body[0]);
        Assert.Equal(0xD8, body[1]);
    }

    [Fact] // AC-02 — hash inexistente e bytes fora do hash: um404 só, sem traceId
    public async Task Ac02_an_absent_hash_and_bytes_that_do_not_answer_the_key_share_one_404()
    {
        var missing = await _host.Http.GetAsync($"/media/{new string('0', 64)}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var absentBody = await missing.Content.ReadAsByteArrayAsync();

        var error = await FeedHarness.JsonOfAsync(missing);
        Assert.Equal("not_found", error.GetProperty("error").GetProperty("code").GetString());
        Assert.False(error.GetProperty("error").TryGetProperty("traceId", out _), "o 404 da mídia não carrega traceId");

        // A prova da conferência do SHA-256 (AC-02): bytes que NÃO são o hash da rota, naquele caminho.
        var jpeg = MediaHarness.JpegWithExif();
        var hash = Convert.ToHexStringLower(SHA256.HashData(jpeg));
        var tampered = Encoding.UTF8.GetBytes("bytes que nao dao o hash da rota");
        var store = _host.App.Services.GetRequiredService<IMediaStore>();
        await store.SaveAsync(tampered, new MediaRecord(
            hash, "image/jpeg", "trocado no volume", null, null, null, null,
            tampered.LongLength, "alice", DateTimeOffset.UtcNow));

        var corrupt = await _host.Http.GetAsync($"/media/{hash}");
        Assert.Equal(HttpStatusCode.NotFound, corrupt.StatusCode);
        Assert.Equal(absentBody, await corrupt.Content.ReadAsByteArrayAsync());
    }

    [Fact] // AC-03 — para o MESMO hash: sessão intacta no /api/media, anônimo no /media
    public async Task Ac03_the_session_gated_route_still_demands_a_session_while_the_contract_route_does_not()
    {
        // O upload é do dono que logo abaixo lê de volta — GAP-25 serve quem tem sessão E é o dono.
        var (owner, _) = await MediaHarness.SignedInAsync(_host, "gateown");
        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(), "midia gate");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var document = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var hash = document.RootElement.GetProperty("contentHash").GetString()!;

        // GAP-25 intacto: a rota de sessão continua 401 sem sessão, mesmo para um hash que existe.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _host.Http.GetAsync($"/api/media/{hash}")).StatusCode);
        // E a rota do contrato serve o mesmo hash sem sessão nenhuma.
        Assert.Equal(HttpStatusCode.OK, (await _host.Http.GetAsync($"/media/{hash}")).StatusCode);

        // A UI local não mudou de rota: quem tem sessão continua lendo pelo caminho de antes.
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);
    }

    /// <summary>Uploads one JPEG and answers its content hash.</summary>
    private async Task<string> UploadAsync(string prefix)
    {
        var (owner, _) = await MediaHarness.SignedInAsync(_host, prefix);
        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(), $"midia {prefix}");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var document = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("contentHash").GetString()!;
    }
}
