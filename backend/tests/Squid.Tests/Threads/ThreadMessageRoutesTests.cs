using System.Net;
using System.Text;
using System.Text.Json;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-01..AC-04 of TASK-0034 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// the seq of a message is taken by the thread's own state under the version guard — two senders
/// racing never share a number (AC-01); Content is sanitized on the server and empty or over the
/// 4000 cap of data-model §2.5.1 refuses with 400 (AC-02, GAP-24's rule); <c>?since=</c> answers
/// the exact delta in seq order with <c>nextSince</c> (AC-03, §9.3); and a non-participant and an
/// absent thread share one byte-identical 404 in GET and POST, leaking neither existence nor the
/// participants (AC-04, FR-031/SC-005).
/// </summary>
[Collection("ServiceHost")]
public sealed class ThreadMessageRoutesTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ThreadMessageRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Two_sends_racing_one_thread_never_share_a_seq_and_the_delta_keeps_all_of_them()
    {
        var (sender, _) = await PostRoutes.SignedInAsync(_host, "msgconc1");
        var (peer, peerHandle) = await PostRoutes.SignedInAsync(_host, "msgconc2");
        // The second seat belongs to the peer's own account, so both halves of the race can send.
        var threadId = await DirectThreadAsync(sender, peerHandle);

        var seqs = new List<long>();

        // Two rounds of the same race: both participants fire in the same instant, each from its
        // own session (AC-01's "dois envios concorrentes" — a real parallel pair, not a sequence).
        for (var round = 0; round < 2; round++)
        {
            var racing = await Task.WhenAll(
                PostRoutes.SendAsync(sender, HttpMethod.Post, MessagesPath(threadId),
                    new { content = $"do A r{round}" }, sender.Csrf),
                PostRoutes.SendAsync(peer, HttpMethod.Post, MessagesPath(threadId),
                    new { content = $"do B r{round}" }, peer.Csrf));
            Assert.All(racing, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

            foreach (var response in racing)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                seqs.Add(json.RootElement.GetProperty("seq").GetInt64());
            }
        }

        // Four sends, four DIFFERENT numbers, exactly 1..4: the ETag serialized every race and
        // nothing burned a number on the way.
        Assert.Equal(new long[] { 1, 2, 3, 4 }, seqs.OrderBy(value => value).ToArray());

        // And the delta returns all four, once each, ordered by seq.
        var history = await MessagesAsync(sender, threadId, since: 0);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, SeqsOf(history));
        Assert.Equal(4, history.GetProperty("nextSince").GetInt64());
        // Every send made it through the race — order between the pair is the race's to decide.
        Assert.Equal(
            new[] { "do A r0", "do A r1", "do B r0", "do B r1" },
            ItemsOf(history).Select(item => item.GetProperty("content").GetString())
                .Order(StringComparer.Ordinal).ToArray());
    }

    [Fact] // AC-02
    public async Task Content_is_sanitized_on_the_server_and_empty_or_over_four_thousand_is_400()
    {
        var (sender, _) = await PostRoutes.SignedInAsync(_host, "msgcap");
        var threadId = await DirectThreadAsync(sender, "parcap");

        // Empty, and a text the sanitizer empties (controls only): 400 both — AC-02 "vazio".
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(sender, threadId,
            new { content = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(sender, threadId,
            new { content = "\u0001\u0002" })).StatusCode);

        // Over the cap of data-model §2.5.1 BEFORE the write, and at the cap exactly: 400 then 200.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(sender, threadId,
            new { content = new string('a', 4001) })).StatusCode);
        var atCap = await SendAsync(sender, threadId, new { content = new string('a', 4000) });
        Assert.Equal(HttpStatusCode.OK, atCap.StatusCode);
        using var atCapJson = JsonDocument.Parse(await atCap.Content.ReadAsStringAsync());
        Assert.Equal(1, atCapJson.RootElement.GetProperty("seq").GetInt64());

        // Escaping MULTIPLIES the text (& → &amp;): 4000 ampersands fit raw and explode past the
        // cap after sanitizing — refused, because §2.5.1 caps what is PERSISTED.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(sender, threadId,
            new { content = new string('&', 4000) })).StatusCode);

        // GAP-24's rule in one pass: HTML metacharacters escaped, C0 controls dropped, the text's
        // own tab/LF kept — decided on the server, whatever the client sent.
        var dirty = await SendAsync(sender, threadId, new { content = "<b>oi & 'x'</b>\u0007\nfim\t" });
        Assert.Equal(HttpStatusCode.OK, dirty.StatusCode);

        // The whole history is exactly the two accepted sends: every refusal wrote NOTHING.
        var history = await MessagesAsync(sender, threadId, since: 0);
        var items = ItemsOf(history);
        Assert.Equal(new long[] { 1, 2 }, SeqsOf(history));
        Assert.Equal(new string('a', 4000), items[0].GetProperty("content").GetString());
        Assert.Equal("&lt;b&gt;oi &amp; &#39;x&#39;&lt;/b&gt;\nfim\t",
            items[1].GetProperty("content").GetString());
    }

    [Fact] // AC-03
    public async Task The_delta_returns_only_seq_since_ordered_with_nextSince_and_never_repeats()
    {
        var (sender, _) = await PostRoutes.SignedInAsync(_host, "msgdelta");
        var threadId = await DirectThreadAsync(sender, "pardelta");

        for (var index = 1; index <= 5; index++)
        {
            var sent = await SendAsync(sender, threadId, new { content = $"mensagem {index}" });
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            using var json = JsonDocument.Parse(await sent.Content.ReadAsStringAsync());
            Assert.Equal(index, json.RootElement.GetProperty("seq").GetInt64());
        }

        // Since 0: the whole history, in seq order, cursor at the end (§9.3).
        var all = await MessagesAsync(sender, threadId, since: 0);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, SeqsOf(all));
        Assert.Equal(5, all.GetProperty("nextSince").GetInt64());
        Assert.Equal(
            new[] { "mensagem 1", "mensagem 2", "mensagem 3", "mensagem 4", "mensagem 5" },
            ItemsOf(all).Select(item => item.GetProperty("content").GetString()).ToArray());

        // Middle of the road: only seq > since, nothing behind it.
        var tail = await MessagesAsync(sender, threadId, since: 3);
        Assert.Equal(new long[] { 4, 5 }, SeqsOf(tail));
        Assert.Equal(5, tail.GetProperty("nextSince").GetInt64());

        // At the end: empty, and the cursor does not move (a poll that found nothing keeps its place).
        var none = await MessagesAsync(sender, threadId, since: 5);
        Assert.Empty(ItemsOf(none));
        Assert.Equal(5, none.GetProperty("nextSince").GetInt64());

        // The same poll twice answers byte-identically: no duplicate ever walks out twice.
        var first = await GetRawAsync(sender, $"{MessagesPath(threadId)}?since=2");
        var second = await GetRawAsync(sender, $"{MessagesPath(threadId)}?since=2");
        Assert.Equal(first, second);
        using var repoll = JsonDocument.Parse(Encoding.UTF8.GetString(first));
        Assert.Equal(new long[] { 3, 4, 5 }, SeqsOf(repoll.RootElement));

        // A cursor that is not a number is a payload this route cannot take (§8: 400).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await sender.SendAsync(HttpMethod.Get, $"{MessagesPath(threadId)}?since=abc")).StatusCode);
    }

    [Fact] // AC-04
    public async Task A_non_participant_and_an_absent_thread_get_one_404_body_byte_for_byte()
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, "msgdono");
        var (stranger, _) = await PostRoutes.SignedInAsync(_host, "msgestra");
        var threadId = await DirectThreadAsync(owner, "par404");
        // Owner in their own thread (the second seat is a placeholder that never signs in); the
        // stranger is in neither seat.

        var real = await MessagesAsync(owner, threadId, since: 0);
        Assert.Empty(ItemsOf(real)); // the thread exists and answers its owner before the race

        var absentId = $"{PostRoutes.Origin(_host)}/t/{Convert.ToHexStringLower(Guid.NewGuid().ToByteArray())}";
        var responses = new[]
        {
            // The foreign thread, both verbs…
            await PostRoutes.SendAsync(stranger, HttpMethod.Get, $"{MessagesPath(threadId)}?since=0"),
            await PostRoutes.SendAsync(stranger, HttpMethod.Post, MessagesPath(threadId),
                new { content = "não deveria entrar" }, stranger.Csrf),
            // …and the thread that never existed, both verbs: all four cases, one body.
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

        Assert.Equal(bodies[0], bodies[1]); // foreign × GET/POST
        Assert.Equal(bodies[0], bodies[2]); // foreign × absent
        Assert.Equal(bodies[0], bodies[3]); // absent × GET/POST

        // The refusal carries nothing but the envelope: no traceId (it would differ per request
        // and break the pair), no participant list, no owner, not even the thread id (SC-005).
        var text = Encoding.UTF8.GetString(bodies[0]);
        Assert.Contains("not_found", text, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("participants", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ownerHandle, text, StringComparison.Ordinal);
        Assert.DoesNotContain(threadId, text, StringComparison.Ordinal);

        // The stranger never became visible to the owner's thread: still empty, and the owner
        // still reads and sends — the refusals touched nothing.
        Assert.Empty(ItemsOf(await MessagesAsync(owner, threadId, since: 0)));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, threadId,
            new { content = "dono continua" })).StatusCode);
    }

    /// <summary>Creates a direct thread with this actor and <paramref name="participantHandle"/>.</summary>
    private async Task<string> DirectThreadAsync(AuthClient owner, string participantHandle)
    {
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/threads",
            new { kind = "direct", participantActorIds = new[] { participantHandle } }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>POST one message through the route as the contract spells it (id percent-encoded).</summary>
    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client, string threadId, object body) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, MessagesPath(threadId), body, client.Csrf);

    /// <summary>The parsed <c>GET .../messages?since=</c> body.</summary>
    private async Task<JsonElement> MessagesAsync(AuthClient client, string threadId, long since)
    {
        var raw = await GetRawAsync(client, $"{MessagesPath(threadId)}?since={since}");
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(raw));
        return json.RootElement.Clone();
    }

    private async Task<byte[]> GetRawAsync(AuthClient client, string path)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static JsonElement[] ItemsOf(JsonElement envelope) =>
        envelope.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();

    private static long[] SeqsOf(JsonElement envelope) =>
        ItemsOf(envelope).Select(item => item.GetProperty("seq").GetInt64()).ToArray();

    /// <summary>The route as the contract spells it: a thread id is a URI, so one escaped segment.</summary>
    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";
}
