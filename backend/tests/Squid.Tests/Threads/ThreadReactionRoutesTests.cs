using System.Net;
using System.Text;
using System.Text.Json;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-01..AC-04 of TASK-0038 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// ONE row per (message, actor, emoji) — repeating the same triple changes nothing while a second,
/// DIFFERENT emoji of the same actor is its own row (AC-01), and the §9.6 gate does not reach this
/// path because section D binds it to the SEND of a message; an emoji outside
/// <c>Reactions:AllowedEmojis</c> refused with the house <c>422 invalid_emoji</c> before a row
/// exists (AC-02, GAP-34); a DELETE that takes the actor's own row, answers 404 when there is none
/// and shows a count reconciled from the rows on every read (AC-03, never a stored counter); and
/// the byte-identical 404 a stranger keeps on the read AND on both reaction routes, with no count
/// ever leaving the participant wall (AC-04, §9.10).
/// </summary>
[Collection("ServiceHost")]
public sealed class ThreadReactionRoutesTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ThreadReactionRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task The_same_message_actor_emoji_is_one_row_and_repeating_it_changes_nothing()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "rea38a");
        var (peer, peerHandle) = await PostRoutes.SignedInAsync(_host, "rea38b");
        var threadId = await DirectThreadAsync(owner, peerHandle); // só o dono aceitou o aviso
        var messageId = await SendAsync(owner, threadId, "uma pergunta");

        // AC-01, primeira linha: 200 com a projeção derivada — 👍 com contagem 1 e mine do dono.
        var first = await ReactAsync(owner, threadId, messageId, "👍");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(new[] { (Emoji: "👍", Count: 1L, Mine: true) }, await ReactionsAsync(first));

        // Repetir o MESMO (mensagem, ator, emoji) é idempotente: mesma resposta, mesma contagem,
        // nenhuma segunda linha — o que se lê é COUNT(*) das linhas que existem AGORA.
        var repeat = await ReactAsync(owner, threadId, messageId, "👍");
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(new[] { (Emoji: "👍", Count: 1L, Mine: true) }, await ReactionsAsync(repeat));
        Assert.Equal(new[] { (Emoji: "👍", Count: 1L, Mine: true) },
            await ReactionsAsync(owner, threadId, messageId));

        // Um segundo emoji DIFERENTE do mesmo ator é outra linha (FR-058: uma reação por ator e
        // por emoji) — as duas convivem, cada uma com contagem 1.
        var second = await ReactAsync(owner, threadId, messageId, "🎉");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            new[] { (Emoji: "👍", Count: 1L, Mine: true), (Emoji: "🎉", Count: 1L, Mine: true) },
            await ReactionsAsync(second));

        // A reação NÃO passa pelo gate 428 (SPEC-0010 §D trava o envio, não a reação): a par nunca
        // aceitou o aviso — o envio dela é 428 e a reação dela é 200.
        Assert.Equal(HttpStatusCode.PreconditionRequired,
            (await SendRawAsync(peer, threadId, "sem aceite nenhum")).StatusCode);
        var peerReact = await ReactAsync(peer, threadId, messageId, "👍");
        Assert.Equal(HttpStatusCode.OK, peerReact.StatusCode);
        Assert.Equal(
            new[] { (Emoji: "👍", Count: 2L, Mine: true), (Emoji: "🎉", Count: 1L, Mine: false) },
            await ReactionsAsync(peerReact));

        // E a leitura do dono conta as MESMAS linhas: 👍 com 2 (dono + par), 🎉 só do dono.
        Assert.Equal(
            new[] { (Emoji: "👍", Count: 2L, Mine: true), (Emoji: "🎉", Count: 1L, Mine: true) },
            await ReactionsAsync(owner, threadId, messageId));
    }

    [Fact] // AC-02
    public async Task An_emoji_outside_Reactions_AllowedEmojis_is_422_and_never_reaches_a_row()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "rea38c");
        var threadId = await DirectThreadAsync(owner, PostRoutes.Handle("par38c"));
        var messageId = await SendAsync(owner, threadId, "primeiro");

        // O conjunto padrão aceita 🎉 (config vazia ⇒ os defaults de GAP-34, com 👍 sempre presente).
        Assert.Equal(HttpStatusCode.OK,
            (await ReactAsync(owner, threadId, messageId, "🎉")).StatusCode);

        // 🚀 está fora da lista: 422 invalid_emoji no formato da casa (com traceId) e nada gravado.
        var refused = await ReactAsync(owner, threadId, messageId, "🚀");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        var refusedBody = await refused.Content.ReadAsStringAsync();
        Assert.Contains("invalid_emoji", refusedBody, StringComparison.Ordinal);
        Assert.Contains("traceId", refusedBody, StringComparison.Ordinal);
        Assert.Equal(new[] { (Emoji: "🎉", Count: 1L, Mine: true) },
            await ReactionsAsync(owner, threadId, messageId));

        // A recusa não envenenou nada: um emoji da lista passa logo em seguida.
        Assert.Equal(HttpStatusCode.OK,
            (await ReactAsync(owner, threadId, messageId, "❤️")).StatusCode);

        // O DELETE anda pela MESMA escada: emoji fora da lista é 422 — nunca um efeito e nunca um
        // 404 que contaria a existência de uma linha.
        var refusedDelete = await UnreactAsync(owner, threadId, messageId, "🚀");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refusedDelete.StatusCode);
        Assert.Contains("invalid_emoji",
            await refusedDelete.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Sem ?emoji= nenhum: a MESMA palavra — a recusa do valor nunca depende de haver linha.
        var empty = await UnreactAsync(owner, threadId, messageId, emoji: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Contains("invalid_emoji",
            await empty.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // E as linhas seguem intactas: 🎉 e ❤️ com contagem 1 cada.
        Assert.Equal(
            new[] { (Emoji: "🎉", Count: 1L, Mine: true), (Emoji: "❤️", Count: 1L, Mine: true) },
            await ReactionsAsync(owner, threadId, messageId));
    }

    [Fact] // AC-03
    public async Task Deleting_takes_the_actor_row_the_count_follows_the_rows_and_a_missing_one_is_404()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "rea38d");
        var (peer, peerHandle) = await PostRoutes.SignedInAsync(_host, "rea38e");
        var threadId = await DirectThreadAsync(owner, peerHandle);
        var messageId = await SendAsync(owner, threadId, "contar linhas");

        await ReactAsync(owner, threadId, messageId, "👍");
        await ReactAsync(owner, threadId, messageId, "🎉");
        await ReactAsync(peer, threadId, messageId, "👍");

        // Duas linhas de 👍 (dono + par) e uma de 🎉, lidas na ordem em que nasceram.
        Assert.Equal(
            new[] { (Emoji: "👍", Count: 2L, Mine: true), (Emoji: "🎉", Count: 1L, Mine: true) },
            await ReactionsAsync(owner, threadId, messageId));

        // DELETE ?emoji=👍 apaga a LINHA DO DONO: a do par sobrevive, então a contagem cai para 1
        // e mine passa a falso — a projeção segue a existência das linhas, nunca um contador.
        var removed = await UnreactAsync(owner, threadId, messageId, "👍");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(
            new[] { (Emoji: "🎉", Count: 1L, Mine: true), (Emoji: "👍", Count: 1L, Mine: false) },
            await ReactionsAsync(removed));

        // Remover de novo: a linha já não existe → 404 com o MESMO corpo das rotas de thread e sem
        // traceId (a recusa não distingue "nunca existiu" de "já saiu").
        var again = await UnreactAsync(owner, threadId, messageId, "👍");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        var againBody = await again.Content.ReadAsByteArrayAsync();
        var againText = Encoding.UTF8.GetString(againBody);
        Assert.Contains("not_found", againText, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", againText, StringComparison.Ordinal);

        // Nunca reagido: o 🎉 do par é 404 para o par, byte a byte o MESMO corpo.
        var never = await UnreactAsync(peer, threadId, messageId, "🎉");
        Assert.Equal(HttpStatusCode.NotFound, never.StatusCode);
        Assert.Equal(againBody, await never.Content.ReadAsByteArrayAsync());

        // Derivada: nada além das linhas mudou — o dono lê 👍 1 (do par) e 🎉 1 (o seu)…
        Assert.Equal(
            new[] { (Emoji: "🎉", Count: 1L, Mine: true), (Emoji: "👍", Count: 1L, Mine: false) },
            await ReactionsAsync(owner, threadId, messageId));
        // …e o par lê as MESMAS linhas com mine trocado.
        Assert.Equal(
            new[] { (Emoji: "🎉", Count: 1L, Mine: false), (Emoji: "👍", Count: 1L, Mine: true) },
            await ReactionsAsync(peer, threadId, messageId));
    }

    [Fact] // AC-04
    public async Task A_stranger_gets_one_404_body_on_read_and_on_both_reaction_routes()
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, "rea38f");
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "rea38g");
        var threadId = await DirectThreadAsync(owner, PostRoutes.Handle("par38f"));
        var messageId = await SendAsync(owner, threadId, "dentro da parede");
        Assert.Equal(HttpStatusCode.OK, (await ReactAsync(owner, threadId, messageId, "👍")).StatusCode);

        // Uma segunda thread do dono com a MESMA mensagem de fora: alvo cruzado para o 404 dela.
        var otherThread = await DirectThreadAsync(owner, PostRoutes.Handle("par38g"));
        var otherMessage = await SendAsync(owner, otherThread, "outra conversa");

        var absentId = $"{PostRoutes.Origin(_host)}/t/{Guid.NewGuid():N}";
        var responses = new[]
        {
            // O leitor de fora, na leitura e nas DUAS rotas de reação (emoji válido)…
            await PostRoutes.SendAsync(stranger, HttpMethod.Get, $"{MessagesPath(threadId)}?since=0"),
            await PostRoutes.SendAsync(stranger, HttpMethod.Post, ReactionsPath(threadId, messageId),
                new { emoji = "👍" }, stranger.Csrf),
            await PostRoutes.SendAsync(stranger, HttpMethod.Delete,
                $"{ReactionsPath(threadId, messageId)}?emoji={Uri.EscapeDataString("👍")}",
                null, stranger.Csrf),
            // …com emoji FORA da lista: a sonda de 404 vem antes da validação, então o corpo não
            // muda…
            await PostRoutes.SendAsync(stranger, HttpMethod.Post, ReactionsPath(threadId, messageId),
                new { emoji = "🚀" }, stranger.Csrf),
            // …e na thread que nunca existiu, na leitura e na reação.
            await PostRoutes.SendAsync(stranger, HttpMethod.Get, $"{MessagesPath(absentId)}?since=0"),
            await PostRoutes.SendAsync(stranger, HttpMethod.Post, ReactionsPath(absentId, messageId),
                new { emoji = "👍" }, stranger.Csrf),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var bodies = new List<byte[]>();
        foreach (var response in responses)
        {
            bodies.Add(await response.Content.ReadAsByteArrayAsync());
        }

        Assert.All(bodies, body => Assert.Equal(bodies[0], body)); // um corpo só, seis casos

        // E o DONO reagindo à mensagem de OUTRA thread recebe exatamente o MESMO corpo: pertencer
        // a uma thread não faz a mensagem da thread alheia existir — 404, nunca 422 (SC-005).
        var cross = await ReactAsync(owner, otherThread, messageId, "👍");
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        Assert.Equal(bodies[0], await cross.Content.ReadAsByteArrayAsync());

        // A recusa não vaza nada além do envelope: sem traceId, sem ids, sem participante…
        var text = Encoding.UTF8.GetString(bodies[0]);
        Assert.Contains("not_found", text, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", text, StringComparison.Ordinal);
        Assert.DoesNotContain(threadId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(messageId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(otherMessage, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ownerHandle, text, StringComparison.Ordinal);
        // …e sem contagem, sem reação e sem emoji algum: §9.10 (reactions só para participantes).
        Assert.DoesNotContain("reactions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("count", text, StringComparison.Ordinal);
        Assert.DoesNotContain("emoji", text, StringComparison.Ordinal);

        // Dentro da parede nada mudou: o dono lê a reação, e repetir a reação segue idempotente.
        Assert.Equal(new[] { (Emoji: "👍", Count: 1L, Mine: true) },
            await ReactionsAsync(owner, threadId, messageId));
        Assert.Equal(HttpStatusCode.OK,
            (await ReactAsync(owner, threadId, messageId, "👍")).StatusCode);
        Assert.Equal(new[] { (Emoji: "👍", Count: 1L, Mine: true) },
            await ReactionsAsync(owner, threadId, messageId));
    }

    /// <summary>
    /// Creates a direct thread with this actor and <paramref name="participantHandle"/>, with the
    /// creator's FR-032 acceptance recorded — the §9.6 gate refuses the first send of a sender that
    /// never acked (a REACTION never passes that gate at all, which AC-01's own case proves).
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

    /// <summary>POSTs one message exactly as §6 spells it, without judging the answer (the 428 case).</summary>
    private static Task<HttpResponseMessage> SendRawAsync(
        AuthClient client, string threadId, string content) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, MessagesPath(threadId),
            new { content }, client.Csrf);

    /// <summary>POSTs one message and asserts the 200, returning the id §6 answers with.</summary>
    private async Task<string> SendAsync(AuthClient client, string threadId, string content)
    {
        var response = await SendRawAsync(client, threadId, content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary><c>POST .../reactions</c> with <c>{emoji}</c> as contract §6 spells it.</summary>
    private static Task<HttpResponseMessage> ReactAsync(
        AuthClient client, string threadId, string messageId, string emoji) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, ReactionsPath(threadId, messageId),
            new { emoji }, client.Csrf);

    /// <summary>
    /// <c>DELETE .../reactions?emoji=</c>; <c>emoji</c> null sends NO query value at all (the
    /// route then refuses the empty value as <c>invalid_emoji</c>).
    /// </summary>
    private static Task<HttpResponseMessage> UnreactAsync(
        AuthClient client, string threadId, string messageId, string? emoji)
    {
        var path = emoji is null
            ? ReactionsPath(threadId, messageId)
            : $"{ReactionsPath(threadId, messageId)}?emoji={Uri.EscapeDataString(emoji)}";
        return PostRoutes.SendAsync(client, HttpMethod.Delete, path, null, client.Csrf);
    }

    /// <summary>The parsed <c>reactions</c> of a reaction-route answer (<c>{reactions: [...]}</c>).</summary>
    private static async Task<(string Emoji, long Count, bool Mine)[]> ReactionsAsync(
        HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ProjectionOf(json.RootElement);
    }

    /// <summary>The same projection read off the message of the <c>GET .../messages</c> envelope.</summary>
    private async Task<(string Emoji, long Count, bool Mine)[]> ReactionsAsync(
        AuthClient client, string threadId, string messageId)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get,
            $"{MessagesPath(threadId)}?since=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == messageId);
        return ProjectionOf(message);
    }

    /// <summary>The <c>{emoji, count, mine}</c> items of one message, in the order the server sent them.</summary>
    private static (string Emoji, long Count, bool Mine)[] ProjectionOf(JsonElement message) =>
        message.GetProperty("reactions").EnumerateArray().Select(entry => (
            Emoji: entry.GetProperty("emoji").GetString()!,
            Count: entry.GetProperty("count").GetInt64(),
            Mine: entry.GetProperty("mine").GetBoolean())).ToArray();

    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";

    private static string ReactionsPath(string threadId, string messageId) =>
        $"{MessagesPath(threadId)}/{Uri.EscapeDataString(messageId)}/reactions";

    private static string AckPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/ack-no-e2ee";
}
