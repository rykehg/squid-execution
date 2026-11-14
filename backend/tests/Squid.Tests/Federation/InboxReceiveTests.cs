using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Federation;
using Squid.Api.Media;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Feed;
using Squid.Tests.Posts;
using Squid.Tests.Unfurl;

namespace Squid.Tests.Federation;

/// <summary>
/// AC-01..AC-03 and AC-05..AC-07 of TASK-0044, proved over HTTP against the host
/// <see cref="SquidServiceHost"/> builds: a signed remote Create landing as a post in the
/// destination D-01 fixed, the id window answering a repeat without reprocessing, the §3 order
/// refusing each step with the state intact, Follow/Accept/Undo over the relation, Delete's
/// tombstone leaving the feed, the idempotent Like, the denunciation queue, the visibility gates
/// of AC-06 with the metric, and the guarded attachment download of AC-07. Every signature is
/// real draft-cavage over a key the fake resolver published for a remote actor — the production
/// pipeline runs end to end, only the sockets are fake.
/// </summary>
[Collection("ServiceHost")]
public sealed class InboxReceiveTests : IClassFixture<InboxHostFixture>
{
    private readonly InboxHostFixture _host;

    public InboxReceiveTests(InboxHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task A_remote_create_with_valid_headers_becomes_a_post_in_the_named_container()
    {
        var (_, ownerHandle, slug) = await InboxHarness.CommunityAsync(_host, "ac01");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "aliceac01");
        var objectUri = $"https://remote.example/o/{InboxHarness.Unique("ac01")}";
        var activity = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac01")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "to": ["{{InboxHarness.Public}}"],
              "object": {
                "id": "{{objectUri}}",
                "type": "Note",
                "attributedTo": "{{actor}}",
                "content": "<p>conteúdo federado de verdade</p>",
                "published": "2026-11-03T12:00:00Z",
                "to": ["{{InboxHarness.Public}}"],
                "audience": "{{_host.Origin}}/c/{{slug}}"
              }
            }
            """;

        var response = await InboxHarness.SendSignedAsync(
            _host.Http, $"/users/{ownerHandle}/inbox", activity, rsa, keyId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // O post existe com autor remoto, conteúdo sanitizado (GAP-24) e a data do par (§2.1).
        using var document = JsonDocument.Parse(
            await GetAsync($"/objects/{Uri.EscapeDataString(objectUri)}"));
        var note = document.RootElement;
        Assert.Equal("Note", note.GetProperty("type").GetString());
        Assert.Equal(actor, note.GetProperty("attributedTo").GetString());
        Assert.Equal("2026-11-03T12:00:00Z", note.GetProperty("published").GetString());
        Assert.Equal("&lt;p&gt;conteúdo federado de verdade&lt;/p&gt;", note.GetProperty("content").GetString());
        Assert.Equal(slug, note.GetProperty("squid:slug").GetString());

        // E o outbox do contêiner NOMEADO carrega o objeto — a prova de que o destino foi o certo.
        using var outbox = JsonDocument.Parse(await GetAsync($"/c/{slug}/outbox?page=1"));
        var items = outbox.RootElement.GetProperty("orderedItems").EnumerateArray().ToList();
        Assert.Equal(objectUri, Assert.Single(items).GetProperty("id").GetString());
    }

    [Fact] // AC-02
    public async Task The_same_activity_id_delivered_twice_is_accepted_without_reprocessing()
    {
        var (_, _, slug) = await InboxHarness.CommunityAsync(_host, "ac02");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "bobac02");
        var objectUri = $"https://remote.example/o/{InboxHarness.Unique("ac02")}";
        var activity = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac02")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "to": ["{{InboxHarness.Public}}"],
              "object": {
                "id": "{{objectUri}}",
                "type": "Note",
                "attributedTo": "{{actor}}",
                "content": "entrega repetida não duplica estado",
                "published": "2026-11-03T12:00:00Z",
                "to": ["{{InboxHarness.Public}}"]
              }
            }
            """;
        var inbox = $"/c/{slug}/inbox";

        var first = await InboxHarness.SendSignedAsync(_host.Http, inbox, activity, rsa, keyId);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        await InboxHarness.ProjectPendingAsync(_host);
        var logBefore = await InboxHarness.LogCountAsync(_host);
        var feedBefore = await FeedContentsAsync(slug);

