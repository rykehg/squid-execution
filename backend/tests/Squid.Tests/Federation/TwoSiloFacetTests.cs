using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Squid.Api.Federation;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Feed;
using Squid.Tests.Media;
using Squid.Tests.Posts;

namespace Squid.Tests.Federation;

/// <summary>
/// TASK-0047 — the closing facet of the two-silo DoD (contract §7 items 1–9, 14, 15): two Squid
/// silos in ONE process, two databases, two blob volumes, every hop of the chain over REAL HTTP —
/// the production <c>HttpPeerSender</c> posts, the production <c>HttpFederatedActorClient</c>
/// resolves keys and senders from the other silo's actor documents, and only the pump's clock is
/// the suite's own hand. Tests-only: a product gap the facet proves becomes a doubt for ODEV,
/// never a fix outside the <c>touches</c>.
/// </summary>
[Collection("ServiceHost")]
public sealed class TwoSiloFacetTests : IClassFixture<TwoSilosFixture>
{
    private readonly TwoSilosFixture _fixture;

    public TwoSiloFacetTests(TwoSilosFixture fixture) => _fixture = fixture;

    private TwoSilos Pair => _fixture.Silos;

    [Fact] // harness self-check (AC-08's premise: two silos, distinct bases)
    public async Task Two_silos_live_in_one_process_with_their_own_origins_and_databases()
    {
        Assert.Equal(HttpStatusCode.OK, (await Pair.A.Http.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Pair.B.Http.GetAsync("/health")).StatusCode);
        Assert.NotEqual(Pair.A.Origin, Pair.B.Origin);
        Assert.NotEqual(Pair.A.StoragePath, Pair.B.StoragePath);
        Assert.NotEqual(Pair.A.BlobRoot, Pair.B.BlobRoot);

        // State isolation: an account born on B does not exist on A — no grain ever crossed silos.
        var (_, handle) = await TwoSiloHarness.RegisterAsync(Pair.B, "iso");
        Assert.Equal(HttpStatusCode.OK, (await Pair.B.Http.GetAsync($"{Pair.B.Origin}/users/{handle}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Pair.A.Http.GetAsync($"{Pair.A.Origin}/users/{handle}")).StatusCode);
    }

    [Fact] // AC-01 (§7.1/SC-007) — autor, data, conteúdo E mídia
    public async Task Ac01_public_post_of_a_lands_on_b_with_the_author_date_content_and_media()
    {
        var (owner, alice, _, _, slug) = await ChainAsync("f01");

        // A mídia nasce na A: um upload real cujo contentHash é o que o post vai anexar.
        var upload = await MediaHarness.UploadAsync(owner, MediaHarness.JpegWithExif(), "foto da A para a B");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var hash = uploaded.RootElement.GetProperty("contentHash").GetString()!;

        var postId = await TwoSiloHarness.PublishAsync(owner, slug, "publicado da A para a B", hash);

        // Enqueued to B's follower, then the production sender really POSTs it (FR-036 + §5).
        var row = Assert.Single(await Pair.A.Deliveries.ForObjectAsync(postId, PeerActivity.Create));
        Assert.Equal(PeerDeliveryState.Pending, row.State);
        await Pair.A.Pump.RunOnceAsync();
        row = Assert.Single(await Pair.A.Deliveries.ForObjectAsync(postId, PeerActivity.Create));
        Assert.Equal(PeerDeliveryState.Sent, row.State);

        // B applied it: the post exists on B, addressed to the destination the payload named.
        await TwoSiloHarness.ProjectAsync(Pair.B);
        var item = TwoSiloHarness.ItemOf(await TwoSiloHarness.FeedAsync(Pair.B, slug), postId);
        Assert.Equal(JsonValueKind.Object, item.ValueKind);
        Assert.Equal($"{Pair.A.Origin}/users/{alice}", item.GetProperty("author").GetString());

        // §7.1: author, date and content of A as B serves them — one rendering of the same row.
        var docA = await TwoSiloHarness.GetAsync(Pair.A, $"/objects/{Uri.EscapeDataString(postId)}");
        var docB = await TwoSiloHarness.GetAsync(Pair.B, $"/objects/{Uri.EscapeDataString(postId)}");
        Assert.Equal($"{Pair.A.Origin}/users/{alice}", docA.GetProperty("attributedTo").GetString());
        Assert.Equal(docA.GetProperty("attributedTo").GetString(), docB.GetProperty("attributedTo").GetString());
        Assert.Equal(docA.GetProperty("published").GetString(), docB.GetProperty("published").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", docB.GetProperty("published").GetString());
        Assert.Equal("publicado da A para a B", docB.GetProperty("content").GetString());

        // §7.1 mídia: as duas renderizações trazem o attachment, cada uma com a rota de contrato
        // da própria origem — nunca a /api/media de sessão, que responderia 401 ao par.
        var blobA = Assert.Single(docA.GetProperty("attachment").EnumerateArray());
        var blobB = Assert.Single(docB.GetProperty("attachment").EnumerateArray());
        Assert.Equal(hash, blobA.GetProperty("sha256").GetString());
        Assert.Equal($"{Pair.A.Origin}/media/{hash}", blobA.GetProperty("url").GetString());
        Assert.Equal(hash, blobB.GetProperty("sha256").GetString());
        Assert.Equal($"{Pair.B.Origin}/media/{hash}", blobB.GetProperty("url").GetString());
        Assert.Equal("image/jpeg", blobB.GetProperty("mediaType").GetString());

        // O blob baixado da A pela própria url que o documento declara, sem sessão nenhuma
        // (Pair.A.Http não carrega jar de cookies): os bytes conferem com o hash do anexo.
        using var fromA = await Pair.A.Http.GetAsync(blobA.GetProperty("url").GetString()!);
        Assert.Equal(HttpStatusCode.OK, fromA.StatusCode);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(await fromA.Content.ReadAsByteArrayAsync())));

        // …e a B serve a cópia que o inbound baixou e verificou pelo SHA-256 antes de gravar
        // (GAP-44) — volume próprio do silo, nunca um redirecionamento para a A.
        using var fromB = await Pair.B.Http.GetAsync(blobB.GetProperty("url").GetString()!);
        Assert.Equal(HttpStatusCode.OK, fromB.StatusCode);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(await fromB.Content.ReadAsByteArrayAsync())));
    }

