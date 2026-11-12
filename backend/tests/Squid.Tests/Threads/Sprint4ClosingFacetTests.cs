using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Media;
using Squid.Tests.Posts;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-01..AC-06 of TASK-0040 — the Sprint 4 DoD closing facet, entirely over the routes
/// TASK-0033..0039 delivered: two users create a direct, accept the FR-032 notice and exchange
/// messages both read back in seq order (AC-01); a third user shares ONE byte-identical 404 on
/// GET and POST and never receives the participant list (AC-02, FR-031/SC-005); sends racing
/// from two sessions take unique seqs and read back complete and ordered (AC-03, GAP-29);
/// repeating a reply or a reaction changes nothing while an outsider sees neither behind the
/// same 404 (AC-04, FR-057/FR-058); the two QA-0036 regressions become PERMANENT tests — a
/// seat spelled <c>read</c> is still that participant's removal (AC-05a, the
/// <c>ThreadRouteOf</c> marker order) and the ack row carries the FR-032 notice TEXT itself
/// (AC-05b, section D); and the whole 0033..0039 flow — create, ack, send, reply, react,
/// attach — reads back with every reference, count and attachment intact (AC-06). Against the
/// host <see cref="SquidServiceHost"/> builds, like every suite of this folder.
/// </summary>
[Collection("ServiceHost")]
public sealed class Sprint4ClosingFacetTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public Sprint4ClosingFacetTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Two_users_create_a_direct_accept_the_notice_and_exchange_messages_read_in_seq_order()
    {
        var (alice, aliceHandle) = await PostRoutes.SignedInAsync(_host, "fac40a");
        var (bob, bobHandle) = await PostRoutes.SignedInAsync(_host, "fac40b");
        var origin = PostRoutes.Origin(_host);
        var aliceId = $"{origin}/users/{aliceHandle}";
        var bobId = $"{origin}/users/{bobHandle}";

        // Criação (0033): direct com exatamente os dois, a criadora entre eles, contadores 0.
        var created = await PostRoutes.SendAsync(alice, HttpMethod.Post, "/api/threads",
            new { kind = "direct", participantActorIds = new[] { bobHandle } }, alice.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
        {
            var root = json.RootElement;
            var threadId = root.GetProperty("id").GetString()!;
            Assert.Equal("direct", root.GetProperty("kind").GetString());
            Assert.Equal(
                new[] { aliceId, bobId },
                root.GetProperty("participants").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(0, root.GetProperty("messageSeq").GetInt64());
            Assert.Equal(0, root.GetProperty("lastReadSeq").GetInt64());

            // O aviso é obrigatório de verdade (0036): antes do aceite o envio é 428 e a
            // recusa não grava nada — a história da thread continua vazia.
            var gated = await SendAsync(alice, threadId, new { content = "antes do aviso" });
            Assert.Equal(HttpStatusCode.PreconditionRequired, gated.StatusCode);
            Assert.Empty(ItemsOf(await MessagesAsync(alice, threadId, since: 0)));

            // Os DOIS aceitam o aviso (FR-032, registro por ator) e então trocam mensagens.
            Assert.Equal(HttpStatusCode.OK, (await AckAsync(alice, threadId)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await AckAsync(bob, threadId)).StatusCode);
            Assert.Equal(1, await SendSeqAsync(alice, threadId, new { content = "oi, bob" }));
            Assert.Equal(2, await SendSeqAsync(bob, threadId, new { content = "oi, alice" }));
            Assert.Equal(3, await SendSeqAsync(alice, threadId, new { content = "entao combinado" }));

            // Os DOIS leem a MESMA história na ordem de seq, com os mesmos ids e textos.
            var byAlice = await MessagesAsync(alice, threadId, since: 0);
            var byBob = await MessagesAsync(bob, threadId, since: 0);
            Assert.Equal(new long[] { 1, 2, 3 }, SeqsOf(byAlice));
            Assert.Equal(new long[] { 1, 2, 3 }, SeqsOf(byBob));
            Assert.Equal(3, byAlice.GetProperty("nextSince").GetInt64());
            Assert.Equal(
                new[] { "oi, bob", "oi, alice", "entao combinado" },
                ItemsOf(byAlice).Select(item => item.GetProperty("content").GetString()));
            Assert.Equal(
                ItemsOf(byAlice).Select(item => item.GetProperty("id").GetString()),
                ItemsOf(byBob).Select(item => item.GetProperty("id").GetString()));

            // Marcador por participante (FR-033): cada um avança o PRÓPRIO e a lista expõe o seu.
            Assert.Equal(3, await MarkReadAsync(alice, threadId, 3));
            Assert.Equal(2, await MarkReadAsync(bob, threadId, 2));
            Assert.Equal(3, await LastReadInListAsync(alice, threadId));
            Assert.Equal(2, await LastReadInListAsync(bob, threadId));
        }
    }

    [Fact] // AC-02
    public async Task A_third_user_shares_one_404_body_on_get_and_post_and_never_reads_the_participants()
    {
        var (alice, aliceHandle) = await PostRoutes.SignedInAsync(_host, "fac40c");
        var (bob, bobHandle) = await PostRoutes.SignedInAsync(_host, "fac40d");
        var (carol, _) = await PostRoutes.SignedInAsync(_host, "fac40e");
        var origin = PostRoutes.Origin(_host);

        var threadId = await CreateDirectAsync(alice, bobHandle);
        Assert.Equal(1, await SendSeqAsync(alice, threadId, new { content = "só entre nós" }));
        Assert.Single(ItemsOf(await MessagesAsync(alice, threadId, since: 0))); // a thread existe e responde

        // C não está em nenhuma das duas cadeiras: GET e POST da thread de A/B respondem o
        // MESMO corpo da thread que nunca existiu — byte a byte, sem traceId que diferisse.
        var absentId = $"{origin}/t/{Guid.NewGuid():N}";
        var responses = new[]
        {
            await PostRoutes.SendAsync(carol, HttpMethod.Get, $"{MessagesPath(threadId)}?since=0"),
            await PostRoutes.SendAsync(carol, HttpMethod.Post, MessagesPath(threadId),
                new { content = "posso ler?" }, carol.Csrf),
            await PostRoutes.SendAsync(carol, HttpMethod.Get, $"{MessagesPath(absentId)}?since=0"),
            await PostRoutes.SendAsync(carol, HttpMethod.Post, MessagesPath(absentId),
                new { content = "posso ler?" }, carol.Csrf),
        };
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var bodies = new List<byte[]>();
        foreach (var response in responses)
        {
            bodies.Add(await response.Content.ReadAsByteArrayAsync());
        }

        Assert.All(bodies, body => Assert.Equal(bodies[0], body)); // um corpo só, quatro casos

        // A recusa não ensina NADA: nem a lista de participantes, nem os atores, nem o id.
        var text = Encoding.UTF8.GetString(bodies[0]);
        Assert.Contains("not_found", text, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("participants", text, StringComparison.Ordinal);
        Assert.DoesNotContain(aliceHandle, text, StringComparison.Ordinal);
        Assert.DoesNotContain(bobHandle, text, StringComparison.Ordinal);
        Assert.DoesNotContain(threadId, text, StringComparison.Ordinal);

        // E a lista de C não carrega a thread de A/B — nenhuma rota revelou que ela existe.
        Assert.DoesNotContain(await ListAsync(carol),
            item => item.GetProperty("id").GetString() == threadId);

        // Dentro da parede nada mudou pelas recusas: a dona segue lendo a mensagem.
        Assert.Single(ItemsOf(await MessagesAsync(alice, threadId, since: 0)));
    }

    [Fact] // AC-03
    public async Task Concurrent_sends_take_unique_seqs_and_the_history_reads_back_complete_ordered_and_without_duplicates()
    {
        var (alice, _) = await PostRoutes.SignedInAsync(_host, "fac40f");
        var (bob, bobHandle) = await PostRoutes.SignedInAsync(_host, "fac40g");
        var threadId = await CreateDirectAsync(alice, bobHandle);
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(bob, threadId)).StatusCode);

        // Três rodadas da MESMA corrida: os DOIS participantes disparam no mesmo instante, cada
        // um da própria sessão — concorrência de verdade, não uma sequência disfarçada.
        var seqs = new List<long>();
        for (var round = 0; round < 3; round++)
        {
            var racing = await Task.WhenAll(
                PostRoutes.SendAsync(alice, HttpMethod.Post, MessagesPath(threadId),
                    new { content = $"do A r{round}" }, alice.Csrf),
                PostRoutes.SendAsync(bob, HttpMethod.Post, MessagesPath(threadId),
                    new { content = $"do B r{round}" }, bob.Csrf));
            Assert.All(racing, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

            foreach (var response in racing)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                seqs.Add(json.RootElement.GetProperty("seq").GetInt64());
            }
        }

        // Seis envios, seis números DIFERENTES, exatamente 1..6: o ETag serializou cada corrida
        // e ninguém queimou número no caminho (GAP-29).
        Assert.Equal(6, seqs.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 6).Select(index => (long)index), seqs.OrderBy(value => value));

        // A leitura devolve TODOS, uma vez cada, em ordem crescente — sem duplicata e sem buraco.
        var history = await MessagesAsync(alice, threadId, since: 0);
        Assert.Equal(Enumerable.Range(1, 6).Select(index => (long)index), SeqsOf(history));
        Assert.Equal(6, history.GetProperty("nextSince").GetInt64());

        // Os dois lados leem a MESMA ordem total, e o delta do meio não repete nada já lido (§9.3).
        Assert.Equal(SeqsOf(history), SeqsOf(await MessagesAsync(bob, threadId, since: 0)));
        Assert.Equal(new long[] { 5, 6 }, SeqsOf(await MessagesAsync(alice, threadId, since: 4)));
        Assert.Empty(ItemsOf(await MessagesAsync(alice, threadId, since: 6)));
    }

    [Fact] // AC-04
    public async Task Repeating_a_reply_or_a_reaction_changes_nothing_and_an_outsider_sees_neither()
    {
        var (alice, aliceHandle) = await PostRoutes.SignedInAsync(_host, "fac40k");
        var (bob, bobHandle) = await PostRoutes.SignedInAsync(_host, "fac40l");
        var (carol, _) = await PostRoutes.SignedInAsync(_host, "fac40m");
        var origin = PostRoutes.Origin(_host);

        var threadId = await CreateDirectAsync(alice, bobHandle);
        var originalId = await SendForIdAsync(alice, threadId, new { content = "a pergunta" });

        // Repetir a RESPOSTA não reescreve nada que já existe: as duas passam com a MESMA
        // referência…
        var first = await SendAsync(alice, threadId, new { content = "a resposta", replyToId = originalId });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            Assert.Equal(originalId, firstJson.RootElement.GetProperty("replyToId").GetString());
        }

        var repeat = await SendAsync(alice, threadId, new { content = "a resposta", replyToId = originalId });
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

        // …a original segue SEM resposta (nada a reescreveu por baixo) e as duas respostas
        // apontam para ela — o estado pré-existente ficou intacto.
        var items = ItemsOf(await MessagesAsync(alice, threadId, since: 0));
        Assert.Equal(new long[] { 1, 2, 3 },
            items.Select(item => item.GetProperty("seq").GetInt64()).ToArray());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("replyToId").ValueKind);
        Assert.Equal(originalId, items[1].GetProperty("replyToId").GetString());
        Assert.Equal(originalId, items[2].GetProperty("replyToId").GetString());

        // Repetir a REAÇÃO não muda a contagem: uma linha por (mensagem, ator, emoji) e a
        // contagem é a DERIVAÇÃO das linhas que existem, nunca um contador guardado.
        var reacted = await ReactAsync(alice, threadId, originalId, "👍");
        Assert.Equal(HttpStatusCode.OK, reacted.StatusCode);
        Assert.Equal(new[] { (Emoji: "👍", Count: 1L, Mine: true) }, await ReactionsAsync(reacted));

        var again = await ReactAsync(alice, threadId, originalId, "👍");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(new[] { (Emoji: "👍", Count: 1L, Mine: true) }, await ReactionsAsync(again));

        // O par reagindo ao MESMO emoji é a segunda linha, e repetir do primeiro depois dela
        // continua sem mexer em nada.
        Assert.Equal(HttpStatusCode.OK, (await ReactAsync(bob, threadId, originalId, "👍")).StatusCode);
        var pair = await ReactAsync(alice, threadId, originalId, "👍");
        Assert.Equal(new[] { (Emoji: "👍", Count: 2L, Mine: true) }, await ReactionsAsync(pair));
        Assert.Equal(new[] { (Emoji: "👍", Count: 2L, Mine: true) },
            await ReactionsAsync(alice, threadId, originalId));
        Assert.Equal(new[] { (Emoji: "👍", Count: 2L, Mine: true) },
            await ReactionsAsync(bob, threadId, originalId));

        // O terceiro não vê resposta NEM reação: um corpo só, byte a byte, na leitura, no POST
        // de resposta (com alvo VÁLIDO de fora) e nas DUAS rotas de reação — e o MESMO corpo da
        // thread que nunca existiu (SC-005: a referência não abre porta).
        var absentId = $"{origin}/t/{Guid.NewGuid():N}";
        var responses = new[]
        {
            await PostRoutes.SendAsync(carol, HttpMethod.Get, $"{MessagesPath(threadId)}?since=0"),
            await PostRoutes.SendAsync(carol, HttpMethod.Post, MessagesPath(threadId),
                new { content = "eu também", replyToId = originalId }, carol.Csrf),
            await PostRoutes.SendAsync(carol, HttpMethod.Post, ReactionsPath(threadId, originalId),
                new { emoji = "👍" }, carol.Csrf),
            await PostRoutes.SendAsync(carol, HttpMethod.Delete,
                $"{ReactionsPath(threadId, originalId)}?emoji={Uri.EscapeDataString("👍")}",
                null, carol.Csrf),
            await PostRoutes.SendAsync(carol, HttpMethod.Get, $"{MessagesPath(absentId)}?since=0"),
            await PostRoutes.SendAsync(carol, HttpMethod.Post, ReactionsPath(absentId, originalId),
                new { emoji = "👍" }, carol.Csrf),
        };
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var bodies = new List<byte[]>();
        foreach (var response in responses)
        {
            bodies.Add(await response.Content.ReadAsByteArrayAsync());
        }

        Assert.All(bodies, body => Assert.Equal(bodies[0], body)); // um corpo só, seis casos
        var refusal = Encoding.UTF8.GetString(bodies[0]);
        Assert.Contains("not_found", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("replyToId", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("reactions", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("count", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(threadId, refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(originalId, refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(aliceHandle, refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(bobHandle, refusal, StringComparison.Ordinal);

        // E as recusas não tocaram nada dentro da parede: as contagens e as referências seguem
        // exatamente onde estavam.
        Assert.Equal(new[] { (Emoji: "👍", Count: 2L, Mine: true) },
            await ReactionsAsync(alice, threadId, originalId));
        var still = ItemsOf(await MessagesAsync(alice, threadId, since: 0));
        Assert.Equal(new long[] { 1, 2, 3 },
            still.Select(item => item.GetProperty("seq").GetInt64()).ToArray());
        Assert.Equal(originalId, still[1].GetProperty("replyToId").GetString());
        Assert.Equal(originalId, still[2].GetProperty("replyToId").GetString());
    }

    [Fact] // AC-05 (a)
    public async Task A_seat_spelled_read_is_still_a_removal_and_the_section_d_tails_keep_working()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "fac40h");
        var origin = PostRoutes.Origin(_host);

        // O sufixo de rota escrito como handle: a regra [a-z0-9_]{3,30} permite — um handle
        // GERADO nunca termina em `read`, por isso nenhum teste do DEV alcança esta forma.
        var reader = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created,
            (await reader.RegisterAsync("read", PostRoutes.Password)).StatusCode);
        var (_, extraHandle) = await PostRoutes.SignedInAsync(_host, "fac40i");

        // Grupo de 3 (criador + `read` + extra): remover `read` jamais pode ser 409
        // last_participant, então a ÚNICA coisa que responde o DELETE é o parser de rota.
        var threadId = await CreateAsync(owner, "group", "read", extraHandle);
        var readId = $"{origin}/users/read";
        Assert.Contains(readId, await ParticipantsAsync(owner, threadId));

        // A participante LÊ a thread com esse handle…
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(owner, threadId)).StatusCode);
        Assert.Equal(1, await SendSeqAsync(owner, threadId, new { content = "antes da remoção" }));
        Assert.Equal(HttpStatusCode.OK, (await PostRoutes.SendAsync(reader, HttpMethod.Get,
            $"{MessagesPath(threadId)}?since=0")).StatusCode);

        // …e o DELETE continua sendo a REMOÇÃO dessa cadeira — o marcador /participants/ vence
        // o sufixo /read: 200, a cadeira some da lista e o acesso cai na hora (FR-031).
        var seatPath =
            $"/api/threads/{Uri.EscapeDataString(threadId)}/participants/{Uri.EscapeDataString(readId)}";
        var removed = await PostRoutes.SendAsync(owner, HttpMethod.Delete, seatPath, null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.DoesNotContain(readId, await ParticipantsAsync(owner, threadId));
        Assert.Equal(HttpStatusCode.NotFound, (await PostRoutes.SendAsync(reader, HttpMethod.Get,
            $"{MessagesPath(threadId)}?since=0")).StatusCode);

        // A rota /read segue respondendo no PRÓPRIO caminho (nada engoliu o sufixo): o marcador
        // avança até o total da thread.
        var marked = await PostRoutes.SendAsync(owner, HttpMethod.Post, ReadPath(threadId),
            new { seq = 1 }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        using (var json = JsonDocument.Parse(await marked.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, json.RootElement.GetProperty("lastReadSeq").GetInt64());
        }

        // E envio e lista seguem intactos depois da remoção — a colisão não quebrou nada disso.
        Assert.Equal(2, await SendSeqAsync(owner, threadId, new { content = "depois da remoção" }));
        Assert.Contains(threadId, (await ListAsync(owner))
            .Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(new long[] { 1, 2 }, SeqsOf(await MessagesAsync(owner, threadId, since: 0)));
    }

    [Fact] // AC-05 (b)
    public async Task The_ack_row_carries_the_fr_032_notice_text_itself()
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, "fac40j");
        var origin = PostRoutes.Origin(_host);
        var threadId = await CreateAsync(owner, "direct", "fac40jpar");
        var actorId = $"{origin}/users/{ownerHandle}";

        var ack = await AckAsync(owner, threadId);
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

        // Lido na FONTE (a própria tabela do registro, como a QA-0036 leu): a linha do aceite
        // carrega o TEXTO do aviso — o que FR-032 manda registrar, palavra por palavra.
        var notice = await NoticeAsync(threadId, actorId);
        Assert.NotEmpty(notice);
        Assert.Contains("criptografia ponta a ponta", notice, StringComparison.Ordinal);
        Assert.Contains("legíveis pelo operador", notice, StringComparison.Ordinal);

        // Repetir o aceite mantém UMA linha com o MESMO texto (idempotente por ator+thread).
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(owner, threadId)).StatusCode);
        Assert.Equal(1, await AckCountAsync(threadId, actorId));
        Assert.Equal(notice, await NoticeAsync(threadId, actorId));
    }

    [Fact] // AC-06
    public async Task The_full_flow_from_creation_to_attachment_reads_back_every_reference_count_and_attachment()
    {
        var (ana, _) = await PostRoutes.SignedInAsync(_host, "fac40n");
        var (bia, biaHandle) = await PostRoutes.SignedInAsync(_host, "fac40o");
        var origin = PostRoutes.Origin(_host);

        // 0033 criação + 0036 aceite dos DOIS lados.
        var threadId = await CreateDirectAsync(ana, biaHandle);
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(bia, threadId)).StatusCode);

        // 0039: upload real pela rota /api/media (geometria própria — o registro é por hash).
        var upload = await MediaHarness.UploadAsync(ana, MediaHarness.JpegWithExif(9, 5), "foto da conversa");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using (var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync()))
        {
            var hash = uploaded.RootElement.GetProperty("contentHash").GetString()!;

            // 0034 envio, 0037 resposta, 0038 reação e 0039 anexo — tudo na MESMA thread.
            var m1 = await SendForIdAsync(ana, threadId, new { content = "a pergunta" });
            var m2 = await SendForIdAsync(bia, threadId, new { content = "a resposta", replyToId = m1 });
            Assert.Equal(HttpStatusCode.OK, (await ReactAsync(ana, threadId, m1, "👍")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ReactAsync(bia, threadId, m1, "👍")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ReactAsync(ana, threadId, m2, "🎉")).StatusCode);
            var m3 = await SendForIdAsync(ana, threadId,
                new { content = "com anexo", contentHashes = new[] { hash } });

            // Leitura de ponta a ponta, dos DOIS lados: sequência, ids, referência e cursor.
            foreach (var reader in new[] { ana, bia })
            {
                var envelope = await MessagesAsync(reader, threadId, since: 0);
                var items = ItemsOf(envelope);
                Assert.Equal(new long[] { 1, 2, 3 },
                    items.Select(item => item.GetProperty("seq").GetInt64()).ToArray());
                Assert.Equal(3, envelope.GetProperty("nextSince").GetInt64());
                Assert.Equal(m1, items[0].GetProperty("id").GetString());
                Assert.Equal(m2, items[1].GetProperty("id").GetString());
                Assert.Equal(m3, items[2].GetProperty("id").GetString());
                Assert.Equal(JsonValueKind.Null, items[0].GetProperty("replyToId").ValueKind);
                Assert.Equal(m1, items[1].GetProperty("replyToId").GetString());
                Assert.Equal(JsonValueKind.Null, items[2].GetProperty("replyToId").ValueKind);

                // As contagens derivadas: 👍 2 na pergunta (os dois), 🎉 1 na resposta.
                Assert.Equal(new[] { (Emoji: "👍", Count: 2L, Mine: true) },
                    ProjectionOf(items[0]));
                Assert.Equal(
                    new[] { (Emoji: "🎉", Count: 1L, Mine: reader == ana) },
                    ProjectionOf(items[1]));

                // O anexo no formato de §2.6, com os dados do registro de upload.
                Assert.Empty(items[0].GetProperty("attachments").EnumerateArray());
                Assert.Empty(items[1].GetProperty("attachments").EnumerateArray());
                var attachment = items[2].GetProperty("attachments").EnumerateArray().Single();
                Assert.Equal("image", attachment.GetProperty("kind").GetString());
                Assert.Equal(hash, attachment.GetProperty("contentHash").GetString());
                Assert.Equal("image/jpeg", attachment.GetProperty("mimeType").GetString());
                Assert.Equal($"{origin}/api/media/{hash}", attachment.GetProperty("url").GetString());
                Assert.Equal("foto da conversa", attachment.GetProperty("alt").GetString());
                Assert.True(attachment.GetProperty("width").GetInt32() > 0, "width vem do upload");
                Assert.True(attachment.GetProperty("height").GetInt32() > 0, "height vem do upload");
                Assert.True(attachment.GetProperty("sizeBytes").GetInt64() > 0, "sizeBytes vem do upload");
                Assert.True(attachment.TryGetProperty("blurHash", out _), "blurHash é campo de §2.6");
            }

            // A mídia referenciada serve os DOIS participantes (GAP-25 ampliada pela referência).
            Assert.Equal(HttpStatusCode.OK,
                (await ana.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await bia.SendAsync(HttpMethod.Get, $"/api/media/{hash}")).StatusCode);
        }

        // Marcadores por participante expostos na lista (FR-033): só quem marcou avança.
        Assert.Equal(3, await MarkReadAsync(ana, threadId, 3));
        Assert.Equal(3, await LastReadInListAsync(ana, threadId));
        Assert.Equal(0, await LastReadInListAsync(bia, threadId));
    }

    /// <summary>Creates a thread of <paramref name="kind"/> with these handle spellings (the creator counts).</summary>
    private async Task<string> CreateAsync(AuthClient owner, string kind, params string[] handles)
    {
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/threads",
            new { kind, participantActorIds = handles }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>
    /// A direct thread with this actor and <paramref name="participantHandle"/>, WITH the
    /// creator's FR-032 acceptance recorded — §9.6 refuses the first send without it, so every
    /// flow of this facet starts from an accepted notice.
    /// </summary>
    private async Task<string> CreateDirectAsync(AuthClient owner, string participantHandle)
    {
        var threadId = await CreateAsync(owner, "direct", participantHandle);
        Assert.Equal(HttpStatusCode.OK, (await AckAsync(owner, threadId)).StatusCode);
        return threadId;
    }

    /// <summary>The acceptance of <c>POST .../ack-no-e2ee</c> (idempotent, required before a send).</summary>
    private static Task<HttpResponseMessage> AckAsync(AuthClient client, string threadId) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, AckPath(threadId), null, client.Csrf);

    /// <summary>POSTs one message through the route without judging the answer (the 428 case).</summary>
    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client, string threadId, object body) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, MessagesPath(threadId), body, client.Csrf);

    /// <summary>POSTs one message and returns the seq the 200 answers with.</summary>
    private static async Task<long> SendSeqAsync(AuthClient client, string threadId, object body)
    {
        var response = await SendAsync(client, threadId, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("seq").GetInt64();
    }

    /// <summary>POSTs one message and returns the id §6 answers with (replyToId/contentHashes ride the body).</summary>
    private static async Task<string> SendForIdAsync(AuthClient client, string threadId, object body)
    {
        var response = await SendAsync(client, threadId, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>The parsed <c>GET .../messages?since=</c> body (items + nextSince + lastReadSeq).</summary>
    private async Task<JsonElement> MessagesAsync(AuthClient client, string threadId, long since)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get,
            $"{MessagesPath(threadId)}?since={since}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    /// <summary>POST <c>.../read</c> and the <c>lastReadSeq</c> it says is now in force.</summary>
    private async Task<long> MarkReadAsync(AuthClient client, string threadId, long seq)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Post,
            ReadPath(threadId), new { seq }, client.Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("lastReadSeq").GetInt64();
    }

    /// <summary><c>POST .../reactions</c> with <c>{emoji}</c> as contract §6 spells it.</summary>
    private static Task<HttpResponseMessage> ReactAsync(
        AuthClient client, string threadId, string messageId, string emoji) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, ReactionsPath(threadId, messageId),
            new { emoji }, client.Csrf);

    /// <summary>The <c>{reactions: [...]}</c> projection of a reaction-route answer.</summary>
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
        var envelope = await MessagesAsync(client, threadId, since: 0);
        var message = ItemsOf(envelope).Single(item => item.GetProperty("id").GetString() == messageId);
        return ProjectionOf(message);
    }

    /// <summary>The <c>{emoji, count, mine}</c> items of one message, in the order the server sent them.</summary>
    private static (string Emoji, long Count, bool Mine)[] ProjectionOf(JsonElement message) =>
        message.GetProperty("reactions").EnumerateArray().Select(entry => (
            Emoji: entry.GetProperty("emoji").GetString()!,
            Count: entry.GetProperty("count").GetInt64(),
            Mine: entry.GetProperty("mine").GetBoolean())).ToArray();

    /// <summary>Every item of <c>GET /api/threads</c>, parsed once.</summary>
    private async Task<List<JsonElement>> ListAsync(AuthClient client)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get, "/api/threads");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.Clone()).ToList();
    }

    /// <summary>The participants the list answers with for this thread — data-model §2.7's list.</summary>
    private async Task<List<string>> ParticipantsAsync(AuthClient client, string threadId)
    {
        var items = await ListAsync(client);
        var item = items.Single(value => value.GetProperty("id").GetString() == threadId);
        return item.GetProperty("participants").EnumerateArray()
            .Select(value => value.GetString()!).ToList();
    }

    /// <summary>The reader's own <c>lastReadSeq</c> as <c>GET /api/threads</c> exposes it (FR-033).</summary>
    private async Task<long> LastReadInListAsync(AuthClient client, string threadId)
    {
        var items = await ListAsync(client);
        var item = items.Single(value => value.GetProperty("id").GetString() == threadId);
        return item.GetProperty("lastReadSeq").GetInt64();
    }

    /// <summary>The notice text one (thread, actor) ack row stored — read at the source (migration 0004).</summary>
    private async Task<string> NoticeAsync(string threadId, string actorId)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={_host.StoragePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT notice FROM thread_no_e2ee_ack
            WHERE thread_id = @ThreadId AND actor_id = @ActorId
            """;
        command.Parameters.AddWithValue("@ThreadId", threadId);
        command.Parameters.AddWithValue("@ActorId", actorId);
        return (string?)await command.ExecuteScalarAsync() ?? string.Empty;
    }

    /// <summary>How many ack rows one (thread, actor) pair has — idempotence, counted at the source.</summary>
    private async Task<long> AckCountAsync(string threadId, string actorId)
    {
        await using var connection = new SqliteConnection(
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

    private static JsonElement[] ItemsOf(JsonElement envelope) =>
        envelope.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();

    private static long[] SeqsOf(JsonElement envelope) =>
        ItemsOf(envelope).Select(item => item.GetProperty("seq").GetInt64()).ToArray();

    /// <summary>The route as the contract spells it: a thread id is a URI, so one escaped segment.</summary>
    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";

    private static string ReactionsPath(string threadId, string messageId) =>
        $"{MessagesPath(threadId)}/{Uri.EscapeDataString(messageId)}/reactions";

    private static string AckPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/ack-no-e2ee";

    private static string ReadPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/read";
}