        // A repetição: 202 idêntico byte a byte, nenhum evento novo, nenhuma projeção mudando.
        var second = await InboxHarness.SendSignedAsync(_host.Http, inbox, activity, rsa, keyId);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsByteArrayAsync(),
            await second.Content.ReadAsByteArrayAsync());
        Assert.Equal(logBefore, await InboxHarness.LogCountAsync(_host));

        await InboxHarness.ProjectPendingAsync(_host);
        Assert.Equal(feedBefore, await FeedContentsAsync(slug));

        using var outbox = JsonDocument.Parse(await GetAsync($"/c/{slug}/outbox?page=1"));
        Assert.Single(outbox.RootElement.GetProperty("orderedItems").EnumerateArray());
    }

    [Fact] // AC-03
    public async Task The_contract_order_refuses_each_step_with_the_state_intact()
    {
        var (_, _, slug) = await InboxHarness.CommunityAsync(_host, "ac03");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "carolac03");
        var inbox = $"/c/{slug}/inbox";
        var activity = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac03")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "object": { "id": "https://remote.example/o/{{InboxHarness.Unique("ac03")}}", "type": "Note", "content": "não aplico", "attributedTo": "{{actor}}" }
            }
            """;
        var logBefore = await InboxHarness.LogCountAsync(_host);

        // Passo 1: sem assinatura — 401, corpo único de recusa (a regra da TASK-0043 continua de pé).
        var unsigned = await InboxHarness.SendUnsignedAsync(_host.Http, inbox, activity);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);

        // Passo 2: Digest divergente — assinado sobre o corpo bom, entregue com outro corpo → 401.
        var tampered = await InboxHarness.SendSignedAsync(
            _host.Http, inbox, activity, rsa, keyId, sentBody: activity + " ");
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        Assert.Contains("digest", await tampered.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // Passo 3: Date fora da janela de ±5 min → 401.
        var stale = await InboxHarness.SendSignedAsync(
            _host.Http, inbox, activity, rsa, keyId, at: DateTimeOffset.UtcNow.AddMinutes(-10));
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);

        // Passo 4: corpo de 5 MB assinado → 413 (o limite do contrato é 1 MB).
        var padding = new string('x', 5 * 1024 * 1024);
        var huge = $$"""{"@context":"{{InboxHarness.ActivityStreams}}","id":"https://remote.example/activities/huge","type":"Create","actor":"{{actor}}","object":{"type":"Note","content":"enorme"},"padrao":"{{padding}}"}""";
        var oversized = await InboxHarness.SendSignedAsync(_host.Http, inbox, huge, rsa, keyId);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);

        // Passo 4: aninhamento de 100 níveis → 400.
        var nested = new string('[', 100) + "1" + new string(']', 100);
        var deep = $$"""{"@context":"{{InboxHarness.ActivityStreams}}","id":"https://remote.example/activities/deep","type":"Create","actor":"{{actor}}","x":{{nested}}}""";
        var tooDeep = await InboxHarness.SendSignedAsync(_host.Http, inbox, deep, rsa, keyId);
        Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);

        // Em todos: estado intacto e o serviço de pé.
        Assert.Equal(logBefore, await InboxHarness.LogCountAsync(_host));
        using (var outbox = JsonDocument.Parse(await GetAsync($"/c/{slug}/outbox?page=1")))
        {
            Assert.Empty(outbox.RootElement.GetProperty("orderedItems").EnumerateArray());
        }

        Assert.Equal(HttpStatusCode.OK, (await _host.Http.GetAsync("/health")).StatusCode);
    }

    [Fact] // AC-05 (Follow + Accept de volta + Undo)
    public async Task A_follow_creates_the_relation_answers_accept_and_undo_removes_it()
    {
        var (_, handle, _) = await InboxHarness.CommunityAsync(_host, "ac05f");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "daveac05");
        var localActor = $"{_host.Origin}/users/{handle}";
        var followId = $"https://remote.example/activities/{InboxHarness.Unique("ac05")}";
        var follow = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "{{followId}}",
              "type": "Follow",
              "actor": "{{actor}}",
              "object": "{{localActor}}"
            }
            """;

        var response = await InboxHarness.SendSignedAsync(
            _host.Http, $"/users/{handle}/inbox", follow, rsa, keyId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // AC-05: o Accept volta no corpo do 202, aceitando o Follow original verbatim.
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var accept = body.RootElement;
            Assert.Equal("Accept", accept.GetProperty("type").GetString());
            Assert.Equal(localActor, accept.GetProperty("actor").GetString());
            Assert.Equal(InboxHarness.ActivityStreams, accept.GetProperty("@context").GetString());
            Assert.StartsWith($"{_host.Origin}/activities/", accept.GetProperty("id").GetString());
            var original = accept.GetProperty("object");
            Assert.Equal("Follow", original.GetProperty("type").GetString());
            Assert.Equal(followId, original.GetProperty("id").GetString());
            Assert.Equal(actor, original.GetProperty("actor").GetString());
            Assert.Equal(localActor, original.GetProperty("object").GetString());
        }

        // E a relação aparece na coleção federada (a única superfície observável dela).
        Assert.Equal(new[] { actor }, await RelationAsync($"{_host.Origin}/users/{handle}/followers"));

        var undo = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac05")}}",
              "type": "Undo",
              "actor": "{{actor}}",
              "object": { "type": "Follow", "actor": "{{actor}}", "object": "{{localActor}}" }
            }
            """;
        var undone = await InboxHarness.SendSignedAsync(
            _host.Http, $"/users/{handle}/inbox", undo, rsa, keyId);
        Assert.Equal(HttpStatusCode.Accepted, undone.StatusCode);
        Assert.Empty(await RelationAsync($"{_host.Origin}/users/{handle}/followers"));
    }

    [Fact] // AC-05 (Accept de um Follow que esta instância enviou — a linha da TASK-0045)
    public async Task An_accept_settles_the_follow_this_instance_had_sent()
    {
        var (_, handle, _) = await InboxHarness.CommunityAsync(_host, "ac05a");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "eveac05");
        var follows = _host.App.Services.GetRequiredService<IFollowStore>();
        await follows.AddFollowingAsync(handle, actor, DateTimeOffset.UtcNow);
        Assert.Empty(await RelationAsync($"{_host.Origin}/users/{handle}/following")); // ainda pendente: não federada

        var accept = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac05")}}",
              "type": "Accept",
              "actor": "{{actor}}",
              "object": { "type": "Follow", "actor": "{{_host.Origin}}/users/{{handle}}", "object": "{{actor}}" }
            }
            """;
        var response = await InboxHarness.SendSignedAsync(
            _host.Http, $"/users/{handle}/inbox", accept, rsa, keyId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(new[] { actor }, await RelationAsync($"{_host.Origin}/users/{handle}/following"));
    }

    [Fact] // AC-05 (Delete vira tombstone e o post some do feed)
    public async Task A_remote_delete_tombstones_the_post_and_it_leaves_the_feed()
    {
        var (_, _, slug) = await InboxHarness.CommunityAsync(_host, "ac05d");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "frankac05");
        var objectUri = $"https://remote.example/o/{InboxHarness.Unique("ac05d")}";
        var create = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac05d")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "to": ["{{InboxHarness.Public}}"],
              "object": { "id": "{{objectUri}}", "type": "Note", "attributedTo": "{{actor}}", "content": "apagável", "to": ["{{InboxHarness.Public}}"] }
            }
            """;
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/c/{slug}/inbox", create, rsa, keyId)).StatusCode);

        await InboxHarness.ProjectPendingAsync(_host);
        Assert.Contains("apagável", await FeedContentsAsync(slug));
        Assert.Equal(HttpStatusCode.OK,
            (await _host.Http.GetAsync($"/objects/{Uri.EscapeDataString(objectUri)}")).StatusCode);

        var delete = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac05d")}}",
              "type": "Delete",
              "actor": "{{actor}}",
              "object": "{{objectUri}}"
            }
            """;
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/c/{slug}/inbox", delete, rsa, keyId)).StatusCode);

        // Tombstone: a rota de objeto responde o 404 da remoção e a projeção o tira do feed (§H).
        Assert.Equal(HttpStatusCode.NotFound,
            (await _host.Http.GetAsync($"/objects/{Uri.EscapeDataString(objectUri)}")).StatusCode);
        await InboxHarness.ProjectPendingAsync(_host);
        Assert.DoesNotContain("apagável", await FeedContentsAsync(slug));
    }

    [Fact] // AC-05 (Like idempotente por ator+objeto — §7.14)
    public async Task The_same_actor_liking_twice_keeps_the_count_at_one()
    {
        var (owner, _, slug) = await InboxHarness.CommunityAsync(_host, "ac05l");
        var postId = await PublishAsync(owner, slug, "post para curtir");
        await InboxHarness.ProjectPendingAsync(_host); // o like corrige a coluna da projeção já projetada

        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "graceac05");
        var like = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac05l")}}",
              "type": "Like",
              "actor": "{{actor}}",
              "object": "{{postId}}"
            }
            """;
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/c/{slug}/inbox", like, rsa, keyId)).StatusCode);
        Assert.Equal(1, await LikeCountAsync(slug, postId));

        // Outro activity.id, mesmo par (ator, objeto): a contagem não duplica.
        var repeated = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac05l")}}",
              "type": "Like",
              "actor": "{{actor}}",
              "object": "{{postId}}"
            }
            """;
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/c/{slug}/inbox", repeated, rsa, keyId)).StatusCode);
        Assert.Equal(1, await LikeCountAsync(slug, postId));
    }

    [Fact] // AC-05 (Flag entra na fila de moderação existente — IReportStore, GAP-28)
    public async Task A_flag_files_one_denunciation_and_a_repeat_files_no_second()
    {
        var (owner, _, slug) = await InboxHarness.CommunityAsync(_host, "ac05r");
        var postId = await PublishAsync(owner, slug, "post denunciável");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "henryac05");
        var reports = _host.App.Services.GetRequiredService<IReportStore>();

        var flag = FlagActivity(InboxHarness.Unique("ac05r"));
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/c/{slug}/inbox", flag, rsa, keyId)).StatusCode);

        var report = Assert.Single(await reports.QueryAsync(new ReportFilter(Post: postId)));
        Assert.Equal(actor, report.ReporterActorId);
        Assert.Equal("conteúdo impróprio", report.Reason);
        Assert.Equal(ReportStatus.Open, report.Status);

        var repeated = FlagActivity(InboxHarness.Unique("ac05r"));
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/c/{slug}/inbox", repeated, rsa, keyId)).StatusCode);
        Assert.Single(await reports.QueryAsync(new ReportFilter(Post: postId))); // GAP-28: (post, ator) é uma linha

        string FlagActivity(string activityId) => $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{activityId}}",
              "type": "Flag",
              "actor": "{{actor}}",
              "object": "{{postId}}",
              "summary": "conteúdo impróprio"
            }
            """;
    }

    [Fact] // AC-06 (tipo desconhecido e a família GAP-42: 202 + métrica, nunca erro)
    public async Task An_unknown_type_is_accepted_and_counted_by_the_metric()
    {
        var (_, _, slug) = await InboxHarness.CommunityAsync(_host, "ac06m");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "irisac06");

        using var listener = new MeterListener();
        long unknown = 0;
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FederationMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == FederationMetrics.UnknownActivityCounter)
            {
                Interlocked.Add(ref unknown, value);
            }
        });
        listener.Start();

        foreach (var type in new[] { "Warp", "Update", "Announce" })
        {
            var activity = $$"""
                {
                  "@context": "{{InboxHarness.ActivityStreams}}",
                  "id": "https://remote.example/activities/{{InboxHarness.Unique("ac06")}}",
                  "type": "{{type}}",
                  "actor": "{{actor}}"
                }
                """;
            Assert.Equal(HttpStatusCode.Accepted,
                (await InboxHarness.SendSignedAsync(_host.Http, $"/c/{slug}/inbox", activity, rsa, keyId)).StatusCode);
        }

        Assert.Equal(3, Interlocked.Read(ref unknown));
    }

    [Fact] // AC-06 (membersOnly só com destinatário local membro ativo)
    public async Task A_members_only_create_applies_for_a_member_and_not_for_a_stranger()
    {
        var (_, ownerHandle, slug) = await InboxHarness.CommunityAsync(_host, "ac06m");
        var (strangerHandle, _) = await RegisterAsync("ac06e");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "jackac06");

        // Para o dono (membro ativo do contêiner): aplicado, com a visibilidade congelada do par.
        var memberUri = $"https://remote.example/o/{InboxHarness.Unique("ac06m")}";
        var memberCreate = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac06m")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "object": {
                "id": "{{memberUri}}",
                "type": "Note",
                "attributedTo": "{{actor}}",
                "content": "só membros leem isto",
                "squid:slug": "{{slug}}",
                "squid:policy": { "visibility": "membersOnly" }
              }
            }
            """;
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/users/{ownerHandle}/inbox", memberCreate, rsa, keyId)).StatusCode);
        using (var created = JsonDocument.Parse(await GetAsync($"/api/posts/{Uri.EscapeDataString(memberUri)}")))
        {
            Assert.Equal("membersOnly", created.RootElement.GetProperty("visibilityAtPublish").GetString());
        }

        // Para um estranho (não membro): 202, mas nada aplicado — estado intacto.
        var strangerUri = $"https://remote.example/o/{InboxHarness.Unique("ac06e")}";
        var strangerCreate = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac06e")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "object": {
                "id": "{{strangerUri}}",
                "type": "Note",
                "attributedTo": "{{actor}}",
                "content": "entregue a um não membro",
                "audience": "https://remote.example/c/{{slug}}",
                "squid:policy": { "visibility": "membersOnly" }
              }
            }
            """;
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/users/{strangerHandle}/inbox", strangerCreate, rsa, keyId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _host.Http.GetAsync($"/api/posts/{Uri.EscapeDataString(strangerUri)}")).StatusCode);
    }

    [Fact] // AC-06 (private nunca é aplicado em claro — R-11)
    public async Task A_private_create_is_never_applied()
    {
        var (_, _, slug) = await InboxHarness.CommunityAsync(_host, "ac06p");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "kateac06");
        var objectUri = $"https://remote.example/o/{InboxHarness.Unique("ac06p")}";
        var activity = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac06p")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "object": {
                "id": "{{objectUri}}",
                "type": "Note",
                "attributedTo": "{{actor}}",
                "content": "em claro nunca",
                "squid:policy": { "visibility": "private" }
              }
            }
            """;

        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/c/{slug}/inbox", activity, rsa, keyId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _host.Http.GetAsync($"/objects/{Uri.EscapeDataString(objectUri)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _host.Http.GetAsync($"/api/posts/{Uri.EscapeDataString(objectUri)}")).StatusCode);
    }

    [Fact] // D-01 (sem destino nomeado → 202 sem aplicar, estado intacto)
    public async Task A_create_without_any_named_destination_is_accepted_without_applying()
    {
        var (_, ownerHandle, _) = await InboxHarness.CommunityAsync(_host, "d01");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "land01");
        var objectUri = $"https://remote.example/o/{InboxHarness.Unique("d01")}";
        var activity = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("d01")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "object": { "id": "{{objectUri}}", "type": "Note", "attributedTo": "{{actor}}", "content": "sem destino" }
            }
            """;

        // No inbox do ator, sem nenhuma referência nomeada no payload: aceito e nada aplicado.
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/users/{ownerHandle}/inbox", activity, rsa, keyId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _host.Http.GetAsync($"/objects/{Uri.EscapeDataString(objectUri)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _host.Http.GetAsync($"/api/posts/{Uri.EscapeDataString(objectUri)}")).StatusCode);
    }

    [Fact] // AC-07 (anexo baixado com guarda, integridade pelo SHA-256, falha não rejeita o post)
    public async Task Remote_attachments_are_verified_by_sha256_and_a_failed_one_stays_absent()
    {
        var (_, _, slug) = await InboxHarness.CommunityAsync(_host, "ac07");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "miaac07");
        var goodUrl = "https://remote.example/img-ac07.png";
        var brokenUrl = "https://remote.example/quebrado-ac07.png";
        var unreachableUrl = "http://127.0.0.1:9/fora-do-perimetro.png";
        var blob = Encoding.UTF8.GetBytes("bytes do png remoto");
        var hash = Convert.ToHexStringLower(SHA256.HashData(blob));
        _host.Media.Serve(goodUrl, blob);
        _host.Media.Serve(brokenUrl, Encoding.UTF8.GetBytes("bytes diferentes"));

        var objectUri = $"https://remote.example/o/{InboxHarness.Unique("ac07")}";
        var activity = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("ac07")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "to": ["{{InboxHarness.Public}}"],
              "object": {
                "id": "{{objectUri}}",
                "type": "Note",
                "attributedTo": "{{actor}}",
                "content": "post com anexo",
                "to": ["{{InboxHarness.Public}}"],
                "attachment": [
                  { "type": "Image", "mediaType": "image/png", "url": "{{goodUrl}}", "name": "descrição do anexo", "sha256": "{{hash}}" },
                  { "type": "Image", "mediaType": "image/png", "url": "{{brokenUrl}}", "name": "digest errado", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" },
                  { "type": "Image", "mediaType": "image/png", "url": "{{unreachableUrl}}", "name": "baixa falha" }
                ]
              }
            }
            """;

        // Nenhuma das três falhas rejeita o post (GAP-44): a entrega segue 202 e o post nasce.
        Assert.Equal(HttpStatusCode.Accepted,
            (await InboxHarness.SendSignedAsync(_host.Http, $"/c/{slug}/inbox", activity, rsa, keyId)).StatusCode);

        using var document = JsonDocument.Parse(await GetAsync($"/api/posts/{Uri.EscapeDataString(objectUri)}"));
        var attachments = document.RootElement.GetProperty("attachments");
        var kept = Assert.Single(attachments.EnumerateArray());
        Assert.Equal(hash, kept.GetProperty("contentHash").GetString());
        Assert.Equal("descrição do anexo", kept.GetProperty("alt").GetString());
        Assert.Equal($"{_host.Origin}/api/media/{hash}", kept.GetProperty("url").GetString());

        // O blob real está no volume content-addressado (a guarda SSRF mora no cliente de produção).
        var media = _host.App.Services.GetRequiredService<IMediaStore>();
        Assert.NotNull(await media.ReadAsync(hash));
        Assert.Contains(goodUrl, _host.Media.Requests.ToArray());
        Assert.Contains(unreachableUrl, _host.Media.Requests.ToArray());
    }

    [Fact] // AC-07 (a guarda SSRF do download de produção recusa antes de qualquer socket)
    public async Task The_production_media_client_never_touches_a_blocked_address()
    {
        var handler = new NeverCalledHandler();
        using var http = new HttpClient(handler);
        var client = new RemoteMediaClient(http, new FakeUnfurlResolver(), new UnfurlOptions(500, 4096));

        Assert.Null(await client.FetchAsync("http://127.0.0.1:9/a.png", CancellationToken.None));
        Assert.Null(await client.FetchAsync("http://192.168.1.1/a.png", CancellationToken.None));
        Assert.Null(await client.FetchAsync("http://169.254.169.254/meta/a.png", CancellationToken.None));
        Assert.Null(await client.FetchAsync("ftp://remote.example/a.png", CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    // ---- house helpers -----------------------------------------------------------------

    private async Task<(string Handle, AuthClient Client)> RegisterAsync(string prefix)
    {
        var handle = PostRoutes.Handle(prefix);
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created,
            (await client.RegisterAsync(handle, PostRoutes.Password)).StatusCode);
        return (handle, client);
    }

    private async Task<string> GetAsync(string path)
    {
        using var response = await _host.Http.GetAsync(path);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Publishes one local post and answers its canonical id.</summary>
    private async Task<string> PublishAsync(AuthClient owner, string slug, string content)
    {
        var published = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts", new { content }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        using var json = JsonDocument.Parse(await published.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>The contents of every item on page 1 of a container's feed, in order.</summary>
    private async Task<List<string>> FeedContentsAsync(string slug)
    {
        using var json = JsonDocument.Parse(await GetAsync($"/api/containers/{slug}/feed"));
        return FeedHarness.Contents(json.RootElement.GetProperty("items"));
    }

    /// <summary>The likeCount a projection row serves for one post.</summary>
    private async Task<long> LikeCountAsync(string slug, string postId)
    {
        using var json = JsonDocument.Parse(await GetAsync($"/api/containers/{slug}/feed"));
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("id").GetString() == postId)
            {
                return item.GetProperty("likeCount").GetInt64();
            }
        }

        return -1; // o post nem está no feed: a contagem não existe, é um valor que falha a asserção
    }

    /// <summary>The actor URIs of page 1 of a relation collection, in the order they federate.</summary>
    private async Task<string[]> RelationAsync(string collectionId)
    {
        using var json = JsonDocument.Parse(await GetAsync($"{collectionId}?page=1"));
        return json.RootElement.GetProperty("orderedItems").EnumerateArray()
            .Select(item => item.GetString())
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    /// <summary>A handler that records the call: reaching it means the SSRF guard let a request through.</summary>
    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("the guard must refuse before any socket is opened");
        }
    }
}