    [Fact] // AC-02 (§7.2/SC-005)
    public async Task Ac02_members_only_never_reaches_a_non_member_follower_of_b()
    {
        var (owner, _, _, _, slug) = await ChainAsync("f02m"); // bob segue alice, mas não é membro
        await TwoSiloHarness.VisibilityAsync(owner, slug, "membersOnly", "\"1\"");
        var postId = await TwoSiloHarness.PublishAsync(owner, slug, "so para membros ativos da A");

        // §7.2: a non-member follower is not even enqueued — nothing to send, nothing to cancel.
        Assert.Empty(await Pair.A.Deliveries.ForObjectAsync(postId, PeerActivity.Create));
        await Pair.A.Pump.RunOnceAsync();
        await Pair.B.Pump.RunOnceAsync();
        await TwoSiloHarness.ProjectAsync(Pair.B);
        Assert.Equal(JsonValueKind.Undefined,
            TwoSiloHarness.ItemOf(await TwoSiloHarness.FeedAsync(Pair.B, slug), postId).ValueKind);
    }

    [Fact] // AC-02 (§7.3)
    public async Task Ac02_private_generates_no_delivery_at_all()
    {
        var (owner, _, _, _, slug) = await ChainAsync("f02p");
        await TwoSiloHarness.VisibilityAsync(owner, slug, "private", "\"1\"");
        var postId = await TwoSiloHarness.PublishAsync(owner, slug, "conversa privada nao federa");

        Assert.Empty(await Pair.A.Deliveries.ForObjectAsync(postId, PeerActivity.Create)); // §7.3: nenhuma entrega
        await Pair.A.Pump.RunOnceAsync();
        await TwoSiloHarness.ProjectAsync(Pair.B);
        Assert.Equal(JsonValueKind.Undefined,
            TwoSiloHarness.ItemOf(await TwoSiloHarness.FeedAsync(Pair.B, slug), postId).ValueKind);
    }

