using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Posts;

/// <summary>
/// QA-0022 — the seven acceptance criteria of TASK-0023, one scenario each, executed against the
/// host <see cref="SquidServiceHost"/> builds. Every expected value is read from an artifact this
/// run produced (the container's own policy body, the persisted grain documents, the bytes of two
/// error responses) — never from a label remembered from the implementation, so an assertion can
/// fail when the behaviour breaks.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0023Tests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public QaTask0023Tests(AuthHostFixture host) => _host = host;

    private IPolicyAuditStore Audit =>
        _host.App.Services.GetRequiredService<IPolicyAuditStore>();

    private IActivityStore Activities =>
        _host.App.Services.GetRequiredService<IActivityStore>();

    private IActorStatusStore Statuses =>
        _host.App.Services.GetRequiredService<IActorStatusStore>();

    [Fact] // AC-01
    public async Task Ac01_publish_answers_the_frozen_fields_and_appends_a_raw_create_event_on_one_global_sequence()
    {
        var (owner, handle) = await PostRoutes.SignedInAsync(_host, "qa01dono");
        var origin = PostRoutes.Origin(_host);
        var slugA = await NewContainerAsync(owner, "group", PostRoutes.Slug("qa01a"));
        var slugB = await NewContainerAsync(owner, "group", PostRoutes.Slug("qa01b"));

        var logBefore = await Activities.ReadAsync();

        // A fresh container has no sequence document yet, so its first number can only be 1.
        var seqRowBefore = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteObjectStore.SeqStoreName,
            GrainId.Create("objectseq", slugA).ToString());
        Assert.True(
            string.IsNullOrEmpty(seqRowBefore),
            $"the fresh container already carried a sequence row: {seqRowBefore}");

        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slugA}/posts", new { content = "primeiro texto da QA" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var view = await JsonOfAsync(created);
        var id = Text(view, "id")!;
        Assert.StartsWith($"{origin}/o/", id, StringComparison.Ordinal);
        Assert.Equal(1, view.GetProperty("seq").GetInt64());
        Assert.Equal("post", Text(view, "kind"));

        // The frozen values are read from the container's own policy body at publishing time.
        var container = await ContainerOfAsync(owner, slugA);
        var policy = container.GetProperty("policy");
        var versionAtPublish = policy.GetProperty("version").GetInt32();
        var visibilityAtPublish = policy.GetProperty("visibility").GetString()!;
        Assert.Equal(versionAtPublish, view.GetProperty("policyVersion").GetInt32());
        Assert.Equal(visibilityAtPublish, Text(view, "visibilityAtPublish"));

        // ...and they stay frozen when the policy in force moves on (§2.5, SPEC-0009 A).
        var moved = await PostRoutes.SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slugA}/policy", new { visibility = "public" },
            owner.Csrf, ifMatch: $"\"{versionAtPublish}\"");
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var containerAfter = await ContainerOfAsync(owner, slugA);
        var policyAfter = containerAfter.GetProperty("policy");
        Assert.NotEqual(
            visibilityAtPublish, policyAfter.GetProperty("visibility").GetString());
        Assert.True(policyAfter.GetProperty("version").GetInt32() > versionAtPublish);

        var reread = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{id}");
        Assert.Equal(HttpStatusCode.OK, reread.StatusCode);
        var postAfter = await JsonOfAsync(reread);
        Assert.Equal(visibilityAtPublish, Text(postAfter, "visibilityAtPublish"));
        Assert.Equal(versionAtPublish, postAfter.GetProperty("policyVersion").GetInt32());

        // A second publish in ANOTHER container: Object.Seq restarts, the event sequence does not.
        var createdB = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slugB}/posts", new { content = "do outro contêiner" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, createdB.StatusCode);
        var viewB = await JsonOfAsync(createdB);
        Assert.Equal(1, viewB.GetProperty("seq").GetInt64());

        var logAfter = await Activities.ReadAsync();
        Assert.Equal(logBefore.Count + 2, logAfter.Count);

        // Append-only: every event that was in the log is still there, byte for byte (§2.8).
        foreach (var previous in logBefore)
        {
            var still = Assert.Single(logAfter, candidate => candidate.Id == previous.Id);
            Assert.Equal(previous.Seq, still.Seq);
            Assert.Equal(previous.Payload, still.Payload);
        }

        // ONE numbering across both containers, append order, no gap (GAP-23 global Activity.Seq).
        var appended = logAfter.Skip(logBefore.Count).ToList();
        Assert.Equal(
            new long[] { logBefore.Count + 1, logBefore.Count + 2 },
            appended.Select(candidate => candidate.Seq).ToArray());

        var activity = appended[0];
        Assert.Equal(id, activity.ObjectId);
        Assert.Equal(Text(viewB, "id"), appended[1].ObjectId);
        Assert.Equal("Create", activity.Type);
        Assert.Equal($"{origin}/users/{handle}", activity.ActorId);
        Assert.StartsWith("urn:squid:activity:", activity.Id, StringComparison.Ordinal);

        // The payload is the RAW json of the post (R-13): parse it and read the values back.
        using (var payload = JsonDocument.Parse(activity.Payload))
        {
            Assert.Equal(id, payload.RootElement.GetProperty("id").GetString());
            Assert.Equal("primeiro texto da QA", payload.RootElement.GetProperty("content").GetString());
            Assert.Equal(1, payload.RootElement.GetProperty("seq").GetInt64());
        }

        // ...and raw in storage too: the persisted document holds that very string.
        var rawLog = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteActivityStore.StoreName,
            GrainId.Create("activity", "log").ToString());
        Assert.False(string.IsNullOrEmpty(rawLog), "the activity document was not persisted");
        using (var document = JsonDocument.Parse(rawLog!))
        {
            var stored = document.RootElement.GetProperty("Events").EnumerateArray()
                .Single(element => element.GetProperty("Seq").GetInt64() == activity.Seq);
            Assert.Equal(activity.Payload, stored.GetProperty("Payload").GetString());
        }
    }

    [Fact] // AC-02
    public async Task Ac02_denied_member_gets_403_with_a_deny_row_and_an_invisible_container_answers_the_absent_404()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa02dono");
        var slug = await NewContainerAsync(owner, "community", PostRoutes.Slug("qa02"));

        // A policy that denies Posting: version 1 -> 2 (the version the denial must report).
        var changed = await PostRoutes.SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/policy", new { posting = "moderators" },
            owner.Csrf, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var container = await ContainerOfAsync(owner, slug);
        var currentVersion = container.GetProperty("policy").GetProperty("version").GetInt32();

        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "qa02membro");
        var joined = await PostRoutes.SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");

        var denied = await PostRoutes.SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "não publico" }, member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        // Contract §1: the envelope carries a traceId for every error BUT the shared 404.
        var deniedBody = await denied.Content.ReadAsStringAsync();
        Assert.Contains("policy_denied", deniedBody, StringComparison.Ordinal);
        Assert.Contains("traceId", deniedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", deniedBody, StringComparison.Ordinal); // contract §9.7

        // The refusal is a decision: one Deny row under the version that was evaluated (FR-019).
        var rows = await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: memberHandle, Action: "Post", Limit: 500));
        var deny = Assert.Single(rows, row => row.Result == PolicyAudit.Deny);
        Assert.Equal(currentVersion, deny.PolicyVersion);
        Assert.False(string.IsNullOrEmpty(deny.Reason));
        Assert.EndsWith($"/users/{memberHandle}", deny.ActorId, StringComparison.Ordinal);

        // The other half: an invisible container and an absent slug answer the very same bytes.
        var closed = await NewContainerAsync(owner, "group", PostRoutes.Slug("qa02fechado"));
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "qa02fora");
        var invisible = await PostRoutes.SendAsync(stranger, HttpMethod.Post,
            $"/api/containers/{closed}/posts", new { content = "de fora" }, stranger.Csrf);
        var absent = await PostRoutes.SendAsync(stranger, HttpMethod.Post,
            "/api/containers/slug-que-nao-existe/posts", new { content = "de fora" }, stranger.Csrf);

        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        var invisibleBody = await invisible.Content.ReadAsStringAsync();
        var absentBody = await absent.Content.ReadAsStringAsync();
        Assert.Equal(invisibleBody, absentBody);
        Assert.Contains("not_found", invisibleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", invisibleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", invisibleBody, StringComparison.Ordinal);
    }

    [Fact] // AC-03
    public async Task Ac03_concurrent_publishes_keep_the_seq_unique_and_growing_and_containers_stay_apart()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa03dono");
        var slugA = await NewContainerAsync(owner, "community", PostRoutes.Slug("qa03a"));
        var slugB = await NewContainerAsync(owner, "community", PostRoutes.Slug("qa03b"));

        var (one, _) = await PostRoutes.SignedInAsync(_host, "qa03um");
        var (two, _) = await PostRoutes.SignedInAsync(_host, "qa03dois");
        foreach (var client in new[] { one, two })
        {
            var joined = await PostRoutes.SendAsync(client, HttpMethod.Post,
                $"/api/containers/{slugA}/join", null, client.Csrf);
            Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");
        }

        // The race: four publishes of the same container in flight at once (AC-03).
        var racers = new[] { owner, one, two, owner };
        var inFlight = racers.Select((client, position) => PostRoutes.SendAsync(
            client, HttpMethod.Post, $"/api/containers/{slugA}/posts",
            new { content = $"corrida {position}" }, client.Csrf)).ToArray();
        var responses = await Task.WhenAll(inFlight);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

        var seqs = new List<long>();
        var ids = new List<string>();
        foreach (var response in responses)
        {
            var view = await JsonOfAsync(response);
            seqs.Add(view.GetProperty("seq").GetInt64());
            ids.Add(Text(view, "id")!);
        }

        // Unique, complete and growing: the race handed out exactly the container's first numbers.
        Assert.Equal(new long[] { 1, 2, 3, 4 }, seqs.OrderBy(seq => seq).ToArray());
        Assert.Equal(4, ids.Distinct(StringComparer.Ordinal).Count());

        // ...and the counter keeps growing from where it stopped.
        var fifth = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slugA}/posts", new { content = "depois da corrida" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, fifth.StatusCode);
        Assert.Equal(5, (await JsonOfAsync(fifth)).GetProperty("seq").GetInt64());

        // Another container starts its own count at 1: the two counters never touch.
        var inB = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slugB}/posts", new { content = "do outro" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, inB.StatusCode);
        Assert.Equal(1, (await JsonOfAsync(inB)).GetProperty("seq").GetInt64());
    }

    [Fact] // AC-04
    public async Task Ac04_reading_answers_the_post_or_one_404_for_invisible_and_absent()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa04dono");
        var slug = await NewContainerAsync(owner, "group", PostRoutes.Slug("qa04"));
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "ler isto" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var view = await JsonOfAsync(created);
        var id = Text(view, "id")!;
        var key = id[(id.LastIndexOf('/') + 1)..];

        // One row, three spellings: opaque id, whole URI, percent-encoded URI.
        var byKey = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{key}");
        var byUri = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{id}");
        var byEscaped = await PostRoutes.SendAsync(
            owner, HttpMethod.Get, $"/api/posts/{Uri.EscapeDataString(id)}");
        Assert.Equal(HttpStatusCode.OK, byKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byUri.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byEscaped.StatusCode);

        var visibleBody = await byKey.Content.ReadAsStringAsync();
        Assert.Equal(visibleBody, await byUri.Content.ReadAsStringAsync());
        Assert.Equal(visibleBody, await byEscaped.Content.ReadAsStringAsync());
        using (var visible = JsonDocument.Parse(visibleBody))
        {
            Assert.Equal("ler isto", visible.RootElement.GetProperty("content").GetString());
            Assert.Equal(1, visible.RootElement.GetProperty("seq").GetInt64());
        }

        // Invisible, absent and no session at all: one body, no traceId (FR-018, SC-005).
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "qa04fora");
        var invisible = await PostRoutes.SendAsync(stranger, HttpMethod.Get, $"/api/posts/{key}");
        var absent = await PostRoutes.SendAsync(stranger, HttpMethod.Get, "/api/posts/id-que-nao-existe");
        var anonymous = await _host.NewClient().SendAsync(HttpMethod.Get, $"/api/posts/{key}");

        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        var invisibleBody = await invisible.Content.ReadAsStringAsync();
        var absentBody = await absent.Content.ReadAsStringAsync();
        var anonymousBody = await anonymous.Content.ReadAsStringAsync();
        Assert.Equal(invisibleBody, absentBody);
        Assert.Equal(invisibleBody, anonymousBody);
        Assert.Contains("not_found", invisibleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", invisibleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", invisibleBody, StringComparison.Ordinal);
    }

    [Fact] // AC-05
    public async Task Ac05_three_answers_publish_the_fourth_is_422_and_out_of_sight_targets_share_the_404()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa05dono");
        var slug = await NewContainerAsync(owner, "group", PostRoutes.Slug("qa05"));
        var root = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "raiz" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, root.StatusCode);

        var rootView = await JsonOfAsync(root);
        var parentId = Text(rootView, "id")!;
        var containerId = Text(rootView, "containerId");
        var expectedSeq = rootView.GetProperty("seq").GetInt64() + 1;

        // Depth 1, 2 and 3 publish — "profundidade 3 funciona" (AC-05, root = 0, D-01).
        var parent = parentId;
        for (var depth = 1; depth <= 3; depth++)
        {
            var answer = await PostRoutes.SendAsync(owner, HttpMethod.Post,
                $"/api/posts/{parent}/replies", new { content = $"nível {depth}" }, owner.Csrf);
            Assert.Equal(HttpStatusCode.Created, answer.StatusCode);

            var body = await JsonOfAsync(answer);
            Assert.Equal(depth, body.GetProperty("depth").GetInt32());
            Assert.Equal(parent, Text(body, "replyToId"));
            Assert.Equal(containerId, Text(body, "containerId"));
            Assert.Equal(expectedSeq++, body.GetProperty("seq").GetInt64());
            parent = Text(body, "id")!;
        }

        // The fourth level: 422 invalid_reply_target, and not one row written (AC-05).
        var rowsBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName);
        var eventsBefore = (await Activities.ReadAsync()).Count;
        var tooDeep = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/posts/{parent}/replies", new { content = "quarta" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooDeep.StatusCode);
        var tooDeepBody = await tooDeep.Content.ReadAsStringAsync();
        Assert.Contains("invalid_reply_target", tooDeepBody, StringComparison.Ordinal);
        Assert.Equal(rowsBefore, await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName));
        Assert.Equal(eventsBefore, (await Activities.ReadAsync()).Count);

        // A target outside the reader's sight answers byte-identically to an absent id.
        var parentKey = parent[(parent.LastIndexOf('/') + 1)..];
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "qa05fora");
        var foreign = await PostRoutes.SendAsync(stranger, HttpMethod.Post,
            $"/api/posts/{parentKey}/replies", new { content = "de fora" }, stranger.Csrf);
        var absent = await PostRoutes.SendAsync(stranger, HttpMethod.Post,
            "/api/posts/id-que-nao-existe/replies", new { content = "de fora" }, stranger.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        var foreignBody = await foreign.Content.ReadAsStringAsync();
        Assert.Equal(foreignBody, await absent.Content.ReadAsStringAsync());
        Assert.Contains("not_found", foreignBody, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", foreignBody, StringComparison.Ordinal);

        // Contract §8 half of "post de outro contêiner": a PUBLIC container's post is not
        // invisible, so the policy answer travels as 403 policy_denied — 404 would be the
        // leak-free answer of a restricted resource, and §9.1 forbids that shape for public ones.
        var (pubOwner, _) = await PostRoutes.SignedInAsync(_host, "qa05pub");
        var pubSlug = await NewContainerAsync(pubOwner, "community", PostRoutes.Slug("qa05pub"));
        var pubPost = await PostRoutes.SendAsync(pubOwner, HttpMethod.Post,
            $"/api/containers/{pubSlug}/posts", new { content = "público" }, pubOwner.Csrf);
        Assert.Equal(HttpStatusCode.Created, pubPost.StatusCode);
        var pubView = await JsonOfAsync(pubPost);
        var pubKey = Text(pubView, "id")!;
        pubKey = pubKey[(pubKey.LastIndexOf('/') + 1)..];

        var guest = await PostRoutes.SendAsync(stranger, HttpMethod.Post,
            $"/api/posts/{pubKey}/replies", new { content = "convidado" }, stranger.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, guest.StatusCode);
        Assert.Contains("policy_denied",
            await guest.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact] // AC-06, first half — data-model §3.1
    public async Task Ac06_a_suspended_actor_is_refused_and_audited_while_reading_keeps_working()
    {
        var (owner, handle) = await PostRoutes.SignedInAsync(_host, "qa06dono");
        var slug = await NewContainerAsync(owner, "group", PostRoutes.Slug("qa06"));

        var published = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "antes da suspensão" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var post = await JsonOfAsync(published);
        var id = Text(post, "id")!;

        // The status has no route until TASK-0031 (D-02); the rule is active all the same.
        await Statuses.SuspendAsync(handle);

        var denied = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "não pode mais" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        Assert.Contains("policy_denied", deniedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", deniedBody, StringComparison.Ordinal); // contract §9.7

        var rows = await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: handle, Action: "Post", Limit: 500));
        var deny = Assert.Single(rows, row => row.Result == PolicyAudit.Deny);
        Assert.Contains("suspenso", deny.Reason, StringComparison.Ordinal); // §3.1 named in the trail

        // The same refusal on the reply ladder: a suspended actor does not comment either.
        var reply = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/posts/{id}/replies", new { content = "resposta suspenso" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, reply.StatusCode);

        // §3.1: publishing stops, reading does not.
        var read = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact] // AC-06, second half — cap and GAP-24 sanitization
    public async Task Ac06_content_above_the_cap_is_400_and_html_and_c0_controls_are_sanitized()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "qa06b");
        var slug = await NewContainerAsync(owner, "group", PostRoutes.Slug("qa06b"));

        var objectsBefore = await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName);
        var eventsBefore = (await Activities.ReadAsync()).Count;

        var tooLong = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = new string('x', 5001) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var tooLongBody = await tooLong.Content.ReadAsStringAsync();
        Assert.Contains("validation_failed", tooLongBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", tooLongBody, StringComparison.Ordinal); // contract §9.7
        Assert.Equal(objectsBefore, await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName));
        Assert.Equal(eventsBefore, (await Activities.ReadAsync()).Count);

        // "> 5000 refused": exactly 5000 is still a post, and it takes the next container seq.
        var atCap = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = new string('y', 5000) }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, atCap.StatusCode);
        Assert.Equal(objectsBefore + 1, await AuthDb.CountAsync(_host.StoragePath, SqliteObjectStore.StoreName));

        // GAP-24: HTML escaped, C0 controls (and DEL) dropped, the text itself untouched.
        const string raw = "olá <script>alert(1)</script> & \"aspas\" 'texto' \u0007\u0000\u001b\u007f fim";
        var cleaned = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = raw }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, cleaned.StatusCode);

        var view = await JsonOfAsync(cleaned);
        var content = Text(view, "content")!;
        Assert.DoesNotContain("<", content, StringComparison.Ordinal);
        Assert.DoesNotContain(">", content, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", content, StringComparison.Ordinal);
        Assert.Contains("&amp;", content, StringComparison.Ordinal);
        Assert.Contains("&quot;aspas&quot;", content, StringComparison.Ordinal);
        Assert.Contains("&#39;texto&#39;", content, StringComparison.Ordinal);
        Assert.Contains("olá", content, StringComparison.Ordinal); // accents are text, not markup
        Assert.Contains(" fim", content, StringComparison.Ordinal);
        foreach (var character in content)
        {
            Assert.True(character is '\t' or '\n' or '\r' or >= ' ',
                $"control character U+{(int)character:X4} survived the sanitizer");
        }

        // The stored form, not just the response: a read returns exactly the same text.
        var read = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{Text(view, "id")}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBack = await JsonOfAsync(read);
        Assert.Equal(content, Text(readBack, "content"));
    }

    /// <summary>One container of <paramref name="kind"/>, created by its owner.</summary>
    private static async Task<string> NewContainerAsync(AuthClient owner, string kind, string slug)
    {
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind, name = slug, slug }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return slug;
    }

    /// <summary>The container body of <c>GET /api/containers/{slug}</c> — the artifact the frozen values are read from.</summary>
    private async Task<JsonElement> ContainerOfAsync(AuthClient client, string slug)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonOfAsync(response);
    }

    /// <summary>The parsed body, cloned out of its document so the caller can hold it.</summary>
    private static async Task<JsonElement> JsonOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>One property of a view as text; a missing property fails the assertion, never defaults.</summary>
    private static string? Text(JsonElement element, string name) => element.GetProperty(name).GetString();
}
