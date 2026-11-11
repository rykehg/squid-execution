using System.Net;
using Squid.Tests.Auth;
using Squid.Tests.Feed;
using Squid.Tests.Posts;
using Squid.Tests.Stream;

namespace Squid.Tests.Media;

/// <summary>
/// AC-04 of TASK-0027: the GAP-25 rule of <c>GET /api/media/{contentHash}</c> — session first,
/// then serve the uploader or a reader who sees at least one post referencing the hash, and give
/// a hash that is out of sight the very same 404 body an absent hash gets (SC-005, byte-identical).
/// </summary>
[Collection("ServiceHost")]
public sealed class MediaVisibilityTests : IClassFixture<MediaHostFixture>
{
    private readonly MediaHostFixture _host;

    public MediaVisibilityTests(MediaHostFixture host) => _host = host;

    [Fact] // AC-04
    public async Task GetMedia_ServesTheUploaderAndTheReaderOfAReferencingPost_OthersGetTheIdentical404()
    {
        var (owner, _) = await MediaHarness.SignedInAsync(_host, "vis");
        var (stranger, _) = await MediaHarness.SignedInAsync(_host, "leitor");

        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(), "mídia com visibilidade");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var hash = FeedHarness.Text(await FeedHarness.JsonOfAsync(upload), "contentHash")!;

        // The uploader always reads their own upload.
        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);

        // A post referencing the hash makes the blob readable by anyone who may see that post.
        var slug = await StreamHarness.NewContainerAsync(owner, "vis");
        var published = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "com a mídia anexada", contentHashes = new[] { hash } }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);

        // The visibility read is the CURRENT one: the container goes private, the stranger is out.
        await StreamHarness.SetVisibilityAsync(owner, slug, "private");
        var hidden = await stranger.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        // An absent hash answers the identical body — existence never leaks (SC-005).
        var absent = await stranger.SendAsync(HttpMethod.Get, $"/api/media/{new string('0', 64)}");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(
            await absent.Content.ReadAsByteArrayAsync(),
            await hidden.Content.ReadAsByteArrayAsync());

        // …and the container's new state changes nothing for the uploader.
        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);
    }

    [Fact] // AC-04 — a hash nobody ever uploaded is the same 404, never a 403 or a leak
    public async Task GetMedia_AnUnuploadedHash_Is404ForAReaderToo()
    {
        var (reader, _) = await MediaHarness.SignedInAsync(_host, "fora");
        var response = await reader.SendAsync(HttpMethod.Get, $"/api/media/{new string('f', 64)}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await FeedHarness.JsonOfAsync(response);
        Assert.Equal("not_found", body.GetProperty("error").GetProperty("code").GetString());
        Assert.False(body.GetProperty("error").TryGetProperty("traceId", out _), "o 404 da mídia não carrega traceId (SC-005)");
    }
}