    [Fact] // AC-03 (§7.4 + §7.5)
    public async Task Ac03_duplicate_delivery_bad_signature_and_divergent_digest_leave_b_intact()
    {
        var (owner, alice, bob, _, slug) = await ChainAsync("f03");
        var postId = await TwoSiloHarness.PublishAsync(owner, slug, "entrega repetida nao duplica");
        await Pair.A.Pump.RunOnceAsync();
        var delivered = Assert.Single(await Pair.A.Deliveries.ForObjectAsync(postId, PeerActivity.Create));
        Assert.Equal(PeerDeliveryState.Sent, delivered.State);
        var payload = delivered.Payload;
        var inbox = $"/users/{bob}/inbox";

        await TwoSiloHarness.ProjectAsync(Pair.B);
        var logAfterFirst = await TwoSiloHarness.LogCountAsync(Pair.B);
        var feedAfterFirst = (await TwoSiloHarness.FeedAsync(Pair.B, slug)).GetRawText();
        var outboxAfterFirst = (await TwoSiloHarness.GetAsync(Pair.B, $"/c/{slug}/outbox?page=1")).GetRawText();

        // §7.4: the SAME activity.id delivered again — 202, zero reprocessing, state byte-identical.
        var repeat = await TwoSiloHarness.SendSignedAsAsync(
            Pair.A, alice, Pair.B.Http, inbox, payload);
        Assert.Equal(HttpStatusCode.Accepted, repeat.StatusCode);
        Assert.Equal(logAfterFirst, await TwoSiloHarness.LogCountAsync(Pair.B));
        Assert.Equal(feedAfterFirst, (await TwoSiloHarness.FeedAsync(Pair.B, slug)).GetRawText());
        Assert.Equal(outboxAfterFirst, (await TwoSiloHarness.GetAsync(Pair.B, $"/c/{slug}/outbox?page=1")).GetRawText());

        // §7.5: same keyId, another key — 401, and the receiver's state stays intact.
        using var impostor = RSA.Create(2048);
        var forged = await TwoSiloHarness.SendSignedAsync(
            Pair.B.Http, inbox, payload,
            data => impostor.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            $"{Pair.A.Origin}/users/{alice}#main-key");
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(logAfterFirst, await TwoSiloHarness.LogCountAsync(Pair.B));
        Assert.Equal(feedAfterFirst, (await TwoSiloHarness.FeedAsync(Pair.B, slug)).GetRawText());

        // §7.5: signed body ≠ sent body — 401 on the Digest, again with the state intact.
        var tampered = await TwoSiloHarness.SendSignedAsAsync(
            Pair.A, alice, Pair.B.Http, inbox, payload, sentBody: payload + " ");
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        Assert.Contains("digest", await tampered.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(logAfterFirst, await TwoSiloHarness.LogCountAsync(Pair.B));
        Assert.Equal(feedAfterFirst, (await TwoSiloHarness.FeedAsync(Pair.B, slug)).GetRawText());
    }

    [Fact] // AC-04 (§7.6) — this fact stops B, so it owns its pair
    public async Task Ac04_with_b_down_the_publish_succeeds_and_the_row_stays_pending_with_backoff()
    {
        await using var own = await TwoSilos.StartAsync();
        var slug = PostRoutes.Slug("f04a");
        var (owner, alice, _) = await TwoSiloHarness.CommunityAsync(own.A, "f04a", slug);
        var (bobClient, bob, _) = await TwoSiloHarness.CommunityAsync(own.B, "f04ab", slug);
        await TwoSiloHarness.FollowAsync(own.B, bobClient, $"{own.A.Origin}/users/{alice}");
        await own.B.StopAsync(); // §7.6: B indisponível

        var postId = await TwoSiloHarness.PublishAsync(owner, slug, "b caiu no meio da entrega");
        Assert.Equal(HttpStatusCode.OK, (await own.A.Http.GetAsync($"/api/posts/{Uri.EscapeDataString(postId)}")).StatusCode);

        Assert.Equal(1, await own.A.Pump.RunOnceAsync());
        var row = Assert.Single(await own.A.Deliveries.ForObjectAsync(postId, PeerActivity.Create));
        Assert.Equal(PeerDeliveryState.Pending, row.State);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("send_failed", row.Reason);
        Assert.True(row.NextAttemptAt > DateTimeOffset.UtcNow, "o reagendamento é o backoff do motor, nunca na hora");

        var stored = await own.A.Deliveries.FindAsync(row.ActivityId, row.RecipientActorId);
        Assert.NotNull(stored);
        Assert.Equal(PeerDeliveryState.Pending, stored!.State);
        Assert.Equal(1, stored.Attempts);
        Assert.True(stored.NextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact] // AC-04 (§7.7)
    public async Task Ac04_policy_changed_between_enqueue_and_send_cancels_and_b_receives_nothing()
    {
        var (owner, _, _, _, slug) = await ChainAsync("f04b");
        var postId = await TwoSiloHarness.PublishAsync(owner, slug, "enfileirado antes da troca");
        var row = Assert.Single(await Pair.A.Deliveries.ForObjectAsync(postId, PeerActivity.Create));
        Assert.Equal(PeerDeliveryState.Pending, row.State);

        // The policy changes BETWEEN enqueue and send (R-05/FR-038).
        await TwoSiloHarness.VisibilityAsync(owner, slug, "membersOnly", "\"1\"");
        await Pair.A.Pump.RunOnceAsync();

        var stored = await Pair.A.Deliveries.FindAsync(row.ActivityId, row.RecipientActorId);
        Assert.NotNull(stored);
        Assert.Equal(PeerDeliveryState.Cancelled, stored!.State); // reavaliada: Cancelled, nunca enviada
        Assert.Contains("política vigente", stored.Reason, StringComparison.Ordinal);

        await TwoSiloHarness.ProjectAsync(Pair.B);
        Assert.Equal(JsonValueKind.Undefined,
            TwoSiloHarness.ItemOf(await TwoSiloHarness.FeedAsync(Pair.B, slug), postId).ValueKind);
    }

    [Fact] // AC-05 (§7.8)
    public async Task Ac05_oversized_and_too_deep_payloads_are_refused_and_b_keeps_serving()
    {
        var (_, alice) = await TwoSiloHarness.RegisterAsync(Pair.A, "f05a");
        var (_, bob) = await TwoSiloHarness.RegisterAsync(Pair.B, "f05b");
        var inbox = $"/users/{bob}/inbox";
        var logBefore = await TwoSiloHarness.LogCountAsync(Pair.B);
        var actor = $"{Pair.A.Origin}/users/{alice}";

        // 5 MB, assinado com a chave que A publica: o passo 4 do §3 responde 413.
        var padding = new string('x', 5 * 1024 * 1024);
        var huge = $$"""
            {"@context":"{{InboxHarness.ActivityStreams}}","id":"{{Pair.A.Origin}}/activities/{{InboxHarness.Unique("f05h")}}","type":"Create","actor":"{{actor}}","object":{"type":"Note","content":"enorme"},"padrao":"{{padding}}"}
            """;
        var oversized = await TwoSiloHarness.SendSignedAsAsync(Pair.A, alice, Pair.B.Http, inbox, huge);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);

        // Aninhamento de 100 níveis, também assinado: 400, sempre pelo mesmo caminho do §3.
        var nested = new string('[', 100) + "1" + new string(']', 100);
        var deep = $$"""
            {"@context":"{{InboxHarness.ActivityStreams}}","id":"{{Pair.A.Origin}}/activities/{{InboxHarness.Unique("f05d")}}","type":"Create","actor":"{{actor}}","x":{{nested}}}
            """;
        var tooDeep = await TwoSiloHarness.SendSignedAsAsync(Pair.A, alice, Pair.B.Http, inbox, deep);
        Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);

        // §7.8: nothing applied, and the service of B keeps answering.
        Assert.Equal(logBefore, await TwoSiloHarness.LogCountAsync(Pair.B));
        Assert.Equal(HttpStatusCode.OK, (await Pair.B.Http.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Pair.B.Http.GetAsync(Webfinger(bob))).StatusCode);
    }

