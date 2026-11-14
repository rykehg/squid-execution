using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Federation;
using Squid.Domain;
using Squid.Domain.Containers;
using Squid.Domain.Objects;
using Squid.Protocols.ActivityPub;
using Squid.Tests.Media;
using Squid.Tests.Posts;

namespace Squid.Tests.Federation;

/// <summary>
/// AC-01 of TASK-0053 — the outbound half of the media rule of SPEC-0011 section F (contract
/// §7.1): the federated <c>Create</c> of a post WITH attachments serializes the AS2
/// <c>attachment</c> whose blob url is the contract's anonymous <c>GET /media/{hash}</c> — never
/// the session-gated row url, which would answer the peer a 401 — and the tolerant reader of
/// TASK-0044 decodes it back (round trip out→in). A post without attachments keeps the
/// byte-identical document, proved against the literal document of the code before this task.
/// The third fact runs the real fan-out: a publish with a real upload enqueues the payload.
/// </summary>
[Collection("ServiceHost")]
public sealed class OutboundAttachmentTests : IClassFixture<InboxHostFixture>
{
    private const string Origin = "https://exemplo.test";

    private const string Hash =
        "1f0e3dad99908345f743e048f667f0169cbe87e278c1f5f4063dd26760642858";

    private const string ActivityId = "https://exemplo.test/activities/a1";

    private static readonly string[] To = ["https://www.w3.org/ns/activitystreams#Public"];

    private static readonly string[] Cc = ["https://exemplo.test/users/alice/followers"];

    private readonly InboxHostFixture _host;

    public OutboundAttachmentTests(InboxHostFixture host) => _host = host;

    [Fact] // AC-01 — a rota do contrato na saída + round-trip pelo leitor tolerante
    public async Task Ac01_the_create_of_a_post_with_attachments_carries_the_contract_blob_url_and_the_tolerant_reader_reads_it_back()
    {
        var post = Post() with
        {
            Attachments =
            [
                new Attachment
                {
                    Kind = AttachmentKind.Image,
                    ContentHash = Hash,
                    MimeType = "image/png",
                    // A url da LINHA (GAP-25, sessão) é exatamente o que não pode sair na wire.
                    Url = $"{Origin}/api/media/{Hash}",
                    BlurHash = "LGCQ?B00?b~q",
                    Alt = "descricao da imagem",
                    Width = 8,
                    Height = 4,
                    SizeBytes = 42,
                },
                new Attachment
                {
                    Kind = AttachmentKind.Link,
                    ContentHash = string.Empty,
                    MimeType = "text/html",
                    Url = "https://cdn.example.com/artigo",
                    Alt = "titulo do artigo",
                    SizeBytes = 0,
                },
            ],
        };

        var json = ObjectJsonLd.Create(ActivityId, post, Community(), Origin, To, Cc);

        using var document = JsonDocument.Parse(json);
        var attachment = document.RootElement.GetProperty("object").GetProperty("attachment");
        Assert.Equal(2, attachment.GetArrayLength());

        var blob = attachment[0];
        Assert.Equal("Image", blob.GetProperty("type").GetString());
        Assert.Equal("image/png", blob.GetProperty("mediaType").GetString());
        Assert.Equal($"{Origin}/media/{Hash}", blob.GetProperty("url").GetString());
        Assert.Equal("descricao da imagem", blob.GetProperty("name").GetString());
        Assert.Equal(Hash, blob.GetProperty("sha256").GetString());

        var link = attachment[1];
        Assert.Equal("Link", link.GetProperty("type").GetString());
        Assert.Equal("text/html", link.GetProperty("mediaType").GetString());
        Assert.Equal("https://cdn.example.com/artigo", link.GetProperty("url").GetString());
        Assert.False(link.TryGetProperty("sha256", out _), "link externo não tem blob que digitar");

        // A guarda do 401: a rota de sessão da linha nunca chega ao par.
        Assert.DoesNotContain("/api/media/", json, StringComparison.Ordinal);

        // Round trip saída→entrada: o leitor tolerante da TASK-0044 decodifica de volta.
        var decoded = await ActivityJsonLd.ReceiveAsync(Encoding.UTF8.GetBytes(json));
        Assert.Equal(2, decoded.Object!.Attachments.Count);
        Assert.Equal($"{Origin}/media/{Hash}", decoded.Object.Attachments[0].Url);
        Assert.Equal("image/png", decoded.Object.Attachments[0].MediaType);
        Assert.Equal("descricao da imagem", decoded.Object.Attachments[0].Alt);
        Assert.Equal(Hash, decoded.Object.Attachments[0].Sha256);
        Assert.Equal("Image", decoded.Object.Attachments[0].Type);
        Assert.Equal("https://cdn.example.com/artigo", decoded.Object.Attachments[1].Url);
        Assert.Equal("titulo do artigo", decoded.Object.Attachments[1].Alt);
    }

