using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-01..AC-05 of TASK-0036 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// the send gate of contract §9.6 — first message without the FR-032 acceptance is
/// <c>428 precondition_required</c>, the same send works after the ack, one actor's ack never
/// unlocks another, and repeating the ack is a no-op (AC-01/AC-02); the read marker of FR-033 —
/// it only advances, and a seq above the thread total is 400 (AC-03); <c>lastReadSeq</c> in the
/// list and in the message envelope, reachable only by participants whose refusal stays the
/// byte-identical 404 (AC-04); and a <c>Suspended</c> actor whose send is denied AND recorded on
/// the trail while an active participant keeps sending (AC-05, data-model section 3.1).
/// </summary>
[Collection("ServiceHost")]
public sealed class ThreadAckAndReadRoutesTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ThreadAckAndReadRoutesTests(AuthHostFixture host) => _host = host;

    private IPolicyAuditStore Audit =>
        _host.App.Services.GetRequiredService<IPolicyAuditStore>();

    private IActorStatusStore Statuses =>
        _host.App.Services.GetRequiredService<IActorStatusStore>();

    [Fact] // AC-01 + AC-02
    public async Task Sending_without_the_notice_acceptance_is_428_until_each_actor_acks_and_the_ack_is_idempotent()
    {
        var (sender, senderHandle) = await PostRoutes.SignedInAsync(_host, "ac36a");
        var (peer, peerHandle) = await PostRoutes.SignedInAsync(_host, "ac36b");
        var senderId = $"{PostRoutes.Origin(_host)}/users/{senderHandle}";
        var threadId = await CreateAsync(sender, "direct", peerHandle);

        // ANTES (o par before/after do §9.6): a primeira mensagem sem aceite é 428, e a recusa
        // não escreve nada — a história da thread continua vazia.
        var before = await SendAsync(sender, threadId, "depois do aviso");
        Assert.Equal(HttpStatusCode.PreconditionRequired, before.StatusCode);
        Assert.Contains("precondition_required",
            await before.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(await ItemsAsync(sender, threadId));

        // DEPOIS: o MESMO envio do MESMO ator passa logo após o aceite — 200 com seq 1, ou seja o
        // 428 anterior não queimou número nenhum.
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(sender, threadId)).StatusCode);
        var after = await SendAsync(sender, threadId, "depois do aviso");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using (var json = JsonDocument.Parse(await after.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, json.RootElement.GetProperty("seq").GetInt64());
        }

        // Idempotente (AC-01): repetir o aceite responde 200 e a linha do registro continua sendo
        // UMA só — o registro é por (ator, thread).
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(sender, threadId)).StatusCode);
        Assert.Equal(1, await CountAcksAsync(threadId, senderId));

        // Aceite de um ator NÃO libera outro (AC-02): a par da mesma thread segue com o 428…
        var peerBefore = await SendAsync(peer, threadId, "e eu?");
        Assert.Equal(HttpStatusCode.PreconditionRequired, peerBefore.StatusCode);

        // …e passa com o ACEITE dela — os dois envios convivem na mesma ordem total.
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(peer, threadId)).StatusCode);
        var peerAfter = await SendAsync(peer, threadId, "e eu?");
        Assert.Equal(HttpStatusCode.OK, peerAfter.StatusCode);
        Assert.Equal(new long[] { 1, 2 }, await SeqsAsync(sender, threadId));
    }

    [Fact] // AC-03
    public async Task The_read_marker_only_advances_and_a_seq_above_the_thread_total_is_400()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac36c");
        var threadId = await CreateAsync(owner, "direct", "ac36cpar");
        await AckAsync(owner, threadId);
        for (var index = 1; index <= 3; index++)
        {
            Assert.Equal(HttpStatusCode.OK,
                (await SendAsync(owner, threadId, $"mensagem {index}")).StatusCode);
        }

        // Nasce 0 (nenhum pedido de leitura ainda) e avança até 2 — a resposta é o valor gravado.
        Assert.Equal(0, await LastReadInListAsync(owner, threadId));
        Assert.Equal(2, await MarkReadAsync(owner, threadId, 2));

        // Regresso ignorado (GAP-38): pedir 1 NÃO retrocede — o valor em vigor continua 2.
        Assert.Equal(2, await MarkReadAsync(owner, threadId, 1));
        Assert.Equal(2, await LastReadInListAsync(owner, threadId));

        // Acima do total da thread (3) → 400, e a recusa não move o marcador.
        var above = await RawReadAsync(owner, threadId, 4);
        Assert.Equal(HttpStatusCode.BadRequest, above.StatusCode);
        Assert.Contains("validation_failed",
            await above.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(2, await LastReadInListAsync(owner, threadId));

        // Sem seq e com seq negativo são payloads esta rota não aceita (400), marcador intacto.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostRoutes.SendAsync(owner, HttpMethod.Post, ReadPath(threadId),
                new { }, owner.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await RawReadAsync(owner, threadId, -1)).StatusCode);
        Assert.Equal(2, await LastReadInListAsync(owner, threadId));

        // No limite: o total da thread é marcável — 3 é lido, não é "acima".
        Assert.Equal(3, await MarkReadAsync(owner, threadId, 3));
        Assert.Equal(3, await LastReadInListAsync(owner, threadId));
    }

    [Fact] // AC-04
    public async Task The_last_read_seq_reaches_list_and_envelope_of_participants_and_of_nobody_else()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac36d");
        var (peer, peerHandle) = await PostRoutes.SignedInAsync(_host, "ac36e");
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "ac36f");
        var threadId = await CreateAsync(owner, "direct", peerHandle);
        await AckAsync(owner, threadId);
        await SendAsync(owner, threadId, "uma");
        await SendAsync(owner, threadId, "duas");
        await MarkReadAsync(owner, threadId, 2);

        // Participante: o marcador próprio aparece no GET /api/threads…
        Assert.Equal(2, await LastReadInListAsync(owner, threadId));
        // …e no envelope de mensagens, ao lado de items/nextSince.
        var envelope = await MessagesAsync(owner, threadId);
        Assert.Equal(2, envelope.GetProperty("lastReadSeq").GetInt64());
        Assert.Equal(2, envelope.GetProperty("nextSince").GetInt64());

        // É POR PARTICIPANTE: a par nunca marcou nada, então a lista E o envelope dela seguem 0.
        Assert.Equal(0, await LastReadInListAsync(peer, threadId));
        Assert.Equal(0, (await MessagesAsync(peer, threadId)).GetProperty("lastReadSeq").GetInt64());

        // Não-participante: a thread não existe para a lista dele…
        Assert.DoesNotContain(await ListAsync(stranger),
            item => item.GetProperty("id").GetString() == threadId);

        // …e toda rota da thread responde o MESMO 404 byte a byte, sem marcador e sem traceId —
        // o par estrangeira × inexistente vale para GET, read e ack juntos.
        var absentId = $"{PostRoutes.Origin(_host)}/t/{Guid.NewGuid():N}";
        var responses = new[]
        {
            await PostRoutes.SendAsync(stranger, HttpMethod.Get,
                $"{MessagesPath(threadId)}?since=0"),
            await PostRoutes.SendAsync(stranger, HttpMethod.Post,
                ReadPath(threadId), new { seq = 2 }, stranger.Csrf),
            await PostRoutes.SendAsync(stranger, HttpMethod.Post,
                ReadPath(absentId), new { seq = 2 }, stranger.Csrf),
            await PostRoutes.SendAsync(stranger, HttpMethod.Post,
                AckPath(absentId), null, stranger.Csrf),
        };
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var bodies = new List<byte[]>();
        foreach (var response in responses)
        {
            bodies.Add(await response.Content.ReadAsByteArrayAsync());
        }

        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal(bodies[0], bodies[2]);
        Assert.Equal(bodies[0], bodies[3]);
        var refusal = Encoding.UTF8.GetString(bodies[0]);
        Assert.Contains("not_found", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("lastReadSeq", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(threadId, refusal, StringComparison.Ordinal);

        // E os recusados não tocaram nada: o marcador do dono segue onde ele ficou.
        Assert.Equal(2, await LastReadInListAsync(owner, threadId));
    }

    [Fact] // AC-05
    public async Task A_suspended_actor_is_denied_and_recorded_while_an_active_participant_keeps_sending()
    {
        var (creator, creatorHandle) = await PostRoutes.SignedInAsync(_host, "ac36g");
        var (banned, bannedHandle) = await PostRoutes.SignedInAsync(_host, "ac36h");
        var (active, activeHandle) = await PostRoutes.SignedInAsync(_host, "ac36i");
        var threadId = await CreateAsync(creator, "group", bannedHandle, activeHandle);
        var origin = PostRoutes.Origin(_host);
        var creatorId = $"{origin}/users/{creatorHandle}";

        // O criador aceita o aviso e envia — a baseline de que a rota funciona para um ator vivo.
        await AckAsync(creator, threadId);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(creator, threadId, "antes da suspensão")).StatusCode);

        // SUSPENSÃO em quem JÁ aceitava o aviso: o envio é negado (403 policy_denied, §3.1) e a
        // recusa chega à trilha ANTES da resposta — o aceite não é passe livre.
        await Statuses.SuspendAsync(creatorHandle);
        var denied = await SendAsync(creator, threadId, "não envio mais");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied",
            await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // E vale para quem SUSPENSO nunca aceitou: a recusa de §3.1 vem ANTES do 428 do §9.6 —
        // um ator suspenso é negado como tal, não como "faltou o aviso".
        await Statuses.SuspendAsync(bannedHandle);
        var deniedUnacked = await SendAsync(banned, threadId, "nunca aceitei");
        Assert.Equal(HttpStatusCode.Forbidden, deniedUnacked.StatusCode);

        // Registrado (log estruturado da casa — a mesma trilha de PolicyAudit dos posts): uma
        // linha Deny por ator, com a palavra "suspenso" na própria razão.
        var rows = (await Audit.QueryAsync(new PolicyAuditFilter(Action: "Message")))
            .Where(row => row.Result == PolicyAudit.Deny).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Contains("suspenso", row.Reason, StringComparison.Ordinal));
        Assert.Contains(rows, row => row.ActorId == creatorId);
        Assert.Contains(rows, row => row.ActorId == $"{origin}/users/{bannedHandle}");

        // §3.1 não cala a leitura: o suspenso continua LENDO a thread…
        Assert.Equal(HttpStatusCode.OK,
            (await PostRoutes.SendAsync(creator, HttpMethod.Get,
                $"{MessagesPath(threadId)}?since=0")).StatusCode);

        // …e o participante ATIVO segue enviando normalmente (o gate é por ator, não por thread).
        await AckAsync(active, threadId);
        var sent = await SendAsync(active, threadId, "eu sigo aqui");
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        using (var json = JsonDocument.Parse(await sent.Content.ReadAsStringAsync()))
        {
            Assert.Equal(2, json.RootElement.GetProperty("seq").GetInt64());
        }
        Assert.Equal(new long[] { 1, 2 }, await SeqsAsync(active, threadId));
    }

    /// <summary>Creates a thread of <paramref name="kind"/> from this actor (the creator counts).</summary>
    private async Task<string> CreateAsync(AuthClient owner, string kind, params string[] handles)
    {
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/threads",
            new { kind, participantActorIds = handles }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> AckAsync(AuthClient client, string threadId) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, AckPath(threadId), null, client.Csrf);

    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client, string threadId, string content) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, MessagesPath(threadId),
            new { content }, client.Csrf);

    /// <summary>POST <c>.../read</c> and the <c>lastReadSeq</c> it says is now in force.</summary>
    private async Task<long> MarkReadAsync(AuthClient client, string threadId, long seq)
    {
        var response = await RawReadAsync(client, threadId, seq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("lastReadSeq").GetInt64();
    }

    private static Task<HttpResponseMessage> RawReadAsync(
        AuthClient client, string threadId, long seq) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, ReadPath(threadId), new { seq }, client.Csrf);

    /// <summary>The parsed <c>GET .../messages?since=0</c> envelope (items + nextSince + lastReadSeq).</summary>
    private async Task<JsonElement> MessagesAsync(AuthClient client, string threadId)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get,
            $"{MessagesPath(threadId)}?since=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static JsonElement[] ItemsOf(JsonElement envelope) =>
        envelope.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();

    private async Task<JsonElement[]> ItemsAsync(AuthClient client, string threadId) =>
        ItemsOf(await MessagesAsync(client, threadId));

    private async Task<long[]> SeqsAsync(AuthClient client, string threadId) =>
        (await ItemsAsync(client, threadId)).Select(item => item.GetProperty("seq").GetInt64()).ToArray();

    /// <summary>The reader's own <c>lastReadSeq</c> as <c>GET /api/threads</c> exposes it (AC-04).</summary>
    private async Task<long> LastReadInListAsync(AuthClient client, string threadId)
    {
        var items = await ListAsync(client);
        var item = items.Single(value => value.GetProperty("id").GetString() == threadId);
        return item.GetProperty("lastReadSeq").GetInt64();
    }

    private async Task<List<JsonElement>> ListAsync(AuthClient client)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get, "/api/threads");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.Clone()).ToList();
    }

    /// <summary>How many ack rows one (thread, actor) pair has — idempotence, counted at the source.</summary>
    private async Task<long> CountAcksAsync(string threadId, string actorId)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_host.StoragePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM thread_no_e2ee_ack
            WHERE thread_id = @ThreadId AND actor_id = @ActorId
            """;
        command.Parameters.AddWithValue("@ThreadId", threadId);
        command.Parameters.AddWithValue("@ActorId", actorId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";

    private static string AckPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/ack-no-e2ee";

    private static string ReadPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/read";
}