    [Fact] // AC-05 (§7.9)
    public async Task Ac05_webfinger_of_an_unknown_handle_is_404_without_revealing_similar_actors()
    {
        var (_, known) = await TwoSiloHarness.RegisterAsync(Pair.B, "f09");
        Assert.Equal(HttpStatusCode.OK,
            (await Pair.B.Http.GetAsync(Webfinger(known))).StatusCode); // o serviço responde

        // Um nome colado no que existe: mesmo envelope 404 de toda recusa, sem ator parecido dentro.
        var unknown = known[..^1];
        using var response = await Pair.B.Http.GetAsync(Webfinger(unknown));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(known, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unknown, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subject", body, StringComparison.OrdinalIgnoreCase); // nunca um JRD parcial
    }

    [Fact] // AC-06 (§7.10/SC-015)
    public async Task Ac06_every_send_carries_the_peer_domain_trace_attribute_without_pii()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FederationTrace.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var (owner, alice, bob, _, slug) = await ChainAsync("f06"); // o Follow de B→A já deixa span
        var postId = await TwoSiloHarness.PublishAsync(owner, slug, "rastro peer.domain");
        await Pair.A.Pump.RunOnceAsync(); // e o Create de A→B

        var sends = spans.Where(span => span.OperationName == "peer.send").ToList();
        Assert.True(sends.Count >= 2, $"as duas entregas da cadeia devem sair assinadas: {sends.Count}");
        Assert.All(sends, span =>
        {
            var domain = (string?)span.GetTagItem("peer.domain");
            Assert.NotNull(domain);
            Assert.Equal("127.0.0.1", domain!);     // o domínio do par, sem porta e sem caminho
            Assert.DoesNotContain(":", domain!, StringComparison.Ordinal);
            Assert.DoesNotContain("users", domain!, StringComparison.Ordinal);
            Assert.DoesNotContain(alice, domain!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(bob, domain!, StringComparison.OrdinalIgnoreCase);
        });

        var delivered = Assert.Single(await Pair.A.Deliveries.ForObjectAsync(postId, PeerActivity.Create));
        Assert.Equal(PeerDeliveryState.Sent, delivered.State);
    }

    [Fact] // AC-07 (§7.14)
    public async Task Ac07_a_repeated_like_from_the_same_actor_keeps_the_count_at_one()
    {
        var (owner, _, bob, _, slug) = await ChainAsync("f07l");
        var postId = await TwoSiloHarness.PublishAsync(owner, slug, "post para curtir de fora");
        await Pair.A.Pump.RunOnceAsync(); // a cadeia inteira roda antes da reação
        await TwoSiloHarness.ProjectAsync(Pair.A); // o like corrige a coluna da projeção já projetada

        var first = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "{{Pair.B.Origin}}/activities/{{InboxHarness.Unique("f07l")}}",
              "type": "Like",
              "actor": "{{Pair.B.Origin}}/users/{{bob}}",
              "object": "{{postId}}"
            }
            """;
        var sent = await TwoSiloHarness.SendSignedAsAsync(
            Pair.B, bob, Pair.A.Http, $"/c/{slug}/inbox", first);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        Assert.Equal(1, await LikeCountAsync(Pair.A, slug, postId));

        // Outro activity.id, mesmo par (ator, objeto): §7.14 — a contagem não duplica.
        var repeated = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "{{Pair.B.Origin}}/activities/{{InboxHarness.Unique("f07l")}}",
              "type": "Like",
              "actor": "{{Pair.B.Origin}}/users/{{bob}}",
              "object": "{{postId}}"
            }
            """;
        var again = await TwoSiloHarness.SendSignedAsAsync(
            Pair.B, bob, Pair.A.Http, $"/c/{slug}/inbox", repeated);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(1, await LikeCountAsync(Pair.A, slug, postId));
    }

