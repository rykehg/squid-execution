using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Squid.Api.Posts;
using Squid.Tests.Media;
using Squid.Tests.Unfurl;

namespace Squid.Tests.Federation;

/// <summary>
/// AC-04 of TASK-0053 — the download half of SPEC-0011 section D (GAP-44/GAP-49): the
/// PRODUCTION <see cref="RemoteMediaClient"/> fetches the contract route <c>GET /media/{hash}</c>
/// from a loopback peer NAMED in <c>Federation:TrustedPeerHosts</c> — the blob route of a real
/// host over a real socket — while the default empty allowlist still refuses that same target
/// before any socket, and a failure answers null: a download that fails is an absent attachment,
/// never a rejected post (the post-level half of GAP-44 stays with InboxReceiveTests' AC-07,
/// which runs the very same client seam through the inbox pipeline).
/// </summary>
[Collection("ServiceHost")]
public sealed class RemoteBlobFetchTests : IClassFixture<MediaHostFixture>
{
    private readonly MediaHostFixture _host;

    public RemoteBlobFetchTests(MediaHostFixture host) => _host = host;

    [Fact] // AC-04 — baixa real da rota do contrato do par em loopback nomeado + fail-open
    public async Task Ac04_the_media_client_downloads_the_contract_route_of_a_named_loopback_peer()
    {
        var hash = await UploadAsync("fetch");
        var url = $"{Origin}/media/{hash}";

        using var http = new HttpClient();
        var named = new RemoteMediaClient(
            http, new FakeUnfurlResolver(), new UnfurlOptions(2000, 64 * 1024),
            TrustedPeerHosts.From(Config("Federation:TrustedPeerHosts", "127.0.0.1")));

        // O par (este host, servindo a rota do contrato) é loopback E nomeado: a mídia baixa.
        var bytes = await named.FetchAsync(url, CancellationToken.None);
        Assert.NotNull(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes)));

        // Vazia por padrão (GAP-49), a MESMA allowlist continua recusando o mesmo alvo.
        var refused = new RemoteMediaClient(
            http, new FakeUnfurlResolver(), new UnfurlOptions(2000, 64 * 1024),
            TrustedPeerHosts.Empty);
        Assert.Null(await refused.FetchAsync(url, CancellationToken.None));

        // Fail-open (GAP-44): o hash sem blob responde 404 → null — nunca uma exceção que
        // pudesse derrubar a entrega; o post segue de pé (meio posto, InboxReceiveTests AC-07).
        Assert.Null(await named.FetchAsync($"{Origin}/media/{new string('0', 64)}", CancellationToken.None));
    }

    /// <summary>The origin this host answers on — the same rule the documents publish.</summary>
    private string Origin => $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}";

    private static IConfiguration Config(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

    /// <summary>Uploads one JPEG through the host and answers its content hash.</summary>
    private async Task<string> UploadAsync(string prefix)
    {
        var (owner, _) = await MediaHarness.SignedInAsync(_host, prefix);
        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(), $"midia {prefix}");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var document = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("contentHash").GetString()!;
    }
}
