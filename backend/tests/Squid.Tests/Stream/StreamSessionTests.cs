using System.Diagnostics;
using Squid.Api.Posts;
using ActivityEvent = Squid.Api.Posts.ActivityEvent; // System.Diagnostics ships one too

namespace Squid.Tests.Stream;

/// <summary>
/// The session loop of TASK-0026 driven directly: the first drain frames what the log already
/// holds, a publisher's wake delivers the new delta in seq order, and an Activity type the frame
/// map does not know yet (Delete, TASK-0030) plus an event the visibility gate refuses never
/// become frames — while both still leave the cursor behind them, so the next event arrives.
/// The frames are asserted byte for byte: that is contract §5 (event, id, data, blank line).
/// </summary>
public sealed class StreamSessionTests
{
    [Fact]
    public async Task A_wake_drains_the_delta_in_order_and_skips_what_the_event_map_or_the_gate_refuses()
    {
        var log = new FakeLog();
        log.Add(Create(1, """{"seq":1}"""));

        var hub = new StreamHub();
        using var subscription = hub.Subscribe();
        var session = new StreamSession(
            log,
            subscription,
            activity => Task.FromResult(activity.Seq != 3), // the gate refuses seq 3 only
            TimeSpan.FromSeconds(30), // far enough that no heartbeat can enter this assertion
            TimeProvider.System);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var seen = new List<string>();
        var reading = CollectAsync(session, seen, cancellation.Token);

        // The connect path: one drain, no wake needed for what the log already held.
        await UntilAsync(() => seen.Count == 1, "the first event must frame without any wake");

        // Three events land while the client is connected: only the mapped, visible one frames.
        log.Add(Activity(2, "Delete"));
        log.Add(Create(3, """{"seq":3}"""));
        log.Add(Create(4, """{"seq":4}"""));
        hub.Publish();

        await UntilAsync(() => seen.Count == 2, "the publisher's wake must deliver the new delta");
        cancellation.Cancel();
        try
        {
            await reading;
        }
        catch (OperationCanceledException)
        {
            // expected: the cancel above is how the collector is told to stop
        }

        Assert.Equal(new[]
        {
            "event: post\nid: 1\ndata: {\"seq\":1}\n\n",
            "event: post\nid: 4\ndata: {\"seq\":4}\n\n",
        }, seen);
    }

    [Fact]
    public async Task A_completed_subscription_ends_the_stream_instead_of_leaving_the_client_hanging()
    {
        var log = new FakeLog();
        log.Add(Create(1, """{"seq":1}"""));

        var hub = new StreamHub();
        var subscription = hub.Subscribe();
        var session = new StreamSession(
            log, subscription, _ => Task.FromResult(true), TimeSpan.FromSeconds(30), TimeProvider.System);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var seen = new List<string>();
        var reading = CollectAsync(session, seen, cancellation.Token);
        await UntilAsync(() => seen.Count == 1, "the backlog must frame once");

        subscription.Dispose(); // what the route's finally does when the host shuts the stream down
        await reading; // must COMPLETE through the completed channel — a hang fails here at 10 s

        Assert.Single(seen);
    }

    /// <summary>Collects every frame until the session ends or the token fires.</summary>
    private static async Task CollectAsync(StreamSession session, List<string> seen, CancellationToken cancellation)
    {
        await foreach (var frame in session.ReadAsync(0, cancellation))
        {
            seen.Add(frame);
        }
    }

    /// <summary>Polls a condition for at most five seconds, then says why it had to hold.</summary>
    private static async Task UntilAsync(Func<bool> condition, string because)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), because);
    }

    private static ActivityEvent Create(long seq, string payload) => Activity(seq, "Create", payload);

    private static ActivityEvent Activity(long seq, string type, string payload = "{}") =>
        new($"urn:test:{seq}", type, "urn:test:actor", "urn:test:post", seq, payload, DateTimeOffset.UtcNow);

    /// <summary>An in-memory log: the session only ever reads, and the test writes between wakes.</summary>
    private sealed class FakeLog : IActivityStore
    {
        private readonly object _gate = new();

        private readonly List<ActivityEvent> _events = [];

        public void Add(ActivityEvent activity)
        {
            lock (_gate)
            {
                _events.Add(activity);
            }
        }

        public Task<ActivityEvent> AppendAsync(ActivityEvent activity) =>
            throw new NotSupportedException("the session reads the log; nothing appends here");

        public Task<IReadOnlyList<ActivityEvent>> ReadAsync(long afterSeq = 0)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<ActivityEvent>>(_events
                    .Where(activity => activity.Seq > afterSeq)
                    .OrderBy(activity => activity.Seq)
                    .ToList());
            }
        }
    }
}
