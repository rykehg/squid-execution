using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Media;
using Squid.Domain.Objects;
using Squid.Tests.Auth;
using Squid.Tests.Media;
using Squid.Tests.Posts;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-01, AC-02 and AC-04 of TASK-0039 (SPEC-0010 section G): the body of
/// <c>POST .../messages</c> takes <c>contentHashes</c> (≤ 10) and the read envelope answers with
/// the SAME <c>Attachment</c> shape a post spells (data-model §2.6) — an unknown hash, a list above
/// 10 and an image/video/gif whose media record carries no <c>Alt</c> are the 400 of the
/// PUBLICATION (AC-02), decided inside the append transaction AFTER the 428 and the 422 with
/// nothing written (AC-01) — and <c>GET /api/media/{contentHash}</c> gains GAP-25's ampliation:
/// a participant of the thread whose message references the hash reads it, everyone else keeps the
/// byte-identical 404 (AC-04).
/// </summary>
[Collection("ServiceHost")]
public sealed class ThreadAttachmentRoutesTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ThreadAttachmentRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Send_carries_contentHashes_and_the_read_envelope_returns_the_Attachment_shape()
    {
        var (sender, _) = await PostRoutes.SignedInAsync(_host, "anex1");
        var upload = await MediaHarness.UploadAsync(sender, MediaHarness.JpegWithExif(), "foto da mensagem");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var hash = uploaded.RootElement.GetProperty("contentHash").GetString()!;

        var threadId = await DirectThreadAsync(sender, "paranex1");

        // As duas recusas de AC-01: acima de 10 (a lista é o primeiro juiz, antes de resolver
        // hash algum) e hash inexistente — 400 nenhum escreve linha nem queima número.
        var eleven = Enumerable.Range(0, Attachment.MaxPerPost + 1)
            .Select(_ => new string('0', 64)).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(sender, threadId,
            new { content = "onze anexos", contentHashes = eleven })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(sender, threadId,
            new { content = "fantasma", contentHashes = new[] { new string('e', 64) } })).StatusCode);
        Assert.Empty(ItemsOf(await MessagesAsync(sender, threadId, since: 0)));

        // O envio aceito responde 200 + seq e congela a lista de §2.6 na própria resposta.
        var sent = await SendAsync(sender, threadId,
            new { content = "com anexo", contentHashes = new[] { hash } });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        using var body = JsonDocument.Parse(await sent.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("seq").GetInt64());
        var written = body.RootElement.GetProperty("attachments").EnumerateArray().Single();
        Assert.Equal(hash, written.GetProperty("contentHash").GetString());

        // E a leitura devolve a MESMA forma que PostView escreve, campo por campo.
        var plain = await SendAsync(sender, threadId, new { content = "sem anexo" });
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);

        var items = ItemsOf(await MessagesAsync(sender, threadId, since: 0));
        Assert.Equal(new long[] { 1, 2 }, items.Select(item => item.GetProperty("seq").GetInt64()).ToArray());

        var attachment = items[0].GetProperty("attachments").EnumerateArray().Single();
        Assert.Equal("image", attachment.GetProperty("kind").GetString());
        Assert.Equal(hash, attachment.GetProperty("contentHash").GetString());
        Assert.Equal("image/jpeg", attachment.GetProperty("mimeType").GetString());
        Assert.Equal($"{PostRoutes.Origin(_host)}/api/media/{hash}", attachment.GetProperty("url").GetString());
        Assert.Equal("foto da mensagem", attachment.GetProperty("alt").GetString());
        Assert.True(attachment.GetProperty("width").GetInt32() > 0, "width vem do registro do upload");
        Assert.True(attachment.GetProperty("height").GetInt32() > 0, "height vem do registro do upload");
        Assert.True(attachment.GetProperty("sizeBytes").GetInt64() > 0, "sizeBytes vem do registro do upload");
        Assert.True(attachment.TryGetProperty("blurHash", out _), "blurHash é campo de §2.6 (nulo fora de imagem/gif)");

        // Mensagem sem anexo: a lista nasce lá, vazia — mesma leitura, mesmo item.
        Assert.Empty(items[1].GetProperty("attachments").EnumerateArray());
    }

    [Fact] // AC-02
    public async Task An_image_without_alt_is_refused_only_after_the_428_and_the_422()
    {
        var (sender, senderHandle) = await PostRoutes.SignedInAsync(_host, "altmsg");

        // A regra §2.6 continua viva no P-04: o próprio upload recusa imagem sem alt (FR-022).
        Assert.Equal(HttpStatusCode.BadRequest, (await MediaHarness.UploadAsync(
            sender, MediaHarness.JpegWithExif(), string.Empty)).StatusCode);

        // Um registro que passou pelo caminho DIRETO da store (a rota jamais o gravaria) não entra
        // pelo envio: a publicação da mensagem re-aplica a mesma regra (AC-02, "não só na UI").
        var media = _host.App.Services.GetRequiredService<IMediaStore>();
        var bytes = RandomNumberGenerator.GetBytes(24);
        var withoutAlt = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await media.SaveAsync(bytes, new MediaRecord(
            withoutAlt, "image/png", string.Empty, 8, 4, null, null, bytes.Length,
            senderHandle, DateTimeOffset.UtcNow));

        // Uma thread criada SEM o aceite do aviso: a escada responde 428 antes de olhar o anexo —
        // a pré-condição de §6 é absoluta, então o hash ruim não muda a primeira resposta.
        var threadId = await CreateThreadWithoutAckAsync(sender, "paralt");
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await SendAsync(sender, threadId,
            new { content = "sem alt", contentHashes = new[] { withoutAlt } })).StatusCode);

        // Com o aceite: a recusa de AC-02 na publicação da mensagem.
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(sender, threadId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(sender, threadId,
            new { content = "sem alt", contentHashes = new[] { withoutAlt } })).StatusCode);

        // Um replyToId de fora da thread mantém a precedência da §9.9: 422 antes do 400 do anexo.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(sender, threadId,
            new
            {
                content = "com resposta e anexo ruim",
                replyToId = $"{PostRoutes.Origin(_host)}/m/alvo-de-outra-thread",
                contentHashes = new[] { withoutAlt },
            })).StatusCode);

        // Nenhuma recusa escreveu linha nem consumiu seq: o primeiro envio limpo ainda é o 1.
        var accepted = await SendAsync(sender, threadId, new { content = "agora sim" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("seq").GetInt64());
        var items = ItemsOf(await MessagesAsync(sender, threadId, since: 0));
        Assert.Single(items);
        Assert.Empty(items[0].GetProperty("attachments").EnumerateArray());
    }

    [Fact] // AC-04
    public async Task GetMedia_serves_the_participant_of_a_thread_referencing_the_hash_and_hides_it_from_everyone_else()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "visanex");
        var (peer, peerHandle) = await PostRoutes.SignedInAsync(_host, "vispar");
        var (outsider, _) = await PostRoutes.SignedInAsync(_host, "visfora");

        // Geometria própria: o upload é content-addressed e a PRIMEIRA gravação de um hash vence,
        // então duas suites desta mesma fixture que subissem o mesmo JPEG dividiriam o alt — o
        // tamanho muda os bytes e com eles o hash.
        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(6, 3), "mídia da conversa");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var hash = uploaded.RootElement.GetProperty("contentHash").GetString()!;

        // Duas conversas: o par senta com o dono (que já registrou o aceite) e o outsider participa
        // de OUTRA thread — o caso que a regra ampliada precisa continuar recusando.
        var threadId = await DirectThreadAsync(owner, peerHandle);
        _ = await DirectThreadAsync(outsider, "parfora");

        // Antes de qualquer mensagem: o par não vê nada — a ampliação nasce da referência, não do cargo.
        Assert.Equal(HttpStatusCode.NotFound,
            (await peer.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);

        var sent = await SendAsync(owner, threadId,
            new { content = "segue a mídia", contentHashes = new[] { hash } });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        // Participante da thread cuja MENSAGEM referencia o hash: vê — e o uploader continua vendo.
        Assert.Equal(HttpStatusCode.OK,
            (await peer.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);

        // O outsider participa de outra thread, é estranho a esta: 404 byte-idêntico ao hash
        // inexistente — a existência nunca vaza (SC-005).
        var hidden = await outsider.SendAsync(HttpMethod.Get, $"/api/media/{hash}");
        var absent = await outsider.SendAsync(HttpMethod.Get, $"/api/media/{new string('0', 64)}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(
            await absent.Content.ReadAsByteArrayAsync(),
            await hidden.Content.ReadAsByteArrayAsync());

        // E a parede da conversa não rachou: o par segue lendo a mensagem, o outsider, nada.
        Assert.Equal(HttpStatusCode.OK, (await PostRoutes.SendAsync(
            peer, HttpMethod.Get, MessagesPath(threadId) + "?since=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostRoutes.SendAsync(
            outsider, HttpMethod.Get, MessagesPath(threadId) + "?since=0")).StatusCode);
    }

    /// <summary>
    /// Creates a direct thread with this actor and <paramref name="participantHandle"/>, WITH the
    /// FR-032 acceptance recorded — the §9.6 gate refuses the first send without it, so most tests
    /// start from an accepted notice.
    /// </summary>
    private async Task<string> DirectThreadAsync(AuthClient owner, string participantHandle)
    {
        var threadId = await CreateThreadWithoutAckAsync(owner, participantHandle);
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(owner, threadId)).StatusCode);
        return threadId;
    }

    /// <summary>The same thread without the acceptance — the case AC-02's ladder ordering needs.</summary>
    private async Task<string> CreateThreadWithoutAckAsync(AuthClient owner, string participantHandle)
    {
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/threads",
            new { kind = "direct", participantActorIds = new[] { participantHandle } }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>The acceptance of <c>POST .../ack-no-e2ee</c> (idempotent, required before a send).</summary>
    private static Task<HttpResponseMessage> AckAsync(AuthClient client, string threadId) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, AckPath(threadId), null, client.Csrf);

    private static string AckPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/ack-no-e2ee";

    /// <summary>POST one message through the route as the contract spells it (id percent-encoded).</summary>
    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client, string threadId, object body) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, MessagesPath(threadId), body, client.Csrf);

    /// <summary>The parsed <c>GET .../messages?since=</c> body.</summary>
    private async Task<JsonElement> MessagesAsync(AuthClient client, string threadId, long since)
    {
        var response = await PostRoutes.SendAsync(
            client, HttpMethod.Get, $"{MessagesPath(threadId)}?since={since}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static JsonElement[] ItemsOf(JsonElement envelope) =>
        envelope.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();

    /// <summary>The route as the contract spells it: a thread id is a URI, so one escaped segment.</summary>
    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";
}
