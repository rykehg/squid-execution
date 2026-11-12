using System.Net;
using System.Text;
using System.Text.Json;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-01..AC-03 of TASK-0037 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// a <c>replyToId</c> of a message of the SAME thread creates the reply and the read envelope
/// carries the reference (AC-01); a target from another thread or a target that does not exist
/// answers <c>422 invalid_reply_target</c> with nothing written (AC-02, §9.9); the send ladder
/// keeps 428 BEFORE 422 when one send misses both the FR-032 acceptance and a valid target
/// (contract §6 makes the precondition absolute); and a valid reply never widens the wall — a
/// non-participant still gets the byte-identical 404 on GET and POST (AC-03, FR-057/SC-005).
/// </summary>
[Collection("ServiceHost")]
public sealed class ThreadReplyRoutesTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ThreadReplyRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task A_reply_to_a_message_of_the_same_thread_lands_and_the_envelope_carries_the_reference()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "rep37a");
        var (peer, peerHandle) = await PostRoutes.SignedInAsync(_host, "rep37b");
        var threadId = await DirectThreadAsync(owner, peerHandle);
        // A par é participante e aceita o aviso, então LÊ a thread — quem envia aqui é o dono.
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(peer, threadId)).StatusCode);

        var original = await SendAsync(owner, threadId, "pergunta");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        using var originalJson = JsonDocument.Parse(await original.Content.ReadAsStringAsync());
        var originalId = originalJson.RootElement.GetProperty("id").GetString()!;
        Assert.Equal(1, originalJson.RootElement.GetProperty("seq").GetInt64());
        Assert.Equal(JsonValueKind.Null, originalJson.RootElement.GetProperty("replyToId").ValueKind);

        // AC-01: a resposta à MESMA thread nasce com seq 2 e a referência já no corpo do 200.
        var reply = await SendAsync(owner, threadId, "resposta", replyToId: originalId);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        using var replyJson = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        Assert.Equal(2, replyJson.RootElement.GetProperty("seq").GetInt64());
        Assert.Equal(originalId, replyJson.RootElement.GetProperty("replyToId").GetString());

        // E o envelope de leitura devolve a referência: a resposta aponta para a original, a
        // original continua sem resposta — nenhuma das duas é reescrita pelo ato de responder.
        var items = ItemsOf(await MessagesAsync(owner, threadId, since: 0));
        Assert.Equal(new long[] { 1, 2 },
            items.Select(item => item.GetProperty("seq").GetInt64()).ToArray());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("replyToId").ValueKind);
        Assert.Equal(originalId, items[1].GetProperty("replyToId").GetString());

        // A outra participante lê a MESMA referência: o texto e a referência atravessam a parede
        // juntos, sempre para quem já é participante.
        var peerItems = ItemsOf(await MessagesAsync(peer, threadId, since: 0));
        Assert.Equal(originalId, peerItems[1].GetProperty("replyToId").GetString());
    }

    [Fact] // AC-02
    public async Task A_target_from_another_thread_or_a_missing_message_is_422_and_writes_nothing()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "rep37c");
        var threadA = await DirectThreadAsync(owner, "rep37d");
        var threadB = await DirectThreadAsync(owner, "rep37e");

        var original = await SendAsync(owner, threadA, "só aqui dentro");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        using var originalJson = JsonDocument.Parse(await original.Content.ReadAsStringAsync());
        var originalId = originalJson.RootElement.GetProperty("id").GetString()!;

        // Alvo de OUTRA thread: 422 invalid_reply_target (§9.9), a palavra do contrato.
        var cross = await SendAsync(owner, threadB, "resposta atravessada", replyToId: originalId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cross.StatusCode);
        Assert.Contains("invalid_reply_target",
            await cross.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Alvo INEXISTENTE: o MESMO 422 — os dois casos andam juntos, então a recusa nunca diz
        // ao participante qual dos dois foi (nem vazaria existência de mensagem alheia).
        var ghost = await SendAsync(owner, threadA, "resposta ao nada", replyToId: GhostId());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ghost.StatusCode);
        Assert.Contains("invalid_reply_target",
            await ghost.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // As duas recusas não gravaram nada: o histórico de A segue só com a original e o de B
        // segue vazio (nenhum seq queimado, nenhuma linha órfã).
        Assert.Equal(new long[] { 1 }, SeqsOf(await MessagesAsync(owner, threadA, since: 0)));
        Assert.Empty(ItemsOf(await MessagesAsync(owner, threadB, since: 0)));

        // E a recusa não envenenou nada: um alvo DESTA thread passa logo em seguida, com a
        // referência intacta.
        var valid = await SendAsync(owner, threadA, "resposta de verdade", replyToId: originalId);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var validJson = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
        Assert.Equal(2, validJson.RootElement.GetProperty("seq").GetInt64());
        Assert.Equal(originalId, validJson.RootElement.GetProperty("replyToId").GetString());
    }

    [Fact] // ordem da escada registrada no log: 428 do §9.6 antes do 422 do §9.9
    public async Task A_send_missing_both_the_acceptance_and_a_valid_target_answers_428_then_422()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "rep37f");
        var (peer, peerHandle) = await PostRoutes.SignedInAsync(_host, "rep37g");
        var threadId = await DirectThreadAsync(owner, peerHandle); // só o dono aceitou o aviso
        var root = await SendAsync(owner, threadId, "base");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);

        // A par NUNCA aceitou o aviso e ainda manda um alvo impossível: o §9.6 responde primeiro
        // — o contrato §6 diz em cláusula absoluta "sem aceite, o envio responde 428", então um
        // pedido duplamente inválido é 428, nunca 422.
        var unacked = await SendAsync(peer, threadId, "sem aceite e alvo ruim", replyToId: GhostId());
        Assert.Equal(HttpStatusCode.PreconditionRequired, unacked.StatusCode);

        // Aceite gravado, MESMO pedido: agora cai no 422 do §9.9 — a ordem é a mesma nos dois
        // sentidos (primeiro o pré-condicional, depois o alvo da resposta).
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(peer, threadId)).StatusCode);
        var invalid = await SendAsync(peer, threadId, "sem aceite e alvo ruim", replyToId: GhostId());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Contains("invalid_reply_target",
            await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // E as duas recusas não tocaram nada: a história segue só com a base do dono.
        Assert.Equal(new long[] { 1 }, SeqsOf(await MessagesAsync(owner, threadId, since: 0)));
    }

    [Fact] // AC-03
    public async Task A_valid_reply_never_widens_the_wall_and_the_stranger_keeps_the_byte_identical_404()
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, "rep37h");
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "rep37i");
        var threadId = await DirectThreadAsync(owner, "rep37j");

        var original = await SendAsync(owner, threadId, "pergunta");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        using var originalJson = JsonDocument.Parse(await original.Content.ReadAsStringAsync());
        var originalId = originalJson.RootElement.GetProperty("id").GetString()!;

        // Resposta VÁLIDA existe dentro da thread (o alvo é desta mesma thread)…
        var reply = await SendAsync(owner, threadId, "resposta", replyToId: originalId);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);

        // …e muda nada para quem está fora: GET e POST da thread respondem o MESMO 404 byte a
        // byte que o GET e o POST da thread que nunca existiu — sem traceId, sem participantes,
        // sem o id (SC-005). O POST de fora CARREGA um replyToId válido e mesmo assim não passa:
        // a sonda de participante (404) vem antes do append, então a referência não abre porta.
        var absentId = $"{PostRoutes.Origin(_host)}/t/{Guid.NewGuid():N}";
        var responses = new[]
        {
            await PostRoutes.SendAsync(stranger, HttpMethod.Get, $"{MessagesPath(threadId)}?since=0"),
            await PostRoutes.SendAsync(stranger, HttpMethod.Post, MessagesPath(threadId),
                new { content = "não deveria entrar", replyToId = originalId }, stranger.Csrf),
            await PostRoutes.SendAsync(stranger, HttpMethod.Get, $"{MessagesPath(absentId)}?since=0"),
            await PostRoutes.SendAsync(stranger, HttpMethod.Post, MessagesPath(absentId),
                new { content = "não deveria entrar" }, stranger.Csrf),
        };
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var bodies = new List<byte[]>();
        foreach (var response in responses)
        {
            bodies.Add(await response.Content.ReadAsByteArrayAsync());
        }

        Assert.Equal(bodies[0], bodies[1]); // estrangeiro × GET/POST com replyToId válido
        Assert.Equal(bodies[0], bodies[2]); // estrangeiro × inexistente
        Assert.Equal(bodies[0], bodies[3]); // inexistente × GET/POST
        var refusal = Encoding.UTF8.GetString(bodies[0]);
        Assert.Contains("not_found", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(threadId, refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(ownerHandle, refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(originalId, refusal, StringComparison.Ordinal);

        // Dentro da parede nada mudou: as duas mensagens seguem legíveis com a referência.
        var inside = ItemsOf(await MessagesAsync(owner, threadId, since: 0));
        Assert.Equal(new long[] { 1, 2 },
            inside.Select(item => item.GetProperty("seq").GetInt64()).ToArray());
        Assert.Equal(originalId, inside[1].GetProperty("replyToId").GetString());

        // E o dono segue enviando — inclusive respondendo de novo, sem a resposta abrir brecha.
        var keeper = await SendAsync(owner, threadId, "dono continua", replyToId: originalId);
        Assert.Equal(HttpStatusCode.OK, keeper.StatusCode);
    }

    /// <summary>
    /// Creates a direct thread with this actor and <paramref name="participantHandle"/>, with the
    /// creator's FR-032 acceptance recorded — the §9.6 gate refuses the first message of any
    /// sender that never acked, so every send of this suite starts from an accepted notice.
    /// </summary>
    private async Task<string> DirectThreadAsync(AuthClient owner, string participantHandle)
    {
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/threads",
            new { kind = "direct", participantActorIds = new[] { participantHandle } }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var threadId = json.RootElement.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(owner, threadId)).StatusCode);
        return threadId;
    }

    /// <summary>The acceptance of <c>POST .../ack-no-e2ee</c> (idempotent, required before a send).</summary>
    private static Task<HttpResponseMessage> AckAsync(AuthClient client, string threadId) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, AckPath(threadId), null, client.Csrf);

    /// <summary>POST one message as §6 spells it; <c>replyToId</c> null is "no reply", not a target.</summary>
    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client, string threadId, string content, string? replyToId = null) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, MessagesPath(threadId),
            new { content, replyToId }, client.Csrf);

    /// <summary>The parsed <c>GET .../messages?since=</c> body (items + nextSince + lastReadSeq).</summary>
    private async Task<JsonElement> MessagesAsync(AuthClient client, string threadId, long since)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get,
            $"{MessagesPath(threadId)}?since={since}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    /// <summary>A message id nobody ever wrote — the §9.9 "does not exist" half of AC-02.</summary>
    private string GhostId() =>
        $"{PostRoutes.Origin(_host)}/m/{Convert.ToHexStringLower(Guid.NewGuid().ToByteArray())}";

    private static JsonElement[] ItemsOf(JsonElement envelope) =>
        envelope.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();

    private static long[] SeqsOf(JsonElement envelope) =>
        ItemsOf(envelope).Select(item => item.GetProperty("seq").GetInt64()).ToArray();

    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";

    private static string AckPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/ack-no-e2ee";
}
