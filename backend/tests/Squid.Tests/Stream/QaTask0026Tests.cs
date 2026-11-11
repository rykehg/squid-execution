using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Posts;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Xunit.Abstractions;

namespace Squid.Tests.Stream;

/// <summary>
/// QA-0025 (TASK-0026) — the QA's OWN scenarios over the six acceptance criteria, executed in
/// this session rather than re-read from the DEV suites. The three spots where a green suite can
/// still hide a defect get the real execution: the &lt; 1 s of the Sprint 3 DoD measured on three
/// publishes (AC-01), a reconnect whose subscriber missed SIX events — five it sees with one
/// forbidden post sitting in between — that must deliver exactly those five, once each (AC-02),
/// and the delivery-time gate proven on REPLAY (AC-04): two posts that were VISIBLE when written
/// (policy flipped after publish, member banned after publish) and one forbidden from the start,
/// all published while the subscriber was away, must stay off the wire on reconnect while the
/// control post still arrives. AC-03 watches the injected heartbeat on the wire, AC-05 pairs the
/// 401 with a publisher that must stay fast under a stalled and a departed subscriber, and AC-06
/// is the gate run itself. Expected values are derived from the artifact (the global
/// <c>Activity.Seq</c> of each post, read from <see cref="IActivityStore"/>), never remembered.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0026Tests : IClassFixture<StreamHostFixture>
{
    private readonly StreamHostFixture _host;

    private readonly ITestOutputHelper _output;

    public QaTask0026Tests(StreamHostFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    private IActivityStore Activities =>
        _host.App.Services.GetRequiredService<IActivityStore>();

    // ---------------------------------------------------------------- AC-01

    [Fact] // AC-01 — < 1 s no fio, id = Activity.Seq global, data = o PostView bruto (R-13)
    public async Task Ac01_qa_publish_reaches_the_subscriber_in_under_a_second_with_global_seq_and_raw_payload()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "qa01dono");
        var (reader, _) = await StreamHarness.SignedInAsync(_host, "qa01leitor");
        var slug = await StreamHarness.NewContainerAsync(owner, "qa01");

        using var stream = await SseConnection.OpenAsync(_host, reader);

        for (var round = 1; round <= 3; round++)
        {
            var content = StreamHarness.Unique($"ao-vivo-{round}");
            var watch = Stopwatch.StartNew();
            var (postId, answer) = await PublishRawAsync(owner, slug, content);
            var seen = await stream.ReadUntilAsync(Matches(content), TimeSpan.FromSeconds(5));
            watch.Stop();

            var frame = Assert.Single(seen, Matches(content));
            _output.WriteLine(
                $"AC-01 observacao: round {round} publicado+entregue em {watch.Elapsed.TotalMilliseconds:F0} ms id={frame.Id}");

            // O primeiro ponto do DoD do Sprint 3: medido no teste, no fio, publicação -> quadro.
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1),
                $"o DoD do Sprint 3 pede menos de 1 s, o round {round} levou {watch.Elapsed.TotalMilliseconds:F0} ms");

            // event: post e id = o Activity.Seq GLOBAL (GAP-23), nunca o Object.Seq da resposta.
            Assert.Equal("post", frame.Event);
            var seq = await StreamHarness.GlobalSeqAsync(_host, postId);
            Assert.Equal(seq.ToString(CultureInfo.InvariantCulture), frame.Id);

            // data = o resumo do contrato §5, e o payload BRUTO do log (R-13, sem re-serializar):
            // byte a byte com o que IActivityStore guarda, e o mesmo PostView da resposta 201.
            Assert.False(string.IsNullOrEmpty(frame.Data));
            using var summary = JsonDocument.Parse(frame.Data!);
            Assert.Equal(content, summary.RootElement.GetProperty("content").GetString());
            Assert.Equal(postId, summary.RootElement.GetProperty("id").GetString());
            Assert.False(string.IsNullOrEmpty(summary.RootElement.GetProperty("author").GetString()));
            Assert.Equal(
                $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}/c/{slug}",
                summary.RootElement.GetProperty("containerId").GetString());

            var activity = (await Activities.ReadAsync()).Single(item => item.ObjectId == postId);
            Assert.Equal(activity.Payload, frame.Data); // sem re-serializar: o quadro é o log

            using var answered = JsonDocument.Parse(answer);
            Assert.True(
                answered.RootElement.GetRawText().Equals(summary.RootElement.GetRawText(), StringComparison.Ordinal),
                "o data do quadro deve ser o MESMO PostView que a resposta 201 devolveu");
        }
    }

    // ---------------------------------------------------------------- AC-02

    [Fact] // AC-02 — reconexão depois de SEIS eventos perdidos: só o delta visível, cada um uma vez
    public async Task Ac02_qa_reconnect_after_six_missed_events_delivers_exactly_the_visible_delta()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "qa02dono");
        var (reader, _) = await StreamHarness.SignedInAsync(_host, "qa02leitor");
        var aberto = await StreamHarness.NewContainerAsync(owner, "qa02publico");
        var restrito = await StreamHarness.NewContainerAsync(owner, "qa02restrito");
        await StreamHarness.SetVisibilityAsync(owner, restrito, "membersonly");

        long cursor;
        var preContent = StreamHarness.Unique("antes");
        using (var first = await SseConnection.OpenAsync(_host, reader))
        {
            var preId = await StreamHarness.PublishAsync(owner, aberto, preContent);
            cursor = await StreamHarness.GlobalSeqAsync(_host, preId);
            var seen = await first.ReadUntilAsync(Matches(preContent), TimeSpan.FromSeconds(5));
            var preFrame = Assert.Single(seen, Matches(preContent));
            Assert.Equal(cursor.ToString(CultureInfo.InvariantCulture), preFrame.Id);
        } // a subscriber away: six events will land before it comes back

        var missing = new List<(string Content, long Seq)>();
        for (var i = 1; i <= 2; i++)
        {
            missing.Add(await PublishRecordedAsync(owner, aberto, StreamHarness.Unique($"falta{i}")));
        }

        // the forbidden post in the MIDDLE of the gap: it must neither arrive nor shift the delta.
        var (hiddenId, hiddenContent) = await PublishNamedAsync(owner, restrito, StreamHarness.Unique("faltainvisivel"));
        var hiddenSeq = await StreamHarness.GlobalSeqAsync(_host, hiddenId);
        Assert.True(hiddenSeq > cursor, "o post invisível deve cair no meio da janela perdida");

        for (var i = 3; i <= 5; i++)
        {
            missing.Add(await PublishRecordedAsync(owner, aberto, StreamHarness.Unique($"falta{i}")));
        }

        using var back = await SseConnection.OpenAsync(
            _host, reader, lastEventId: cursor.ToString(CultureInfo.InvariantCulture));
        var delta = await back.ReadUntilAsync(Matches(missing[^1].Content), TimeSpan.FromSeconds(5));

        // Exactly the visible delta: five posts, in seq order, once each, without the cursor's
        // post and without the forbidden one that sat between them (contract §9.4).
        var ids = PostIds(delta);
        _output.WriteLine(
            $"AC-02 observacao: cursor={cursor} perdidos=6 visiveis={missing.Count} invisivel={hiddenSeq} delta ids=[{string.Join(",", ids)}]");
        Assert.Equal(missing.Select(item => item.Seq).ToArray(), ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(cursor, ids);
        Assert.DoesNotContain(hiddenSeq, ids);
        Assert.DoesNotContain(delta, Matches(hiddenContent));
        Assert.DoesNotContain(delta, Matches(preContent));

        // And the same connection keeps delivering live after the backlog (no reopen needed).
        var liveContent = StreamHarness.Unique("depois");
        var liveId = await StreamHarness.PublishAsync(owner, aberto, liveContent);
        var live = await back.ReadUntilAsync(Matches(liveContent), TimeSpan.FromSeconds(5));
        var liveFrame = Assert.Single(live, Matches(liveContent));
        Assert.Equal(
            (await StreamHarness.GlobalSeqAsync(_host, liveId)).ToString(CultureInfo.InvariantCulture),
            liveFrame.Id);
    }

    // ---------------------------------------------------------------- AC-03

    [Fact] // AC-03 — heartbeat na cadência INJETADA, sem id (nunca move o Last-Event-ID do cliente)
    public async Task Ac03_qa_idle_connection_pings_on_the_injected_interval_without_an_id()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "qa03dono");
        using var stream = await SseConnection.OpenAsync(_host, owner);

        // O host roda Stream:PingSeconds=0.2 (o intervalo injetável do AC): o contrato manda 15 s,
        // e a janela de 5 s abaixo faria este teste VERMELHO se a cadência injetada fosse ignorada.
        var first = await ReadPingAsync(stream);
        Assert.Null(first.Id); // um ping nunca pode mover o Last-Event-ID de um cliente
        Assert.Equal("{}", first.Data);

        var gap = Stopwatch.StartNew();
        var second = await ReadPingAsync(stream);
        gap.Stop();

        _output.WriteLine(
            $"AC-03 observacao: intervalo entre pings = {gap.Elapsed.TotalMilliseconds:F0} ms (Stream:PingSeconds=0.2, contrato=15000)");
        Assert.True(gap.Elapsed >= TimeSpan.FromMilliseconds(100),
            $"o heartbeat é periódico, o intervalo observado foi {gap.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(gap.Elapsed <= TimeSpan.FromSeconds(2),
            $"a cadência injetada de 0.2 s foi ignorada: {gap.Elapsed.TotalMilliseconds:F0} ms entre pings");
    }

    // ---------------------------------------------------------------- AC-04

    [Fact] // AC-04 — gate de visibilidade LIDO NA ENTREGA: proibido na reconexão, não só na publicação
    public async Task Ac04_qa_posts_that_became_forbidden_while_the_subscriber_was_away_never_replay()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "qa04dono");
        var (reader, readerHandle) = await StreamHarness.SignedInAsync(_host, "qa04leitor");

        var aberto = await StreamHarness.NewContainerAsync(owner, "qa04aberto"); // sempre Público
        var restrito = await StreamHarness.NewContainerAsync(owner, "qa04restrito");
        await StreamHarness.SetVisibilityAsync(owner, restrito, "membersonly"); // proibido desde o começo
        var flip = await StreamHarness.NewContainerAsync(owner, "qa04flip"); // Público... por enquanto
        var bane = await StreamHarness.NewContainerAsync(owner, "qa04bane");
        await StreamHarness.JoinAsync(reader, bane); // membro Active... por enquanto

        long cursor;
        var preContent = StreamHarness.Unique("antes");
        using (var first = await SseConnection.OpenAsync(_host, reader))
        {
            var preId = await StreamHarness.PublishAsync(owner, aberto, preContent);
            cursor = await StreamHarness.GlobalSeqAsync(_host, preId);
            var seen = await first.ReadUntilAsync(Matches(preContent), TimeSpan.FromSeconds(5));
            Assert.Single(seen, Matches(preContent));
        } // daqui para baixo o assinante está DESCONECTADO até a reconexão

        var hiddenContent = StreamHarness.Unique("nomembro");
        var hiddenId = await StreamHarness.PublishAsync(owner, restrito, hiddenContent);

        // Visible AT PUBLISH TIME (container Público), forbidden only afterwards: a gate frozen at
        // publish would deliver this one on replay.
        var flipContent = StreamHarness.Unique("politica");
        var flipId = await StreamHarness.PublishAsync(owner, flip, flipContent);
        await StreamHarness.SetVisibilityAsync(owner, flip, "membersonly");

        // Visible AT PUBLISH TIME (assinante Active), forbidden only afterwards — same proof, ban.
        var banContent = StreamHarness.Unique("banido");
        var banId = await StreamHarness.PublishAsync(owner, bane, banContent);
        await StreamHarness.BanAsync(owner, bane, readerHandle);

        var controlContent = StreamHarness.Unique("controle");
        var controlId = await StreamHarness.PublishAsync(owner, aberto, controlContent);

        var hiddenSeq = await StreamHarness.GlobalSeqAsync(_host, hiddenId);
        var flipSeq = await StreamHarness.GlobalSeqAsync(_host, flipId);
        var banSeq = await StreamHarness.GlobalSeqAsync(_host, banId);
        var controlSeq = await StreamHarness.GlobalSeqAsync(_host, controlId);
        Assert.True(hiddenSeq < controlSeq && flipSeq < controlSeq && banSeq < controlSeq,
            "os três proibidos precisam preceder o controle na seq para provar o gate");
        // `restrito` nasceu agora: seu primeiro post vale Object.Seq = 1 (SqliteObjectStore),
        // então um id GLOBAL acima de 1 prova que o quadro não carrega a seq do contêiner.
        Assert.True(hiddenSeq > 1, $"o id do stream é o Activity.Seq global, não o Object.Seq=1: {hiddenSeq}");

        using var back = await SseConnection.OpenAsync(
            _host, reader, lastEventId: cursor.ToString(CultureInfo.InvariantCulture));
        var replay = await back.ReadUntilAsync(Matches(controlContent), TimeSpan.FromSeconds(5));

        var control = Assert.Single(replay, Matches(controlContent));
        Assert.Equal(controlSeq.ToString(CultureInfo.InvariantCulture), control.Id);
        Assert.DoesNotContain(replay, Matches(hiddenContent));
        Assert.DoesNotContain(replay, Matches(flipContent));
        Assert.DoesNotContain(replay, Matches(banContent));

        var ids = PostIds(replay);
        _output.WriteLine(
            $"AC-04 observacao: cursor={cursor} nomembro={hiddenSeq} politica={flipSeq} ban={banSeq} controle={controlSeq} ids no replay=[{string.Join(",", ids)}]");
        Assert.Equal(new[] { controlSeq }, ids); // nada além do controle atravessa o gate
    }

    /// <summary>
    /// O espelho do AC-04 (mesma leitura de visibilidade do feed): um post publicado enquanto o
    /// contêiner era MembersOnly — invisível para o assinante na publicação e na assinatura —
    /// ENTREGA na reconexão quando a política afrouxou para Público enquanto ele estava fora.
    /// Sem isto, um gate congelado na publicação passaria no Ac04 acima ao suprimir este quadro.
    /// </summary>
    [Fact] // AC-04 (espelho) — afrouxamento da política lido NA ENTREGA, não congelado
    public async Task Ac04b_qa_a_post_that_became_visible_while_the_subscriber_was_away_is_delivered_on_replay()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "qa04bdono");
        var (reader, _) = await StreamHarness.SignedInAsync(_host, "qa04bleitor");

        var aberto = await StreamHarness.NewContainerAsync(owner, "qa04baberto");
        var restrito = await StreamHarness.NewContainerAsync(owner, "qa04brestrito");
        await StreamHarness.SetVisibilityAsync(owner, restrito, "membersonly");

        long cursor;
        var preContent = StreamHarness.Unique("antes");
        using (var first = await SseConnection.OpenAsync(_host, reader))
        {
            var preId = await StreamHarness.PublishAsync(owner, aberto, preContent);
            cursor = await StreamHarness.GlobalSeqAsync(_host, preId);
            var seen = await first.ReadUntilAsync(Matches(preContent), TimeSpan.FromSeconds(5));
            Assert.Single(seen, Matches(preContent));
        } // daqui para baixo o assinante está DESCONECTADO até a reconexão

        // Invisible at publish AND at subscribe (MembersOnly, leitor sem membros).
        var secretContent = StreamHarness.Unique("segredo");
        var secretId = await StreamHarness.PublishAsync(owner, restrito, secretContent);
        var secretSeq = await StreamHarness.GlobalSeqAsync(_host, secretId);

        // O contêiner afrouxa enquanto ele está fora: a leitura é corrente, como a do feed.
        await StreamHarness.SetVisibilityAsync(owner, restrito, "public");

        var controlContent = StreamHarness.Unique("controle");
        var controlId = await StreamHarness.PublishAsync(owner, aberto, controlContent);
        var controlSeq = await StreamHarness.GlobalSeqAsync(_host, controlId);
        Assert.True(secretSeq < controlSeq && secretSeq > cursor,
            "o post antigo precisa estar na janela perdida, antes do controle");

        using var back = await SseConnection.OpenAsync(
            _host, reader, lastEventId: cursor.ToString(CultureInfo.InvariantCulture));
        var replay = await back.ReadUntilAsync(Matches(controlContent), TimeSpan.FromSeconds(5));

        var secret = Assert.Single(replay, Matches(secretContent));
        Assert.Equal(secretSeq.ToString(CultureInfo.InvariantCulture), secret.Id);
        Assert.Single(replay, Matches(controlContent));
        var ids = PostIds(replay);
        _output.WriteLine(
            $"AC-04b observacao: cursor={cursor} segredo={secretSeq} politica afrouxada para public controle={controlSeq} ids no replay=[{string.Join(",", ids)}]");
        Assert.Equal(new[] { secretSeq, controlSeq }, ids);
    }

    // ---------------------------------------------------------------- AC-05

    [Fact] // AC-05 — 401 sem sessão + publicador que não trava nem falha por causa do assinante
    public async Task Ac05_qa_sessionless_connect_is_401_and_no_subscriber_can_block_or_fail_a_publish()
    {
        var anonymous = _host.NewClient();
        using var refused = await SseConnection.OpenAsync(_host, anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.Equal("application/json", refused.ContentType); // nunca text/event-stream
        Assert.Contains("not_authenticated", await refused.ReadBodyAsync(), StringComparison.Ordinal);

        var (owner, _) = await StreamHarness.SignedInAsync(_host, "qa05dono");
        var slug = await StreamHarness.NewContainerAsync(owner, "qa05");

        // An assinante PARADO: registrado no hub e nunca lê (o canal é bounded(1), o wake cai sozinho).
        var hub = _host.App.Services.GetRequiredService<StreamHub>();
        using var stalled = hub.Subscribe();

        var watch = Stopwatch.StartNew();
        for (var i = 1; i <= 5; i++)
        {
            // bounded: um publicador travado aqui estoura em 10 s e deixa este teste vermelho.
            var id = await StreamHarness.PublishAsync(owner, slug, StreamHarness.Unique($"parado{i}"))
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(string.IsNullOrEmpty(id));
        }

        // ... e o assinante que SAI (o finally da rota quando o cliente cai) muda nada.
        stalled.Dispose();
        for (var i = 1; i <= 3; i++)
        {
            var id = await StreamHarness.PublishAsync(owner, slug, StreamHarness.Unique($"saiu{i}"))
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(string.IsNullOrEmpty(id));
        }

        watch.Stop();
        _output.WriteLine(
            $"AC-05 observacao: 8 publicacoes (5 com assinante parado, 3 depois da saida) em {watch.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10),
            $"o publicador nao pode esperar por assinante: 8 publicacoes levaram {watch.Elapsed.TotalMilliseconds:F0} ms");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Publishes and keeps the RAW 201 body, so `data` can be compared with the answer.</summary>
    private async Task<(string PostId, string RawBody)> PublishRawAsync(AuthClient author, string slug, string content)
    {
        var response = await PostRoutes.SendAsync(author, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content }, author.Csrf);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var parsed = JsonDocument.Parse(body);
        return (parsed.RootElement.GetProperty("id").GetString()!, body);
    }

    /// <summary>Publishes and answers (global seq, content) — the pair a delta assertion compares.</summary>
    private async Task<(string Content, long Seq)> PublishRecordedAsync(AuthClient author, string slug, string content)
    {
        var (postId, _) = await PublishRawAsync(author, slug, content);
        return (content, await StreamHarness.GlobalSeqAsync(_host, postId));
    }

    /// <summary>Publishes and answers (post id, content) — for a post whose seq is read later.</summary>
    private async Task<(string PostId, string Content)> PublishNamedAsync(AuthClient author, string slug, string content)
    {
        var (postId, _) = await PublishRawAsync(author, slug, content);
        return (postId, content);
    }

    /// <summary>The next heartbeat frame, never waiting longer than the window allows.</summary>
    private static async Task<SseFrame> ReadPingAsync(SseConnection stream)
    {
        var seen = await stream.ReadUntilAsync(frame => frame.Event == "ping", TimeSpan.FromSeconds(5));
        var ping = seen.LastOrDefault(frame => frame.Event == "ping");
        Assert.True(ping is not null, "a conexao precisa heartbeatar dentro da janela configurada");
        return ping!;
    }

    /// <summary>The global ids of every `post` frame, in the order the wire carried them.</summary>
    private static List<long> PostIds(IEnumerable<SseFrame> frames) =>
        frames.Where(frame => frame.Event == "post")
            .Select(frame => long.Parse(frame.Id!, CultureInfo.InvariantCulture))
            .ToList();

    /// <summary>The frame carrying one unique content (pings and foreign backlog never match).</summary>
    private static Predicate<SseFrame> Matches(string content) =>
        frame => frame.Data is not null && frame.Data.Contains(content, StringComparison.Ordinal);
}