    [Fact] // AC-01 — sem anexos o documento é o de hoje, byte a byte
    public void Ac01_a_note_and_a_create_without_attachments_stay_byte_identical()
    {
        var post = Post();

        var note = ObjectJsonLd.Note(post, Community(), Origin);
        Assert.Equal(NoteWithoutAttachments, note);
        Assert.DoesNotContain("attachment", note, StringComparison.Ordinal);

        var create = ObjectJsonLd.Create(ActivityId, post, Community(), Origin, To, Cc);
        Assert.Equal(CreateWithoutAttachments, create);
        Assert.DoesNotContain("attachment", create, StringComparison.Ordinal);
    }

    [Fact] // AC-01 — o fan-out real de uma publicação com mídia enfileira o attachment
    public async Task Ac01_a_real_publish_with_media_enqueues_a_create_carrying_the_attachment()
    {
        var (owner, handle, slug) = await InboxHarness.CommunityAsync(_host, "ac01m");
        await FollowAsync(handle, $"https://remote.example/users/{InboxHarness.Unique("ac01mf")}");

        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(), "foto federada");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var hash = uploaded.RootElement.GetProperty("contentHash").GetString()!;

        var response = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "post com midia para fora", contentHashes = new[] { hash } }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var objectId = body.RootElement.GetProperty("id").GetString()!;

        var row = Assert.Single(await Store.ForObjectAsync(objectId, PeerActivity.Create));
        using var payload = JsonDocument.Parse(row.Payload);
        var wire = Assert.Single(payload.RootElement.GetProperty("object")
            .GetProperty("attachment").EnumerateArray());
        Assert.Equal($"{_host.Origin}/media/{hash}", wire.GetProperty("url").GetString());
        Assert.Equal("image/jpeg", wire.GetProperty("mediaType").GetString());
        Assert.Equal(hash, wire.GetProperty("sha256").GetString());

        // …e o leitor tolerante decodifica esse MESMO payload enfileirado de volta.
        var decoded = await ActivityJsonLd.ReceiveAsync(Encoding.UTF8.GetBytes(row.Payload));
        var readBack = Assert.Single(decoded.Object!.Attachments);
        Assert.Equal($"{_host.Origin}/media/{hash}", readBack.Url);
        Assert.Equal(hash, readBack.Sha256);
    }

    // ---- harness --------------------------------------------------------------------------

    private IPeerDeliveryStore Store =>
        _host.App.Services.GetRequiredService<IPeerDeliveryStore>();

    private async Task FollowAsync(string handle, params string[] followers)
    {
        var follows = _host.App.Services.GetRequiredService<IFollowStore>();
        foreach (var follower in followers)
        {
            await follows.AddFollowerAsync(handle, follower, DateTimeOffset.UtcNow);
        }
    }

    private static PostObject Post() => new()
    {
        Id = "https://exemplo.test/o/n1",
        AuthorActorId = "https://exemplo.test/users/alice",
        ContainerId = "https://exemplo.test/c/geral",
        ContainerSlug = "geral",
        Kind = ObjectKind.Post,
        Content = "texto sem anexo",
        VisibilityAtPublish = VisibilityPolicy.Public,
        PolicyVersion = 1,
        Seq = 1,
        Depth = 0,
        CreatedAt = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero),
    };

    private static Container Community() => new()
    {
        Id = "https://exemplo.test/c/geral",
        Slug = "geral",
        Kind = ContainerKind.Community,
        Name = "Comunidade",
        Summary = "",
        OwnerActorId = "https://exemplo.test/users/alice",
        Policy = ContainerPolicy.Community(),
        Listed = true,
        MemberCount = 1,
        CreatedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
    };

    /// <summary>The Note of the fixture BEFORE this task — the literal document the code produced with no attachment member.</summary>
    private const string NoteWithoutAttachments =
        """{"@context":["https://www.w3.org/ns/activitystreams",{"squid":"https://exemplo.test/ns/squid#"}],"id":"https://exemplo.test/o/n1","type":"Note","attributedTo":"https://exemplo.test/users/alice","content":"texto sem anexo","published":"2026-11-14T10:00:00Z","to":["https://www.w3.org/ns/activitystreams#Public"],"cc":["https://exemplo.test/users/alice/followers"],"squid:kind":"post","squid:slug":"geral","squid:policy":{"posting":"members","visibility":"public","join":"open","interaction":{"canComment":"members","canReact":"members","canShare":"members","canAttach":"members"},"version":1}}""";

    /// <summary>The Create of the fixture BEFORE this task — the same Note inlined in the §2.1 envelope.</summary>
    private const string CreateWithoutAttachments =
        """{"@context":"https://www.w3.org/ns/activitystreams","id":"https://exemplo.test/activities/a1","type":"Create","actor":"https://exemplo.test/users/alice","published":"2026-11-14T10:00:00Z","to":["https://www.w3.org/ns/activitystreams#Public"],"cc":["https://exemplo.test/users/alice/followers"],"audience":"https://exemplo.test/c/geral","object":"""
        + NoteWithoutAttachments + "}";
}
