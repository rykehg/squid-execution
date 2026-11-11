using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Posts;
using Squid.Domain.Objects;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Xunit.Abstractions;

namespace Squid.Tests.Feed;

/// <summary>
/// QA-0024 (TASK-0025) — the QA's OWN scenarios over the seven acceptance criteria, executed in
/// this session rather than re-read from the DEV suites: the exact, ordered and idempotent
/// <c>?since=</c> delta walk (AC-01), the pagination decisions (default 20 / cap 100 /
/// beyond-range empty page), CURRENT visibility after a tighter policy for page AND delta
/// (AC-02), the Active-membership personal timeline with entry and exit (AC-03), the unlisted
/// DoD of Sprint 3 (AC-04), banned vs silenced reading (AC-05), a 100-attempt SC-005 probe
/// behind AC-02/AC-04/AC-05, and the content cap held AFTER sanitization with nothing persisted
/// (AC-08). AC-06 is the gate run itself (sdd-test.ps1 test) proven by the gate log.
/// Every cursor is read from a RESPONSE, never from a publish answer: the feed speaks the
/// global Activity.Seq (GAP-23) while publishing answers the per-container Object.Seq.
/// Expected lengths are derived from the artifact (<see cref="PostObject.MaxContentLength"/>)
/// instead of a remembered number.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0025Tests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    private readonly ITestOutputHelper _output;

    public QaTask0025Tests(AuthHostFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    private IActivityStore Activities =>
        _host.App.Services.GetRequiredService<IActivityStore>();

    // ---------------------------------------------------------------- AC-01

    [Fact] // AC-01 — página + duas chamadas encadeadas + chamada repetida (idempotente)
    public async Task Ac01_qa_chained_and_repeated_since_calls_answer_the_exact_delta()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa01dono");
        var slug = await NewContainerAsync(owner);
        var published = new List<string>();
        for (var n = 1; n <= 5; n++)
        {
            var text = $"qa01-p{n}";
            published.Add(text);
            await PublishAsync(owner, slug, text);
        }

        await ProjectAsync();

        // A página cronológica: mais recente primeiro, ordem pura por sequência (FR-023).
        var first = await GetAsync(owner, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = await FeedHarness.JsonOfAsync(first);
        var items = FeedHarness.Items(page);
        Assert.Equal(published.AsEnumerable().Reverse(), FeedHarness.Contents(items));
        var seqs = FeedHarness.Seqs(items);
        for (var i = 1; i < seqs.Count; i++)
        {
            Assert.True(seqs[i - 1] > seqs[i], "a página cronológica desce o seq (FR-023)");
        }

        Assert.Equal(seqs[0], page.GetProperty("nextSince").GetInt64());

        // Caminhada por deltas encadeados de limit=2: cada passo é exato, ascendente e repetível.
        var walked = new List<string>();
        var walkedSeqs = new List<long>();
        long since = 0;
        while (true)
        {
            var path = $"/api/containers/{slug}/feed?since={since}&limit=2";
            var response = await GetAsync(owner, path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await FeedHarness.JsonOfAsync(response);
            var pageItems = FeedHarness.Items(body);
            var pageSeqs = FeedHarness.Seqs(pageItems);

            // A chamada repetida é idempotente: o mesmo delta, byte a byte (§9.3).
            var repeated = await GetAsync(owner, path);
            Assert.Equal(await FeedHarness.BytesOf(response), await FeedHarness.BytesOf(repeated));

            if (pageSeqs.Count == 0)
            {
                Assert.Equal(0, pageItems.GetArrayLength());
                Assert.Equal(since, body.GetProperty("nextSince").GetInt64()); // página vazia mantém o cursor
                Assert.False(body.GetProperty("hasMore").GetBoolean());
                break;
            }

            Assert.All(pageSeqs, value => Assert.True(
                value > since, "todo item do delta é estritamente maior que o cursor"));
            for (var i = 1; i < pageSeqs.Count; i++)
            {
                Assert.True(pageSeqs[i - 1] < pageSeqs[i], "o delta responde em ordem de seq (§9.3)");
            }

            Assert.Equal(pageSeqs.Max(), body.GetProperty("nextSince").GetInt64());
            Assert.Equal(pageSeqs.Count == 2, body.GetProperty("hasMore").GetBoolean());
            walked.AddRange(FeedHarness.Contents(pageItems));
            walkedSeqs.AddRange(pageSeqs);
            since = pageSeqs.Max();
        }

        // Sem duplicata e sem buraco: a caminhada reconstrói a série publicada, na ordem de seq.
        Assert.Equal(published, walked);
        Assert.Equal(walkedSeqs.Count, walkedSeqs.Distinct().Count());
        _output.WriteLine(
            $"AC-01: caminhada de {walkedSeqs.Count} itens reconstruiu a serie publicada, sem duplicatas");
    }

    // --------------------------------- decisão de casa do DEV (verificada, não discutida)

    [Fact]
    public async Task Pagination_default_is_20_the_cap_is_100_and_beyond_range_since_is_empty()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qapag");
        var slug = await NewContainerAsync(owner);
        for (var n = 1; n <= 101; n++)
        {
            await PublishAsync(owner, slug, $"qapag-{n:D3}");
        }

        await ProjectAsync();

        // Página default = 20 (contrato §1), mais recente primeiro, diz que ainda há mais.
        var first = await GetAsync(owner, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await FeedHarness.JsonOfAsync(first);
        var items = FeedHarness.Items(body);
        var contents = FeedHarness.Contents(items);
        Assert.Equal(20, contents.Count);
        Assert.Equal("qapag-101", contents[0]);
        Assert.Equal("qapag-082", contents[19]);
        Assert.True(body.GetProperty("hasMore").GetBoolean());
        var maxSeq = FeedHarness.Seqs(items)[0];

        // Teto = 100: um limit acima do teto desce para 100 linhas, nunca mais.
        var cappedResponse = await GetAsync(owner, $"/api/containers/{slug}/feed?limit=500");
        Assert.Equal(HttpStatusCode.OK, cappedResponse.StatusCode);
        var capped = await FeedHarness.JsonOfAsync(cappedResponse);
        var cappedItems = FeedHarness.Items(capped);
        Assert.Equal(100, cappedItems.GetArrayLength());
        Assert.Equal(maxSeq, FeedHarness.Seqs(cappedItems)[0]);

        // since além do máximo devolve lista vazia — nunca erro — e mantém o cursor do chamador.
        var beyondValue = maxSeq + 1000;
        var beyond = await GetAsync(owner, $"/api/containers/{slug}/feed?since={beyondValue}");
        Assert.Equal(HttpStatusCode.OK, beyond.StatusCode);
        var beyondBody = await FeedHarness.JsonOfAsync(beyond);
        Assert.Equal(0, FeedHarness.Items(beyondBody).GetArrayLength());
        Assert.Equal(beyondValue, beyondBody.GetProperty("nextSince").GetInt64());
        Assert.False(beyondBody.GetProperty("hasMore").GetBoolean());

        // limit <= 0 é o 400 validation_failed do catálogo (§8), nunca um 500.
        var invalid = await GetAsync(owner, $"/api/containers/{slug}/feed?limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(
            "validation_failed",
            await invalid.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        _output.WriteLine("paginacao: default 20, teto 100, alem do maximo = lista vazia, limit=0 = 400");
    }

    // ---------------------------------------------------------------- AC-02

    [Fact] // AC-02 — visibilidade corrente para a página E para o delta, 404 byte-idêntico
    public async Task Ac02_qa_tighter_policy_answers_the_404_of_absent_for_page_and_delta()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa02dono");
        var (reader, _) = await PostRoutes.SignedInAsync(_host, "qa02leitor");
        var slug = await NewContainerAsync(owner);
        await PublishAsync(owner, slug, "qa02-aberto-1");
        await PublishAsync(owner, slug, "qa02-aberto-2");
        await ProjectAsync();

        // Antes da mudança o não-membro lê a página pública e guarda o cursor (viu antes).
        var open = await GetAsync(reader, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var openBody = await FeedHarness.JsonOfAsync(open);
        Assert.Equal(2, FeedHarness.Items(openBody).GetArrayLength());
        var cursor = openBody.GetProperty("nextSince").GetInt64();

        var absent = PostRoutes.Slug("qa0025ausente");

        // Mais restritivo (MembersOnly): quem perdeu acesso recebe o 404 idêntico do inexistente —
        // na página cheia e no delta, porque o delta também é leitura (FR-026).
        await SetVisibilityAsync(owner, slug, "membersonly");
        await AssertIdentical404Async(
            reader, $"/api/containers/{slug}/feed", $"/api/containers/{absent}/feed");
        await AssertIdentical404Async(
            reader,
            $"/api/containers/{slug}/feed?since={cursor}",
            $"/api/containers/{absent}/feed?since={cursor}");

        // Private responde igual (AC-02: Private e MembersOnly para não-membro).
        await SetVisibilityAsync(owner, slug, "private");
        await AssertIdentical404Async(
            reader, $"/api/containers/{slug}/feed", $"/api/containers/{absent}/feed");
        await AssertIdentical404Async(
            reader,
            $"/api/containers/{slug}/feed?since={cursor}",
            $"/api/containers/{absent}/feed?since={cursor}");

        // E nada dos dois estados entra na timeline de quem nunca foi membro.
        var timeline = await GetAsync(reader, "/api/timeline");
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
        var readerItems = FeedHarness.Items(await FeedHarness.JsonOfAsync(timeline));
        Assert.DoesNotContain(
            FeedHarness.ContainerIds(readerItems),
            id => id == $"{PostRoutes.Origin(_host)}/c/{slug}");

        // Quem ainda tem acesso continua lendo: a restrição atinge quem perdeu, não quem o tem.
        var ownerFeed = await GetAsync(owner, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, ownerFeed.StatusCode);
        Assert.Equal(
            2, FeedHarness.Items(await FeedHarness.JsonOfAsync(ownerFeed)).GetArrayLength());
    }

    // ---------------------------------------------------------------- AC-03

    [Fact] // AC-03 — só contêineres com membership Active, mesma semântica de delta, entrar/sair
    public async Task Ac03_qa_timeline_serves_active_memberships_with_the_same_delta()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa03dono");
        var (member, _) = await PostRoutes.SignedInAsync(_host, "qa03membro");
        var slugA = await NewContainerAsync(owner);
        var slugB = await NewContainerAsync(owner);
        await JoinAsync(member, slugA);
        await JoinAsync(member, slugB);
        await PublishAsync(owner, slugA, "qa03-a1");
        await PublishAsync(owner, slugB, "qa03-b1");
        await ProjectAsync();

        var first = await GetAsync(member, "/api/timeline");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = await FeedHarness.JsonOfAsync(first);
        var items = FeedHarness.Items(page);
        Assert.Equal(new[] { "qa03-b1", "qa03-a1" }, FeedHarness.Contents(items));
        var containerA = $"{PostRoutes.Origin(_host)}/c/{slugA}";
        var containerB = $"{PostRoutes.Origin(_host)}/c/{slugB}";
        Assert.All(
            FeedHarness.ContainerIds(items),
            id => Assert.True(id == containerA || id == containerB, "só contêineres meus na página"));
        var pageSeqs = FeedHarness.Seqs(items);
        for (var i = 1; i < pageSeqs.Count; i++)
        {
            Assert.True(pageSeqs[i - 1] > pageSeqs[i], "a timeline também é mais-recente-primeiro");
        }

        var cursor = page.GetProperty("nextSince").GetInt64();

        // Mesma semântica de delta e ordem global de seq atravessando contêineres (GAP-23).
        await PublishAsync(owner, slugA, "qa03-a2");
        await PublishAsync(owner, slugB, "qa03-b2");
        await ProjectAsync();
        var deltaPath = $"/api/timeline?since={cursor}";
        var delta = await GetAsync(member, deltaPath);
        Assert.Equal(HttpStatusCode.OK, delta.StatusCode);
        var deltaBody = await FeedHarness.JsonOfAsync(delta);
        var deltaItems = FeedHarness.Items(deltaBody);
        Assert.Equal(new[] { "qa03-a2", "qa03-b2" }, FeedHarness.Contents(deltaItems));
        var deltaSeqs = FeedHarness.Seqs(deltaItems);
        Assert.All(deltaSeqs, value => Assert.True(value > cursor, "delta estritamente acima do cursor"));
        for (var i = 1; i < deltaSeqs.Count; i++)
        {
            Assert.True(deltaSeqs[i - 1] < deltaSeqs[i], "o delta responde em ordem de seq (§9.3)");
        }

        var repeated = await GetAsync(member, deltaPath);
        Assert.Equal(await FeedHarness.BytesOf(delta), await FeedHarness.BytesOf(repeated));

        // SAIR muda o que aparece: os itens do A somem mesmo depois de vistos (FR-026), B fica.
        var left = await PostRoutes.SendAsync(
            member, HttpMethod.Post, $"/api/containers/{slugA}/leave", null, member.Csrf);
        Assert.True(left.IsSuccessStatusCode, $"leave failed: {left.StatusCode}");
        var afterLeave = FeedHarness.Items(
            await FeedHarness.JsonOfAsync(await GetAsync(member, "/api/timeline")));
        Assert.DoesNotContain(FeedHarness.ContainerIds(afterLeave), id => id == containerA);
        Assert.Contains(FeedHarness.Contents(afterLeave), text => text == "qa03-b1");
        Assert.Contains(FeedHarness.Contents(afterLeave), text => text == "qa03-b2");

        // ENTRAR muda o que aparece: de volta ao A, os itens dele voltam para a timeline.
        var rejoined = await PostRoutes.SendAsync(
            member, HttpMethod.Post, $"/api/containers/{slugA}/join", null, member.Csrf);
        Assert.True(rejoined.IsSuccessStatusCode, $"join failed: {rejoined.StatusCode}");
        var afterEntry = FeedHarness.Items(
            await FeedHarness.JsonOfAsync(await GetAsync(member, "/api/timeline")));
        Assert.Contains(FeedHarness.ContainerIds(afterEntry), id => id == containerA);
        Assert.Contains(FeedHarness.Contents(afterEntry), text => text == "qa03-a1");
        Assert.Contains(FeedHarness.Contents(afterEntry), text => text == "qa03-a2");

        // Entrada PÓS-projeção num contêiner novo: o item publicado DEPOIS da entrada chega à
        // timeline (membros lidos na projeção — SPEC-0009 B emendado). O comportamento quanto ao
        // item anterior à entrada é observado aqui e reportado como achado, não como AC.
        var slugC = await NewContainerAsync(owner);
        await PublishAsync(owner, slugC, "qa03-c1-anterior-a-entrada");
        await ProjectAsync();
        await JoinAsync(member, slugC);
        await PublishAsync(owner, slugC, "qa03-c2-posterior-a-entrada");
        await ProjectAsync();
        var afterFreshJoin = FeedHarness.Contents(FeedHarness.Items(
            await FeedHarness.JsonOfAsync(await GetAsync(member, "/api/timeline"))));
        Assert.Contains(afterFreshJoin, text => text == "qa03-c2-posterior-a-entrada");
        _output.WriteLine(
            "AC-03 observacao: item anterior a entrada fresca presente na timeline = "
            + afterFreshJoin.Contains("qa03-c1-anterior-a-entrada"));
    }

    // ---------------------------------------------------------------- AC-04

    [Fact] // AC-04 — o DoD do Sprint 3: unlisted não serve não-membro e nunca vaza para terceiros
    public async Task Ac04_qa_unlisted_is_404_identical_and_never_reaches_a_third_party_timeline()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa04dono");
        var (member, _) = await PostRoutes.SignedInAsync(_host, "qa04membro");
        var (third, _) = await PostRoutes.SignedInAsync(_host, "qa04terceiro");
        var slug = await NewContainerAsync(owner);
        await JoinAsync(member, slug);
        await SetVisibilityAsync(owner, slug, "unlisted");
        await PublishAsync(owner, slug, "qa0025leak-unlisted-1");
        await PublishAsync(owner, slug, "qa0025leak-unlisted-2");
        await ProjectAsync();

        // Não-público nesta fase (código P-03): feed do não-membro é o 404 idêntico ao
        // inexistente, na página cheia e no delta.
        var absent = PostRoutes.Slug("qa0025ausente");
        await AssertIdentical404Async(
            third, $"/api/containers/{slug}/feed", $"/api/containers/{absent}/feed");
        await AssertIdentical404Async(
            third,
            $"/api/containers/{slug}/feed?since=1",
            $"/api/containers/{absent}/feed?since=1");

        // O membro Active continua lendo: a leitura usa a membership corrente.
        var memberFeed = await GetAsync(member, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, memberFeed.StatusCode);
        Assert.Equal(
            2, FeedHarness.Items(await FeedHarness.JsonOfAsync(memberFeed)).GetArrayLength());

        // O terceiro com timeline própria nunca carrega os itens do unlisted.
        var ownSlug = await NewContainerAsync(third);
        await PublishAsync(third, ownSlug, "qa04-do-terceiro");
        await ProjectAsync();
        var timeline = await GetAsync(third, "/api/timeline");
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
        var items = FeedHarness.Items(await FeedHarness.JsonOfAsync(timeline));
        Assert.Contains(FeedHarness.Contents(items), text => text == "qa04-do-terceiro");
        Assert.DoesNotContain(
            FeedHarness.ContainerIds(items),
            id => id == $"{PostRoutes.Origin(_host)}/c/{slug}");
        Assert.DoesNotContain(
            FeedHarness.Contents(items),
            text => text.StartsWith("qa0025leak-unlisted", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- AC-05

    [Fact] // AC-05 — Banned não lê (404 idêntico); Silenced lê normalmente
    public async Task Ac05_qa_banned_reads_nothing_and_silenced_reads_normally()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa05dono");
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "qa05membro");
        var slug = await NewContainerAsync(owner);
        await JoinAsync(member, slug);
        await PublishAsync(owner, slug, "qa05-clubinho");
        await ProjectAsync();
        var absent = PostRoutes.Slug("qa0025ausente");

        // Silenced lê normalmente o feed (§3.3: Silenced não é Ban).
        await MemberStateAsync(owner, slug, memberHandle, new { state = "silence" });
        var whileSilenced = await GetAsync(member, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, whileSilenced.StatusCode);
        Assert.Contains(
            FeedHarness.Contents(FeedHarness.Items(await FeedHarness.JsonOfAsync(whileSilenced))),
            text => text == "qa05-clubinho");

        // Observação registrada (AC-03 diz "só Active"): o item na timeline pessoal de um
        // membro Silenced, para o ODEV conferir o escopo da frase "lê normalmente".
        var silencedTimeline = FeedHarness.Contents(FeedHarness.Items(
            await FeedHarness.JsonOfAsync(await GetAsync(member, "/api/timeline"))));
        _output.WriteLine(
            "AC-05 observacao: item na timeline de membro Silenced = "
            + silencedTimeline.Contains("qa05-clubinho"));

        // Banned não lê: volta a Active primeiro (a §3.3 só ban a partir de Active), depois ban.
        await MemberStateAsync(owner, slug, memberHandle, new { state = "unsilence" });
        await MemberStateAsync(
            owner, slug, memberHandle,
            new { state = "ban", justification = "qa05 quebra a regra" });

        var whileBanned = await GetAsync(member, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.NotFound, whileBanned.StatusCode);
        await AssertIdentical404Async(
            member, $"/api/containers/{slug}/feed", $"/api/containers/{absent}/feed");
        await AssertIdentical404Async(
            member,
            $"/api/containers/{slug}/feed?since=1",
            $"/api/containers/{absent}/feed?since=1");

        // E o que ele chegou a ver some da timeline: a membership lida é a corrente.
        var bannedTimeline = FeedHarness.Items(
            await FeedHarness.JsonOfAsync(await GetAsync(member, "/api/timeline")));
        Assert.DoesNotContain(
            FeedHarness.ContainerIds(bannedTimeline),
            id => id == $"{PostRoutes.Origin(_host)}/c/{slug}");
    }

    // ---------------------------------------------------------------- SC-005 (rigor dos ACs)

    [Fact] // SC-005 — 100 tentativas: oculto == inexistente, byte a byte, em todos os estados
    public async Task Sc005_qa_100_attempts_of_hidden_feeds_are_byte_identical_to_absent()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa0025scdono");
        var (reader, readerHandle) = await PostRoutes.SignedInAsync(_host, "qa0025scleitor");

        // Cinco estados escontidos com conteúdo real (um vazamento seria visível neles).
        var tightened = await NewContainerAsync(owner); // nasce público; o leitor lê e depois perde
        var banned = await NewContainerAsync(owner);
        var privada = await NewContainerAsync(owner);
        var membersonly = await NewContainerAsync(owner);
        var unlisted = await NewContainerAsync(owner);
        await JoinAsync(reader, banned);

        foreach (var slug in new[] { tightened, banned, privada, membersonly, unlisted })
        {
            await PublishAsync(owner, slug, $"qa0025leak-{slug}-1");
            await PublishAsync(owner, slug, $"qa0025leak-{slug}-2");
        }

        await ProjectAsync();

        // O único instante em que o leitor enxerga algo: o caso "viu antes" de SC-005/FR-026.
        var seen = await GetAsync(reader, $"/api/containers/{tightened}/feed");
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);

        await SetVisibilityAsync(owner, tightened, "membersonly");
        await SetVisibilityAsync(owner, privada, "private");
        await SetVisibilityAsync(owner, membersonly, "membersonly");
        await SetVisibilityAsync(owner, unlisted, "unlisted");
        await MemberStateAsync(
            owner, banned, readerHandle, new { state = "ban", justification = "qa0025 sc005" });

        var absent = PostRoutes.Slug("qa0025ausente");
        var states = new[] { privada, membersonly, unlisted, tightened, banned };
        var variants = new[] { string.Empty, "?since=1", "?limit=5" };
        var attempts = 0;
        for (var i = 0; i < 100; i++)
        {
            var state = states[i % states.Length];
            var variant = variants[(i / states.Length) % variants.Length];
            var hiddenPath = $"/api/containers/{state}/feed{variant}";
            var controlPath = $"/api/containers/{absent}/feed{variant}";

            var hidden = await GetAsync(reader, hiddenPath);
            var control = await GetAsync(reader, controlPath);
            var hiddenBytes = await FeedHarness.BytesOf(hidden);
            var controlBytes = await FeedHarness.BytesOf(control);
            var hiddenBody = await hidden.Content.ReadAsStringAsync();

            if (hidden.StatusCode != HttpStatusCode.NotFound)
            {
                Assert.Fail(
                    $"SC-005 tentativa {i} ({variant}): oculto respondeu {(int)hidden.StatusCode} em {hiddenPath}");
            }

            if (control.StatusCode != HttpStatusCode.NotFound)
            {
                Assert.Fail($"SC-005 tentativa {i} ({variant}): controle respondeu {(int)control.StatusCode}");
            }

            if (!hiddenBytes.SequenceEqual(controlBytes))
            {
                Assert.Fail(
                    $"SC-005 tentativa {i} ({variant}): corpo difere do inexistente: {hiddenBody}");
            }

            if (hiddenBody.Contains("traceId", StringComparison.OrdinalIgnoreCase)
                || hiddenBody.Contains("qa0025leak", StringComparison.Ordinal)
                || hiddenBody.Contains(state, StringComparison.Ordinal))
            {
                Assert.Fail($"SC-005 tentativa {i} ({variant}): vaza informacao: {hiddenBody}");
            }

            attempts++;
        }

        Assert.Equal(100, attempts);
        _output.WriteLine(
            "SC-005: 100 tentativas (5 estados x 3 variantes de query), 0 vazamentos, 404 byte-identico ao inexistente");
    }

    // ---------------------------------------------------------------- AC-08

    [Fact] // AC-08 — cap conferido APÓS a sanitização, com nada gravado nas recusas
    public async Task Ac08_qa_the_cap_holds_after_sanitization_and_nothing_is_persisted()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa08dono");
        var slug = await NewContainerAsync(owner);
        var cap = PostObject.MaxContentLength; // data-model §2.5, derivado do artefato
        var objects0 = await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName);
        var events0 = (await Activities.ReadAsync()).Count;

        // Caso 1: corpo bruto acima do cap continua 400 na checagem cedo, sem gravar nada.
        var raw = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = new string('x', cap + 1) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, raw.StatusCode);
        Assert.Contains(
            "validation_failed", await raw.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Caso 2: corpo bruto DENTRO do cap cujo conteúdo escapado cruza o cap — a repro da
        // QA-0022/F1 (5000 "&" virariam 25000 no Content persistido).
        var amplified = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = new string('&', cap) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, amplified.StatusCode);
        Assert.Contains(
            "validation_failed",
            await amplified.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // Fronteira degrau logo acima do teto escapado: também recusado, também sem gravar.
        var stepped = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = new string('&', (cap / 4) + 1) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, stepped.StatusCode);

        Assert.Equal(objects0, await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName));
        Assert.Equal(events0, (await Activities.ReadAsync()).Count);

        // O pai da resposta, para o segundo call site (reply) existir.
        var parent = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "qa08-pai" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, parent.StatusCode);
        var parentId = (await FeedHarness.JsonOfAsync(parent)).GetProperty("id").GetString()!;
        var objects1 = await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName);
        var events1 = (await Activities.ReadAsync()).Count;

        // A mesma regra no segundo call site: a resposta também é recusada, sem gravar.
        var reply = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/posts/{Uri.EscapeDataString(parentId)}/replies",
            new { content = new string('<', cap - 1000) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        Assert.Contains(
            "validation_failed", await reply.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(objects1, await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName));
        Assert.Equal(events1, (await Activities.ReadAsync()).Count);

        // Caso 3: escapa para EXATAMENTE o cap → publica, e o campo persistido é o conferido.
        var exact = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = new string('&', cap / 5) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, exact.StatusCode);
        var exactView = await FeedHarness.JsonOfAsync(exact);
        var stored = exactView.GetProperty("content").GetString()!;
        Assert.Equal(cap, stored.Length);
        Assert.Equal(string.Concat(Enumerable.Repeat("&amp;", cap / 5)), stored);

        // O PERSISTIDO lê de volta igual (data-model §2.5: Content ≤ 5000 no campo gravado).
        var read = await GetAsync(
            owner,
            $"/api/posts/{Uri.EscapeDataString(exactView.GetProperty("id").GetString()!)}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBack = await FeedHarness.JsonOfAsync(read);
        Assert.Equal(stored, readBack.GetProperty("content").GetString());
        Assert.True(
            readBack.GetProperty("content").GetString()!.Length <= cap,
            "o Content persistido precisa ficar no cap após a sanitização");

        // Abaixo da fronteira com folga: também publica, com o texto escapado intacto.
        var below = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = new string('&', (cap / 5) - 1) + "fim" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, below.StatusCode);
        var belowStored = (await FeedHarness.JsonOfAsync(below)).GetProperty("content").GetString()!;
        Assert.Equal(cap - 2, belowStored.Length);
        Assert.EndsWith("fim", belowStored, StringComparison.Ordinal);

        // As recusas não projetam linha alguma: o feed só mostra as três publicações válidas.
        await ProjectAsync();
        var feed = await GetAsync(owner, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        var feedItems = FeedHarness.Items(await FeedHarness.JsonOfAsync(feed));
        Assert.Equal(3, feedItems.GetArrayLength());
        var feedContents = FeedHarness.Contents(feedItems);
        Assert.Contains(feedContents, text => text == "qa08-pai");
        Assert.Equal(cap, feedContents.Max(text => text.Length));

        var events2 = (await Activities.ReadAsync()).Count;
        Assert.Equal(events1 + 2, events2);
        _output.WriteLine(
            $"AC-08:3 recusas sem gravar, 2 publicacoes no cap ({cap} persistido), feed com 3 itens");
    }

    // ---------------------------------------------------------------- helpers

    private static Task<HttpResponseMessage> GetAsync(AuthClient client, string path) =>
        PostRoutes.SendAsync(client, HttpMethod.Get, path);

    /// <summary>One container of <paramref name="kind"/>, created by its owner.</summary>
    private static async Task<string> NewContainerAsync(AuthClient owner, string kind = "community")
    {
        var slug = PostRoutes.Slug("qa0025");
        var created = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, "/api/containers",
            new { kind, name = slug, slug }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return slug;
    }

    /// <summary>Publishes one text and pins the 201, so a scenario never reads a projection of a post that failed.</summary>
    private static async Task PublishAsync(AuthClient author, string slug, string content)
    {
        var published = await PostRoutes.SendAsync(
            author, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content }, author.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
    }

    /// <summary>Joins an open community: membership Active from the entry route (SPEC-0008 F).</summary>
    private static async Task JoinAsync(AuthClient joiner, string slug)
    {
        var joined = await PostRoutes.SendAsync(
            joiner, HttpMethod.Post, $"/api/containers/{slug}/join", null, joiner.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");
    }

    /// <summary>PUT /policy with the current version (If-Match → 409 otherwise).</summary>
    private static async Task SetVisibilityAsync(AuthClient owner, string slug, string visibility)
    {
        var version = await PolicyVersionAsync(owner, slug);
        var response = await PostRoutes.SendAsync(
            owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { visibility }, owner.Csrf, ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Membership state change of the owner's member route (silence / unsilence / ban).</summary>
    private static async Task MemberStateAsync(
        AuthClient owner, string slug, string handle, object body)
    {
        var response = await PostRoutes.SendAsync(
            owner, HttpMethod.Patch, $"/api/containers/{slug}/members/{handle}", body, owner.Csrf);
        Assert.True(
            response.IsSuccessStatusCode,
            $"member state change failed: {response.StatusCode}");
    }

    /// <summary>The policy version a PUT must answer with (If-Match → 409 otherwise).</summary>
    private static async Task<int> PolicyVersionAsync(AuthClient reader, string slug)
    {
        var response = await GetAsync(reader, $"/api/containers/{slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await FeedHarness.JsonOfAsync(response);
        return body.GetProperty("policy").GetProperty("version").GetInt32();
    }

    /// <summary>
    /// SC-005's comparison: both paths answer 404 byte-for-byte (no traceId), so existence never
    /// leaks — the hidden slug and the absent one travel different URLs but share one body.
    /// </summary>
    private static async Task AssertIdentical404Async(
        AuthClient client, string hiddenPath, string absentPath)
    {
        var hidden = await client.SendAsync(HttpMethod.Get, hiddenPath);
        var control = await client.SendAsync(HttpMethod.Get, absentPath);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, control.StatusCode);
        Assert.Equal(
            await FeedHarness.BytesOf(control), await FeedHarness.BytesOf(hidden));
        var body = await hidden.Content.ReadAsStringAsync();
        Assert.DoesNotContain("traceId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qa0025leak", body, StringComparison.Ordinal);
    }

    /// <summary>Projects every event past the projections' high-water (the pump's tick, by hand).</summary>
    private Task ProjectAsync() => FeedHarness.ProjectPendingAsync(_host);
}
