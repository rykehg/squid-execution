using Squid.Api.Posts;

namespace Squid.Tests.Stream;

/// <summary>
/// The publisher-facing contract of TASK-0026 AC-05 (second half): publishing to the hub never
/// blocks and never fails — a subscriber that stopped reading holds one bounded slot instead of
/// backing the author up, and a subscriber that already left is simply not in the fan-out.
/// </summary>
public sealed class StreamHubTests
{
    [Fact]
    public async Task Publish_never_blocks_on_a_subscriber_that_stops_reading_and_ignores_one_already_gone()
    {
        var hub = new StreamHub();
        using var stalled = hub.Subscribe(); // reads nothing: capacity 1 fills on the first wake
        var departed = hub.Subscribe();
        departed.Dispose(); // what the route's finally does when the client hangs up

        // A backpressured channel would hang this loop; WaitAsync turns that hang into a failure.
        var publishing = Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                hub.Publish();
            }
        });
        await publishing.WaitAsync(TimeSpan.FromSeconds(5));

        // The live subscriber holds exactly ONE queued wake: the other 9 999 were dropped, never
        // buffered (the next delta read they trigger covers whatever a dropped wake pointed at).
        Assert.True(stalled.Reader.TryRead(out _));
        Assert.False(stalled.Reader.TryRead(out _));

        // The subscriber that left is out of the fan-out for good, channel completed.
        Assert.True(departed.Reader.Completion.IsCompleted);

        // And a publish after everything is gone is a no-op, never an error.
        hub.Publish();
    }
}
