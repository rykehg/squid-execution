using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Squid.Tests.Auth;

namespace Squid.Tests.Stream;

/// <summary>
/// TASK-0026 — the SSE acceptance criteria before the visibility gate (AC-04 has its own file,
/// AC-06 is the gate): the live delivery of a publish with the global seq as id (AC-01), the
/// <c>Last-Event-ID</c> delta across a disconnect (AC-02), the injectable heartbeat (AC-03) and
/// the 401 of a sessionless connect (AC-05). Assertions match on a UNIQUE content, never on frame
/// position: a connection without a cursor first replays the log, so "which frame came first"
/// says nothing and "the frame with my content" says everything.
/// </summary>
[Collection("ServiceHost")]
public sealed class StreamRouteTests : IClassFixture<StreamHostFixture>
{
    private readonly StreamHostFixture _host;

    public StreamRouteTests(StreamHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Ac01_a_published_post_reaches_the_connected_subscriber_in_under_a_second_with_the_global_seq_as_id()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "sac01dono");
        var (reader, _) = await StreamHarness.SignedInAsync(_host, "sac01leitor");
        var slug = await StreamHarness.NewContainerAsync(owner, "sac01");

        using var stream = await SseConnection.OpenAsync(_host, reader);

        var content = StreamHarness.Unique("ao-vivo");
        var watch = Stopwatch.StartNew();
        var postId = await StreamHarness.PublishAsync(owner, slug, content);
        var seen = await stream.ReadUntilAsync(Matches(content), TimeSpan.FromSeconds(5));
        watch.Stop();

        var frame = Assert.Single(seen, Matches(content));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1),
            $"the Sprint 3 DoD asks for under a second on the wire, this delivery took {watch.Elapsed}");

        // event: post, id = the GLOBAL Activity.Seq (GAP-23) — never the Object.Seq of the answer.
        Assert.Equal("post", frame.Event);
        Assert.Equal(
            (await StreamHarness.GlobalSeqAsync(_host, postId)).ToString(CultureInfo.InvariantCulture),
            frame.Id);

        // data = the summary of contract §5: content, author and containerId ride in it.
        Assert.Contains(content, frame.Data!, StringComparison.Ordinal);
        using var summary = JsonDocument.Parse(frame.Data!);
        Assert.Equal(
            $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}/c/{slug}",
            summary.RootElement.GetProperty("containerId").GetString());
        Assert.False(string.IsNullOrEmpty(summary.RootElement.GetProperty("author").GetString()));
    }

    [Fact] // AC-02
    public async Task Ac02_last_event_id_reconnects_into_the_exact_delta_nothing_lost_nothing_repeated()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "sac02dono");
        var slug = await StreamHarness.NewContainerAsync(owner, "sac02");
        var firstContent = StreamHarness.Unique("primeiro");
        var betweenA = StreamHarness.Unique("entre-a");
        var betweenB = StreamHarness.Unique("entre-b");
        var afterContent = StreamHarness.Unique("depois");

        long firstSeq;
        using (var first = await SseConnection.OpenAsync(_host, owner))
        {
            var firstPostId = await StreamHarness.PublishAsync(owner, slug, firstContent);
            firstSeq = await StreamHarness.GlobalSeqAsync(_host, firstPostId);
            var seen = await first.ReadUntilAsync(Matches(firstContent), TimeSpan.FromSeconds(5));
            var frame = Assert.Single(seen, Matches(firstContent));
            Assert.Equal(firstSeq.ToString(CultureInfo.InvariantCulture), frame.Id);
        }

        // While the subscriber is away the publisher keeps going. These two 201s, taken right
        // after the disconnect, are also half of AC-05: a subscriber that is gone never blocks
        // or fails a publish.
        var betweenAPostId = await StreamHarness.PublishAsync(owner, slug, betweenA);
        var betweenBPostId = await StreamHarness.PublishAsync(owner, slug, betweenB);

        using var back = await SseConnection.OpenAsync(
            _host, owner, lastEventId: firstSeq.ToString(CultureInfo.InvariantCulture));
        var delta = await back.ReadUntilAsync(Matches(betweenB), TimeSpan.FromSeconds(5));

        // Exactly the delta: the post AT the cursor never comes back, the two written while the
        // subscriber was away arrive, in seq order, each once (§9.4).
        var ids = delta.Where(frame => frame.Event == "post")
            .Select(frame => long.Parse(frame.Id!, CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(
            new[]
            {
                await StreamHarness.GlobalSeqAsync(_host, betweenAPostId),
                await StreamHarness.GlobalSeqAsync(_host, betweenBPostId),
            },
            ids);
        Assert.DoesNotContain(firstSeq, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // And the same connection keeps receiving live after the backlog (nothing else to reopen).
        var afterPostId = await StreamHarness.PublishAsync(owner, slug, afterContent);
        var live = await back.ReadUntilAsync(Matches(afterContent), TimeSpan.FromSeconds(5));
        var liveFrame = Assert.Single(live, Matches(afterContent));
        Assert.Equal(
            (await StreamHarness.GlobalSeqAsync(_host, afterPostId)).ToString(CultureInfo.InvariantCulture),
            liveFrame.Id);
    }

    [Fact] // AC-03
    public async Task Ac03_an_idle_connection_pings_on_the_injected_cadence_without_moving_the_cursor()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "sac03dono");
        using var stream = await SseConnection.OpenAsync(_host, owner);

        // The host runs Stream:PingSeconds=0.2 (the injectable interval of the AC): two
        // heartbeats must land inside a second, where the contract's 15 s would make this test sleep.
        var first = await ReadPingAsync(stream);
        Assert.Null(first.Id); // a heartbeat must never move a client's Last-Event-ID
        Assert.Equal("{}", first.Data);

        var second = await ReadPingAsync(stream);
        Assert.Equal("ping", second.Event);
    }

    [Fact] // AC-05 — a sessão
    public async Task Ac05_without_a_session_the_stream_answers_401_not_authenticated_and_starts_no_stream()
    {
        var anonymous = _host.NewClient();
        using var stream = await SseConnection.OpenAsync(_host, anonymous);

        Assert.Equal(HttpStatusCode.Unauthorized, stream.Status);
        Assert.Equal("application/json", stream.ContentType); // not text/event-stream
        var body = await stream.ReadBodyAsync();
        Assert.Contains("not_authenticated", body, StringComparison.Ordinal);
    }

    /// <summary>The next heartbeat frame, waiting no longer than the window allows.</summary>
    private static async Task<SseFrame> ReadPingAsync(SseConnection stream)
    {
        var seen = await stream.ReadUntilAsync(frame => frame.Event == "ping", TimeSpan.FromSeconds(2));
        var ping = seen.LastOrDefault(frame => frame.Event == "ping");
        Assert.True(ping is not null, "the connection must heartbeat within the configured interval");
        return ping!;
    }

    /// <summary>The frame carrying one unique content (pings and foreign backlog never match).</summary>
    private static Predicate<SseFrame> Matches(string content) =>
        frame => frame.Data is not null && frame.Data.Contains(content, StringComparison.Ordinal);
}
