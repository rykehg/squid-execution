using System.Threading.Channels;

namespace Squid.Api.Posts;

/// <summary>
/// The in-process fan-out of SPEC-0009 section D (FR-024, R-16): one bounded channel per
/// subscriber, no broker. <see cref="Publish"/> is a non-blocking <c>TryWrite</c> into every
/// channel — best effort with NO backpressure toward the author, so a subscriber that stopped
/// reading, or died, can never slow a post down or fail it (AC-05). The channel carries a wake,
/// never the event: the log (<see cref="IActivityStore"/>) is the source of truth and every
/// subscriber re-reads the delta on its own wake. A full channel drops the wake instead of
/// queueing it, and that cannot hide an event: the dropped wake was attempted while an older one
/// was still queued, and that older one's delta read happens strictly after both were written.
/// </summary>
public sealed class StreamHub
{
    private readonly object _gate = new();

    private readonly List<Channel<bool>> _subscribers = [];

    /// <summary>Registers a subscriber; disposing the returned subscription is how it leaves.</summary>
    public StreamSubscription Subscribe()
    {
        // Capacity 1: one pending wake is all a signal needs, so a stalled subscriber holds one
        // slot instead of an unbounded backlog of notices.
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            SingleReader = true, // one SSE loop per subscription
            SingleWriter = false, // any request may publish concurrently
        });
        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        return new StreamSubscription(this, channel);
    }

    /// <summary>Wakes every subscriber to re-read the log — never awaits, never throws for a dead one.</summary>
    public void Publish()
    {
        lock (_gate)
        {
            foreach (var channel in _subscribers)
            {
                // Full or already completed → the wake is dropped (or ignored), never buffered,
                // never blocking: this is the whole publisher-facing surface of the stream.
                channel.Writer.TryWrite(true);
            }
        }
    }

    internal void Unsubscribe(Channel<bool> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }
}

/// <summary>One subscriber's read side: the wake channel the SSE loop waits on, and its way out.</summary>
public sealed class StreamSubscription : IDisposable
{
    private readonly StreamHub _hub;

    private readonly Channel<bool> _channel;

    internal StreamSubscription(StreamHub hub, Channel<bool> channel)
    {
        _hub = hub;
        _channel = channel;
    }

    /// <summary>The wake channel; capacity 1, bounded as the hub created it.</summary>
    public ChannelReader<bool> Reader => _channel.Reader;

    /// <summary>Leaves the hub and completes the channel, which ends a wait the SSE loop may still be in.</summary>
    public void Dispose() => _hub.Unsubscribe(_channel);
}
