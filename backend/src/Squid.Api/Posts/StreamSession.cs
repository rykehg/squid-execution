using System.Runtime.CompilerServices;

namespace Squid.Api.Posts;

/// <summary>
/// One subscriber's SSE session of SPEC-0009 section D: an <see cref="IAsyncEnumerable{T}"/> of
/// rendered frames, fed by the hub's wake channel (R-16: <c>Channel&lt;T&gt;</c> in, async
/// enumerable out, no broker). Every wake drains the exact delta of
/// <see cref="IActivityStore.ReadAsync(long)"/> (<c>Seq &gt; last</c>) — the same first pass that
/// serves a reconnect's <c>Last-Event-ID</c> backlog (contract §9.4), because the cursor is the
/// only state and the log is the only source of truth: frames leave in seq order, never repeat,
/// and a dropped wake only defers delivery to the next one. Delivery is filtered per event by the
/// caller's visibility gate (visibility/membership read at DELIVERY time, FR-026), and a skipped
/// event still moves the cursor past itself. Heartbeat frames run on their own deadline so a busy
/// stream keeps the ping cadence of contract §5.
/// </summary>
public sealed class StreamSession
{
    private readonly IActivityStore _activities;

    private readonly StreamSubscription _subscription;

    private readonly Func<ActivityEvent, Task<bool>> _deliverable;

    private readonly TimeSpan _ping;

    private readonly TimeProvider _time;

    /// <summary>
    /// Binds the session to the log, the subscriber's wake channel, the visibility gate of one
    /// event (<see cref="Func{T, TResult}"/> so this class never sees the container stack) and the
    /// configured heartbeat interval.
    /// </summary>
    public StreamSession(
        IActivityStore activities, StreamSubscription subscription,
        Func<ActivityEvent, Task<bool>> deliverable, TimeSpan ping, TimeProvider time)
    {
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        _deliverable = deliverable ?? throw new ArgumentNullException(nameof(deliverable));
        _ping = ping;
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// The frames of this connection, from <paramref name="afterSeq"/> (exclusive) onward: the
    /// delta drain first — which IS the reconnect backlog when the cursor comes from
    /// <c>Last-Event-ID</c> — then a wait that ends either in the next drain or in a heartbeat.
    /// Ends when the hub completes the channel; throws <see cref="OperationCanceledException"/>
    /// when <paramref name="cancellation"/> fires, which is the client hanging up.
    /// </summary>
    public async IAsyncEnumerable<string> ReadAsync(
        long afterSeq, [EnumeratorCancellation] CancellationToken cancellation)
    {
        var last = afterSeq;
        var nextPing = _time.GetUtcNow() + _ping;

        while (!cancellation.IsCancellationRequested)
        {
            // shortcut: ReadAsync parses the whole log document — one delta read per wake per
            // subscriber is fine while the log is MVP-sized; upgrade to a SQL window when events grow.
            foreach (var activity in await _activities.ReadAsync(last))
            {
                last = activity.Seq; // a skipped event still leaves the cursor behind it
                var eventName = EventName(activity.Type);
                if (eventName is null) continue; // only `post` maps today; a removal moves the cursor, never a frame
                if (!await _deliverable(activity)) continue; // visibility and membership as they stand NOW (AC-04)
                yield return Frame(eventName, activity.Seq, activity.Payload);
            }

            var delay = nextPing - _time.GetUtcNow();
            if (delay <= TimeSpan.Zero)
            {
                yield return Ping();
                nextPing = _time.GetUtcNow() + _ping;
                continue;
            }

            using var wake = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            wake.CancelAfter(delay);

            var timedOut = false;
            bool pending;
            try
            {
                pending = await _subscription.Reader.WaitToReadAsync(wake.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                // Only the heartbeat deadline fired: this is the idle connection pinging.
                pending = false;
                timedOut = true;
            }

            if (timedOut)
            {
                yield return Ping();
                nextPing = _time.GetUtcNow() + _ping;
                continue;
            }

            if (!pending) yield break; // the hub completed the channel: the host is going down

            // One wake is enough; the drain above takes everything the wake was pointing at.
            _subscription.Reader.TryRead(out _);
        }
    }

    /// <summary>
    /// The SSE event name of an Activity type — the one place another action joins the stream:
    /// <c>Create</c> is <c>post</c> (contract §5), and every later type adds one arm here without
    /// touching the loop, the frames or the hub. <c>Delete</c> deliberately maps to no frame:
    /// neither SPEC-0009 section D nor contract §5 speaks a delete event, so the removal of
    /// TASK-0030 reaches the projections and the trail only — the arm joins when a frame is specced.
    /// </summary>
    private static string? EventName(string activityType) => activityType switch
    {
        "Create" => "post",
        _ => null,
    };

    /// <summary>The frame of contract §5: named event, global Activity.Seq as id, summary JSON as data.</summary>
    private static string Frame(string eventName, long seq, string payload) =>
        $"event: {eventName}\nid: {seq}\ndata: {payload}\n\n";

    /// <summary>
    /// The heartbeat of contract §5 — no <c>id</c>, so a ping can never move a client's
    /// <c>Last-Event-ID</c>; <c>data</c> is present because an SSE parser drops an event that
    /// carries none, which would make the heartbeat invisible to the client's listener.
    /// </summary>
    private static string Ping() => "event: ping\ndata: {}\n\n";
}
