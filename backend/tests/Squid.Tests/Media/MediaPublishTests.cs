using System.Net;
using Squid.Tests.Auth;
using Squid.Tests.Feed;
using Squid.Tests.Posts;
using Squid.Tests.Stream;

namespace Squid.Tests.Media;

/// <summary>
/// AC-03 of TASK-0027: the publish side of the media slice — <c>contentHashes</c> on the create
/// body turns each uploaded hash into an <c>Attachment</c> (kind from the sniffed media type, alt
/// inherited from the upload), an unknown hash and more than 10 attachments are refused, and the
/// list survives the round trip through the stored post.
/// </summary>
[Collection("ServiceHost")]
public sealed class MediaPublishTests : IClassFixture<MediaHostFixture>
{
    private readonly MediaHostFixture _host;

    public MediaPublishTests(MediaHostFixture host) => _host = host;

    [Fact] // AC-03
    public async Task Publish_WithContentHashes_AttachesTheUploadAndRoundTripsIt()
    {
        var (owner, _) = await MediaHarness.SignedInAsync(_host, "att");
        var slug = await StreamHarness.NewContainerAsync(owner, "att");

        var upload = await MediaHarness.UploadAsync(client: owner, bytes: MediaHarness.JpegWithExif(), alt: "descrição herdada");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var hash = FeedHarness.Text(await FeedHarness.JsonOfAsync(upload), "contentHash");

        var published = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "post com anexo", contentHashes = new[] { hash } }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);

        var view = await FeedHarness.JsonOfAsync(published);
        var attachments = view.GetProperty("attachments");
        Assert.Equal(1, attachments.GetArrayLength());

        var attachment = attachments[0];
        Assert.Equal("image", attachment.GetProperty("kind").GetString());
        Assert.Equal(hash, attachment.GetProperty("contentHash").GetString());
        Assert.Equal("image/jpeg", attachment.GetProperty("mimeType").GetString());
        Assert.Equal("descrição herdada", attachment.GetProperty("alt").GetString());
        Assert.False(string.IsNullOrEmpty(attachment.GetProperty("blurHash").GetString()));
        Assert.Equal(8, attachment.GetProperty("width").GetInt32());
        Assert.True(attachment.GetProperty("sizeBytes").GetInt64() > 0);
        Assert.EndsWith($"/api/media/{hash}", attachment.GetProperty("url").GetString());

        // The attachment is part of the stored post: reading it back answers the same row (§2.6).
        var read = await PostRoutes.SendAsync(
            owner, HttpMethod.Get, $"/api/posts/{Uri.EscapeDataString(view.GetProperty("id").GetString()!)}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var reread = await FeedHarness.JsonOfAsync(read);
        var stored = reread.GetProperty("attachments");
        Assert.Equal(1, stored.GetArrayLength());
        Assert.Equal(hash, stored[0].GetProperty("contentHash").GetString());
        Assert.Equal("image", stored[0].GetProperty("kind").GetString());
    }

    [Fact] // AC-03 — unknown hash and the > 10 rule are both 400
    public async Task Publish_UnknownHash_OrAboveTheAttachmentCap_Is400()
    {
        var (owner, _) = await MediaHarness.SignedInAsync(_host, "att2");
        var slug = await StreamHarness.NewContainerAsync(owner, "att2");

        var missing = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "anexo que não existe", contentHashes = new[] { new string('0', 64) } }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(
            "validation_failed",
            (await FeedHarness.JsonOfAsync(missing)).GetProperty("error").GetProperty("code").GetString());

        // Different pixels than the sibling test: same bytes would be the same content hash, and
        // the content-addressed record keeps the alt of the FIRST upload (each test brings its own).
        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(9, 5), "último anexo");
        var hash = FeedHarness.Text(await FeedHarness.JsonOfAsync(upload), "contentHash");

        // 11 entries (the cap counts LIST entries, §2.5 ≤ 10) — refused before anything is written.
        var tooMany = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "onze anexos", contentHashes = Enumerable.Repeat(hash!, 11).ToArray() }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        // Exactly 10 is the boundary that must still publish.
        var ten = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "dez anexos", contentHashes = Enumerable.Repeat(hash!, 10).ToArray() }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, ten.StatusCode);
        var view = await FeedHarness.JsonOfAsync(ten);
        Assert.Equal(10, view.GetProperty("attachments").GetArrayLength());
    }
}
