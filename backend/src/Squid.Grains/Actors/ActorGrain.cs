namespace Squid.Grains.Actors;

/// <summary>
/// Event-sourced identity aggregate of SPEC-0007 B-08: one log per handle, a snapshot when the
/// log reaches the threshold, and truncation of the very events that snapshot covers (doc 05
/// §4.7 #3). Every command persists first and reports the direct state; a fresh instance reading
/// the same store rebuilds an identical state from snapshot + events (AC-01, AC-04).
/// It is a plain class on purpose: <c>Squid.Grains</c> carries no Orleans package and may not
/// gain one inside this task's scope, so the composition root registers it (or wraps it in a
/// grain) without this module knowing anything about a silo.
/// </summary>
public sealed class ActorGrain
{
    /// <summary>Default number of open events that triggers a snapshot (AC-03 is reachable with it).</summary>
    public const int DefaultSnapshotThreshold = 8;

    private readonly IActorEventStore _store;
    private readonly int _snapshotThreshold;

    /// <summary>Builds the aggregate that snapshots at <see cref="DefaultSnapshotThreshold"/>.</summary>
    public ActorGrain(IActorEventStore store)
        : this(store, DefaultSnapshotThreshold)
    {
    }

    /// <summary>Builds the aggregate that snapshots when <paramref name="snapshotThreshold"/> events sit in the open log.</summary>
    public ActorGrain(IActorEventStore store, int snapshotThreshold)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _snapshotThreshold = snapshotThreshold >= 1
            ? snapshotThreshold
            : throw new ArgumentOutOfRangeException(
                nameof(snapshotThreshold), snapshotThreshold, "the snapshot threshold must be at least 1");
    }

    /// <summary>Threshold this instance snapshots at.</summary>
    public int SnapshotThreshold => _snapshotThreshold;

    /// <summary>
    /// Creates the actor (AC-01, AC-02): the handle is validated first, then refused when the
    /// store already knows it — in both rejections nothing is written — and otherwise the single
    /// <see cref="ActorCreated"/> event is appended and persisted. The returned state is the
    /// direct state; replaying what was just written produces the same record.
    /// </summary>
    public async Task<ActorState> CreateAsync(string handle, string? displayName, ActorKeyMaterial keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (!ActorHandle.IsValid(handle))
        {
            throw new InvalidActorHandleException(handle ?? string.Empty);
        }

        var existing = await _store.ReadAsync(handle);
        if (existing is not null)
        {
            throw new ActorHandleTakenException(handle);
        }

        return await AppendAsync(handle, new ActorEventStream(), expectedETag: null,
            new ActorCreated(handle, displayName, keys));
    }

    /// <summary>
    /// Changes the display name of an existing actor. A handle the store does not know is
    /// <see cref="ActorNotFoundException"/>, never a half-created stream.
    /// </summary>
    public async Task<ActorState> ChangeDisplayNameAsync(string handle, string displayName)
    {
        var record = await _store.ReadAsync(handle) ?? throw new ActorNotFoundException(handle);
        return await AppendAsync(handle, record.Stream, record.ETag, new ActorDisplayNameChanged(displayName));
    }

    /// <summary>Rebuilds the state of <paramref name="handle"/> from snapshot + log; null when it was never written.</summary>
    public async Task<ActorState?> GetStateAsync(string handle)
    {
        var record = await _store.ReadAsync(handle);
        return record is null ? null : Replay(record.Stream);
    }

    /// <summary>Folds the open log over the snapshot (or over nothing, while no snapshot exists).</summary>
    private static ActorState? Replay(ActorEventStream stream)
    {
        var state = stream.Snapshot;
        foreach (var evt in stream.Events)
        {
            state = Apply(state, evt);
        }

        return state;
    }

    /// <summary>Applies one event; a stream that does not fit its state is corrupt — this grain never writes one.</summary>
    private static ActorState Apply(ActorState? state, ActorEvent evt)
    {
        switch (evt)
        {
            case ActorCreated created when state is null:
                return new ActorState(created.Handle, created.DisplayName, created.Keys);

            case ActorDisplayNameChanged renamed when state is not null:
                return state with { DisplayName = renamed.DisplayName };

            default:
                throw new InvalidOperationException(
                    $"event {evt.GetType().Name} does not fit the actor state it was replayed onto");
        }
    }

    /// <summary>
    /// Applies the event, snapshots when the open log reaches the threshold — writing the folded
    /// state and clearing the events it covers, which is the truncation of AC-03 — and persists
    /// the row with the ETag the read handed over.
    /// </summary>
    private async Task<ActorState> AppendAsync(
        string handle, ActorEventStream stream, string? expectedETag, ActorEvent evt)
    {
        var state = Apply(Replay(stream), evt);

        stream.Events.Add(evt);
        if (stream.Events.Count >= _snapshotThreshold)
        {
            stream.Snapshot = state;
            stream.SnapshotVersion = stream.Version;
            stream.Events.Clear();
        }

        await _store.WriteAsync(handle, stream, expectedETag);
        return state;
    }
}