    [Fact] // AC-07 (§7.15, R-11)
    public async Task Ac07_federated_reply_preserves_inreplyto_and_a_private_conversation_sends_nothing()
    {
        var (owner, _, _, _, slug) = await ChainAsync("f07r");
        var parentId = await TwoSiloHarness.PublishAsync(owner, slug, "pai da conversa federada");
        await Pair.A.Pump.RunOnceAsync();
        await TwoSiloHarness.ProjectAsync(Pair.B);

        var replyId = await TwoSiloHarness.PublishReplyAsync(owner, parentId, "resposta federada da A");
        await Pair.A.Pump.RunOnceAsync();
        await TwoSiloHarness.ProjectAsync(Pair.B);

        var item = TwoSiloHarness.ItemOf(await TwoSiloHarness.FeedAsync(Pair.B, slug), replyId);
        Assert.Equal(JsonValueKind.Object, item.ValueKind);
        Assert.Equal(parentId, item.GetProperty("replyToId").GetString()); // §7.15: preservado byte a byte

        // Conversa privada: nem uma linha de entrega sai da fila (§7.15/R-11).
        await TwoSiloHarness.VisibilityAsync(owner, slug, "private", "\"1\"");
        var privateReply = await TwoSiloHarness.PublishReplyAsync(owner, parentId, "aqui ninguem de fora le");
        Assert.Empty(await Pair.A.Deliveries.ForObjectAsync(privateReply, PeerActivity.Create));
        await Pair.A.Pump.RunOnceAsync();
        await TwoSiloHarness.ProjectAsync(Pair.B);
        Assert.Equal(JsonValueKind.Undefined,
            TwoSiloHarness.ItemOf(await TwoSiloHarness.FeedAsync(Pair.B, slug), privateReply).ValueKind);
    }

