using System.Net;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Feed;

/// <summary>
/// TASK-0025 — the five behavioural acceptance criteria before the cap fix (AC-06 is the gate,
/// AC-08 has its own file): the feed page and its exact <c>?since=</c> delta (AC-01), current
/// visibility on read (AC-02), the personal timeline over Active memberships with the same delta
/// (AC-03), the unlisted DoD of Sprint 3 (AC-04) and the banned/silenced reading rule (AC-05).
/// Every cursor is read from a RESPONSE, never from a publish answer: the feed speaks the global
/// Activity.Seq (GAP-23), while publishing answers the per-container Object.Seq.
/// </summary>
[Collection("ServiceHost")]
public sealed class FeedRoutesTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public FeedRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Ac01_feed_page_is_newest_first_and_since_answers_the_exact_delta_in_seq_order()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "fac01");
        var slug = await NewContainerAsync(owner, "community");
        foreach (var text in new[] { "p1", "p2", "p3" })
        {
            await PublishAsync(owner, slug, text);
        }

        await FeedHarness.ProjectPendingAsync(_host);

        // A página cronológica: mais recente primeiro, ordem pura por sequência (FR-023).
        var first = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = await FeedHarness.JsonOfAsync(first);
        var items = FeedHarness.Items(page);
        Assert.Equal(new[] { "p3", "p2", "p1" }, FeedHarness.Contents(items));
        var seqs = FeedHarness.Seqs(items);
        for (var i = 1; i < seqs.Count; i++)
        {
            Assert.True(seqs[i - 1] > seqs[i], "the feed page must be newest first (FR-023)");
        }

        // The page hands the sync its cursor (contract §1: nextSince = the newest row answered).
        var cursor = page.GetProperty("nextSince").GetInt64();
        Assert.Equal(seqs[0], cursor);

        await PublishAsync(owner, slug, "p4");
        await FeedHarness.ProjectPendingAsync(_host);

        // Duas chamadas encadeadas: a segunda devolve exatamente o que a primeira não mostrou (§9.3).
        var synced = await PostRoutes.SendAsync(owner, HttpMethod.Get,
            $"/api/containers/{slug}/feed?since={cursor}");
        Assert.Equal(HttpStatusCode.OK, synced.StatusCode);
        var syncedItems = FeedHarness.Items(await FeedHarness.JsonOfAsync(synced));
        Assert.Equal(new[] { "p4" }, FeedHarness.Contents(syncedItems));
        Assert.True(FeedHarness.Seqs(syncedItems)[0] > cursor);

        // A partir do meio da página: exatamente seq > since, sem duplicata, na ordem de seq.
        var since = seqs[1]; // "p2" — "p1" must NOT come back, "p3" and "p4" must, in seq order
        var deltaPath = $"/api/containers/{slug}/feed?since={since}";
        var delta = await PostRoutes.SendAsync(owner, HttpMethod.Get, deltaPath);
        Assert.Equal(HttpStatusCode.OK, delta.StatusCode);
        var deltaBody = await FeedHarness.JsonOfAsync(delta);
        var deltaSeqs = FeedHarness.Seqs(FeedHarness.Items(deltaBody));
        Assert.Equal(new[] { "p3", "p4" }, FeedHarness.Contents(FeedHarness.Items(deltaBody)));
        for (var i = 1; i < deltaSeqs.Count; i++)
        {
            Assert.True(deltaSeqs[i - 1] < deltaSeqs[i], "a delta answers in seq order (§9.3)");
        }

        Assert.All(deltaSeqs, value => Assert.True(value > since));
        Assert.Equal(deltaSeqs.Count, deltaSeqs.Distinct().Count()); // sem duplicata

        // A chamada repetida é idempotente: o mesmo delta, byte a byte.
        var again = await PostRoutes.SendAsync(owner, HttpMethod.Get, deltaPath);
        Assert.Equal(await FeedHarness.BytesOf(delta), await FeedHarness.BytesOf(again));

        // since fora dos dados devolve lista vazia, não erro (gap: faixa do ?since=).
        var beyond = await PostRoutes.SendAsync(owner, HttpMethod.Get,
            $"/api/containers/{slug}/feed?since={seqs[0] + 1000}");
        Assert.Equal(HttpStatusCode.OK, beyond.StatusCode);
        Assert.Equal(0, FeedHarness.Items(await FeedHarness.JsonOfAsync(beyond)).GetArrayLength());

        // Cursor malformado é o 400 validation_failed do catálogo (§8), nunca um 500.
        var malformed = await PostRoutes.SendAsync(owner, HttpMethod.Get,
            $"/api/containers/{slug}/feed?since=abc");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Contains("validation_failed",
            await malformed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Página default 20, teto 100 (contrato §1): limit=1 leva a linha mais nova e diz que há mais.
        var limited = await PostRoutes.SendAsync(owner, HttpMethod.Get,
            $"/api/containers/{slug}/feed?limit=1");
        var limitedBody = await FeedHarness.JsonOfAsync(limited);
        Assert.Equal(new[] { "p4" }, FeedHarness.Contents(FeedHarness.Items(limitedBody)));
        Assert.True(limitedBody.GetProperty("hasMore").GetBoolean());
    }

    [Fact] // AC-02
    public async Task Ac02_a_tighter_policy_answers_the_404_of_a_missing_slug_to_who_lost_access()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "fac02dono");
        var (reader, _) = await PostRoutes.SignedInAsync(_host, "fac02leitor");
        var slug = await NewContainerAsync(owner, "community"); // nasce Public
        await PublishAsync(owner, slug, "aberto");
        await PublishAsync(owner, slug, "para todos");
        await FeedHarness.ProjectPendingAsync(_host);

        // Um não-membro lê o feed público (visibilidade corrente admitiu a leitura).
        var open = await PostRoutes.SendAsync(reader, HttpMethod.Get, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        Assert.Equal(2, FeedHarness.Items(await FeedHarness.JsonOfAsync(open)).GetArrayLength());

        // Tornar a política mais restritiva muda a LEITURA: a visibilidade vigente vence o congelado.
        var version = await PolicyVersionAsync(owner, slug);
        var tightened = await PostRoutes.SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/policy", new { visibility = "membersonly" }, owner.Csrf,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, tightened.StatusCode);

        // Quem perdeu acesso recebe 404 byte-idêntico ao contêiner inexistente (SC-005), mesmo
        // tendo lido o feed antes.
        var hidden = await PostRoutes.SendAsync(reader, HttpMethod.Get, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var absentSlug = PostRoutes.Slug("fac02ausente");
        var absent = await PostRoutes.SendAsync(reader, HttpMethod.Get,
            $"/api/containers/{absentSlug}/feed");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await FeedHarness.BytesOf(absent), await FeedHarness.BytesOf(hidden));

        // Private responde igual (AC-02: Private e MembersOnly para não-membro).
        version = await PolicyVersionAsync(owner, slug);
        tightened = await PostRoutes.SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/policy", new { visibility = "private" }, owner.Csrf,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, tightened.StatusCode);
        var privada = await PostRoutes.SendAsync(reader, HttpMethod.Get, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.NotFound, privada.StatusCode);
        Assert.Equal(await FeedHarness.BytesOf(absent), await FeedHarness.BytesOf(privada));

        // E nada dos dois estados entra na timeline de quem nunca foi membro.
        var timeline = await PostRoutes.SendAsync(reader, HttpMethod.Get, "/api/timeline");
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
        var readerItems = FeedHarness.Items(await FeedHarness.JsonOfAsync(timeline));
        Assert.DoesNotContain(FeedHarness.ContainerIds(readerItems),
            id => id == $"{PostRoutes.Origin(_host)}/c/{slug}");

        // O membro dono continua lendo: a restrição atinge quem perdeu acesso, não quem o tem.
        var ownerFeed = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, ownerFeed.StatusCode);
        Assert.Equal(2, FeedHarness.Items(await FeedHarness.JsonOfAsync(ownerFeed)).GetArrayLength());
    }

    [Fact] // AC-03
    public async Task Ac03_timeline_serves_only_my_active_containers_and_entry_and_exit_move_it()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "fac03dono");
        var (member, _) = await PostRoutes.SignedInAsync(_host, "fac03membro");
        var slugA = await NewContainerAsync(owner, "community");
        var slugB = await NewContainerAsync(owner, "community");
        await JoinAsync(member, slugA);
        await JoinAsync(member, slugB);
        await PublishAsync(owner, slugA, "a1");
        await PublishAsync(owner, slugB, "b1");
        await FeedHarness.ProjectPendingAsync(_host);

        // A timeline é pessoal: sem sessão ela nem existe (401, contrato §8).
        var anonymous = _host.NewClient();
        var noSession = await PostRoutes.SendAsync(anonymous, HttpMethod.Get, "/api/timeline");
        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        Assert.Contains("not_authenticated",
            await noSession.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Os dois contêineres em que sou membro Active, na página mais-recente-primeiro.
        var first = await PostRoutes.SendAsync(member, HttpMethod.Get, "/api/timeline");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = await FeedHarness.JsonOfAsync(first);
        var items = FeedHarness.Items(page);
        Assert.Equal(new[] { "b1", "a1" }, FeedHarness.Contents(items));
        var containerA = $"{PostRoutes.Origin(_host)}/c/{slugA}";
        var containerB = $"{PostRoutes.Origin(_host)}/c/{slugB}";
        Assert.All(FeedHarness.ContainerIds(items), id => Assert.True(id == containerA || id == containerB));
        var pageSeqs = FeedHarness.Seqs(items);
        for (var i = 1; i < pageSeqs.Count; i++)
        {
            Assert.True(pageSeqs[i - 1] > pageSeqs[i], "the timeline page is newest first too");
        }

        // Mesma semântica de delta, e a ordem de seq atravessa contêineres (seq global, GAP-23).
        var cursor = page.GetProperty("nextSince").GetInt64();
        await PublishAsync(owner, slugA, "a2");
        await PublishAsync(owner, slugB, "b2");
        await FeedHarness.ProjectPendingAsync(_host);

        var deltaPath = "/api/timeline?since=" + cursor;
        var delta = await PostRoutes.SendAsync(member, HttpMethod.Get, deltaPath);
        Assert.Equal(HttpStatusCode.OK, delta.StatusCode);
        var deltaBody = await FeedHarness.JsonOfAsync(delta);
        var deltaItems = FeedHarness.Items(deltaBody);
        Assert.Equal(new[] { "a2", "b2" }, FeedHarness.Contents(deltaItems));
        var deltaSeqs = FeedHarness.Seqs(deltaItems);
        for (var i = 1; i < deltaSeqs.Count; i++)
        {
            Assert.True(deltaSeqs[i - 1] < deltaSeqs[i], "the delta answers in seq order (§9.3)");
        }

        Assert.All(deltaSeqs, value => Assert.True(value > cursor));
        var again = await PostRoutes.SendAsync(member, HttpMethod.Get, deltaPath);
        Assert.Equal(await FeedHarness.BytesOf(delta), await FeedHarness.BytesOf(again));

        // SAIR muda o que aparece: os itens do contêiner somem da timeline mesmo depois de vistos
        // (membership corrente, FR-026) — e os do outro contêiner ficam.
        var left = await PostRoutes.SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slugA}/leave", null, member.Csrf);
        Assert.True(left.IsSuccessStatusCode, $"leave failed: {left.StatusCode}");

        var afterLeave = await PostRoutes.SendAsync(member, HttpMethod.Get, "/api/timeline");
        var leftItems = FeedHarness.Items(await FeedHarness.JsonOfAsync(afterLeave));
        Assert.DoesNotContain(FeedHarness.ContainerIds(leftItems), id => id == containerA);
        Assert.Contains(FeedHarness.Contents(leftItems), text => text == "b1");
        Assert.Contains(FeedHarness.Contents(leftItems), text => text == "b2");

        // ENTRAR muda o que aparece: de volta ao contêiner, os itens dele voltam para a timeline.
        var rejoined = await PostRoutes.SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slugA}/join", null, member.Csrf);
        Assert.True(rejoined.IsSuccessStatusCode, $"join failed: {rejoined.StatusCode}");

        var afterEntry = await PostRoutes.SendAsync(member, HttpMethod.Get, "/api/timeline");
        var entryItems = FeedHarness.Items(await FeedHarness.JsonOfAsync(afterEntry));
        Assert.Contains(FeedHarness.ContainerIds(entryItems), id => id == containerA);
        Assert.Contains(FeedHarness.Contents(entryItems), text => text == "a1");
        Assert.Contains(FeedHarness.Contents(entryItems), text => text == "a2");
    }

    [Fact] // AC-04 — o DoD do Sprint 3: unlisted não aparece para não-membro
    public async Task Ac04_unlisted_container_is_404_for_non_members_and_never_reaches_a_third_party_timeline()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "fac04dono");
        var (third, _) = await PostRoutes.SignedInAsync(_host, "fac04terceiro");
        var slug = await NewContainerAsync(owner, "community");
        await PublishAsync(owner, slug, "unlisted um");
        await PublishAsync(owner, slug, "unlisted dois");

        var version = await PolicyVersionAsync(owner, slug);
        var moved = await PostRoutes.SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/policy", new { visibility = "unlisted" }, owner.Csrf,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        await FeedHarness.ProjectPendingAsync(_host);

        // Não-público nesta fase: feed para não-membro é o 404 idêntico ao inexistente.
        var hidden = await PostRoutes.SendAsync(third, HttpMethod.Get, $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var absent = await PostRoutes.SendAsync(third, HttpMethod.Get,
            $"/api/containers/{PostRoutes.Slug("fac04ausente")}/feed");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await FeedHarness.BytesOf(absent), await FeedHarness.BytesOf(hidden));

        // E o item do terceiro que tem timeline própria nunca carrega os itens do unlisted.
        var ownSlug = await NewContainerAsync(third, "community");
        await PublishAsync(third, ownSlug, "do terceiro");
        await FeedHarness.ProjectPendingAsync(_host);

        var timeline = await PostRoutes.SendAsync(third, HttpMethod.Get, "/api/timeline");
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
        var items = FeedHarness.Items(await FeedHarness.JsonOfAsync(timeline));
        Assert.Contains(FeedHarness.Contents(items), text => text == "do terceiro");
        Assert.DoesNotContain(FeedHarness.ContainerIds(items),
            id => id == $"{PostRoutes.Origin(_host)}/c/{slug}");
    }

    [Fact] // AC-05
    public async Task Ac05_a_banned_member_reads_nothing_and_a_silenced_member_reads_normally()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "fac05dono");
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "fac05membro");
        var slug = await NewContainerAsync(owner, "community");
        await JoinAsync(member, slug);
        await PublishAsync(owner, slug, "do clubinho");
        await FeedHarness.ProjectPendingAsync(_host);

        // Silenced lê normalmente: o mesmo feed que um membro Active lê.
        var silenced = await PostRoutes.SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{memberHandle}", new { state = "silence" }, owner.Csrf);
        Assert.True(silenced.IsSuccessStatusCode, $"silence failed: {silenced.StatusCode}");

        var readWhileSilenced = await PostRoutes.SendAsync(member, HttpMethod.Get,
            $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.OK, readWhileSilenced.StatusCode);
        Assert.Contains(FeedHarness.Contents(FeedHarness.Items(
            await FeedHarness.JsonOfAsync(readWhileSilenced))), text => text == "do clubinho");

        // Banned não lê: volta a Active primeiro (a §3.3 só ban a partir de Active), depois ban.
        var unsilenced = await PostRoutes.SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{memberHandle}", new { state = "unsilence" }, owner.Csrf);
        Assert.True(unsilenced.IsSuccessStatusCode, $"unsilence failed: {unsilenced.StatusCode}");
        var banned = await PostRoutes.SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}/members/{memberHandle}",
            new { state = "ban", justification = "cena feia no feed" }, owner.Csrf);
        Assert.True(banned.IsSuccessStatusCode, $"ban failed: {banned.StatusCode}");

        var readWhileBanned = await PostRoutes.SendAsync(member, HttpMethod.Get,
            $"/api/containers/{slug}/feed");
        Assert.Equal(HttpStatusCode.NotFound, readWhileBanned.StatusCode);
        var absent = await PostRoutes.SendAsync(member, HttpMethod.Get,
            $"/api/containers/{PostRoutes.Slug("fac05ausente")}/feed");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await FeedHarness.BytesOf(absent), await FeedHarness.BytesOf(readWhileBanned));

        // E o que ele chegou a ver some da timeline pessoal: a membership lida é a corrente.
        var timeline = await PostRoutes.SendAsync(member, HttpMethod.Get, "/api/timeline");
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
        Assert.DoesNotContain(
            FeedHarness.ContainerIds(FeedHarness.Items(await FeedHarness.JsonOfAsync(timeline))),
            id => id == $"{PostRoutes.Origin(_host)}/c/{slug}");
    }

    /// <summary>One container of <paramref name="kind"/>, created by its owner.</summary>
    private static async Task<string> NewContainerAsync(AuthClient owner, string kind)
    {
        var slug = PostRoutes.Slug("facfeed");
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind, name = slug, slug }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return slug;
    }

    /// <summary>Publishes one text and pins the 201, so a scenario never reads a projection of a post that failed.</summary>
    private static async Task PublishAsync(AuthClient author, string slug, string content)
    {
        var published = await PostRoutes.SendAsync(author, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content }, author.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
    }

    /// <summary>Joins an open community: membership Active from the entry route (SPEC-0008 F).</summary>
    private static async Task JoinAsync(AuthClient joiner, string slug)
    {
        var joined = await PostRoutes.SendAsync(joiner, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, joiner.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");
    }

    /// <summary>The policy version a PUT must answer with (If-Match → 409 otherwise).</summary>
    private static async Task<int> PolicyVersionAsync(AuthClient reader, string slug)
    {
        var response = await PostRoutes.SendAsync(reader, HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await FeedHarness.JsonOfAsync(response);
        return body.GetProperty("policy").GetProperty("version").GetInt32();
    }
}