    // ---- harness --------------------------------------------------------------------------

    /// <summary>The full hand-shake every fan-out fact needs: A's community, B's community of the SAME slug, B following A over the wire.</summary>
    private async Task<(AuthClient Owner, string Alice, string Bob, string BobActor, string Slug)> ChainAsync(string prefix)
    {
        var slug = PostRoutes.Slug(prefix);
        var (owner, alice, _) = await TwoSiloHarness.CommunityAsync(Pair.A, prefix, slug);
        var (bobClient, bob, _) = await TwoSiloHarness.CommunityAsync(Pair.B, prefix + "b", slug);
        var bobActor = $"{Pair.B.Origin}/users/{bob}";
        await TwoSiloHarness.FollowAsync(Pair.B, bobClient, $"{Pair.A.Origin}/users/{alice}");

        // The hand-shake really crossed the wire: A's followers collection already carries B's actor.
        var followers = await TwoSiloHarness.GetAsync(
            Pair.A, $"{Pair.A.Origin}/users/{alice}/followers?page=1");
        var seen = followers.GetProperty("orderedItems").EnumerateArray()
            .Select(item => item.GetString()).ToList();
        Assert.Contains(bobActor, seen);

        return (owner, alice, bob, bobActor, slug);
    }

    /// <summary>The likeCount a projection row serves for one post on one silo.</summary>
    private static async Task<long> LikeCountAsync(Silo silo, string slug, string postId)
    {
        foreach (var item in (await TwoSiloHarness.FeedAsync(silo, slug)).GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("id").GetString() == postId)
            {
                return item.GetProperty("likeCount").GetInt64();
            }
        }

        return -1; // o post nem está no feed: falha a asserção, nunca conta zero de verdade
    }

    /// <summary>The webfinger path of one handle at one silo's own authority.</summary>
    private string Webfinger(Silo silo, string handle) =>
        "/.well-known/webfinger?resource="
        + Uri.EscapeDataString($"acct:{handle}@{silo.Http.BaseAddress!.Authority}");

    private string Webfinger(string handle) => Webfinger(Pair.B, handle);
}
