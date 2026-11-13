using System.Security.Cryptography;

namespace Squid.Grains.Actors;

/// <summary>
/// Event-sourced identity aggregate of SPEC-0007 B-08: one log per handle, a snapshot when the
/// log reaches the threshold, and truncation of the very events that snapshot covers (doc 05
/// §4.7 #3). Every command persists first and reports the direct state; a fresh instance reading
/// the same store rebuilds an identical state from snapshot + events (AC-01, AC-04).
/// It is a plain class on purpose: <c>Squid.Grains</c> carries no Orleans package and may not
/// gain one inside this task's scope, so the composition root registers it (or wraps it in a
/// grain) without this module knowing anything about a silo.
/// SPEC-0011 section B (GAP-09) adds one duty: activating the runtime signing key of an actor —
/// unwrapped with the configured <c>Squid:RecoveryKey</c> and kept only in this instance.
/// </summary>
public sealed class ActorGrain
{
    /// <summary>Default number of open events that triggers a snapshot (AC-03 is reachable with it).</summary>
    public const int DefaultSnapshotThreshold = 8;

    private readonly IActorEventStore _store;
    private readonly int _snapshotThreshold;

    /// <summary>Runtime key ring of the composition root; null when the test built the grain without one.</summary>
    private readonly IActorSigningKeyRing? _signingKeys;

    /// <summary>The activated private key itself — memory only, never a field of any state or record.</summary>
    private RSA? _signingKey;

    /// <summary>Builds the aggregate with no runtime key: federated output disabled (SPEC-0011 section B).</summary>
    public ActorGrain(IActorEventStore store)
        : this(store, DefaultSnapshotThreshold, null)
    {
    }

    /// <summary>Builds the aggregate that snapshots at <paramref name="snapshotThreshold"/> events, without a runtime key.</summary>
    public ActorGrain(IActorEventStore store, int snapshotThreshold)
        : this(store, snapshotThreshold, null)
    {
    }

    /// <summary>Builds the aggregate bound to the runtime key ring the composition root registered.</summary>
    public ActorGrain(IActorEventStore store, IActorSigningKeyRing? signingKeys)
        : this(store, DefaultSnapshotThreshold, signingKeys)
    {
    }

    private ActorGrain(IActorEventStore store, int snapshotThreshold, IActorSigningKeyRing? signingKeys)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _snapshotThreshold = snapshotThreshold >= 1
            ? snapshotThreshold
            : throw new ArgumentOutOfRangeException(
                nameof(snapshotThreshold), snapshotThreshold, "the snapshot threshold must be at least 1");
        _signingKeys = signingKeys;
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

    /// <summary>
    /// Activation of the runtime signing key (SPEC-0011 section B, GAP-09, AC-02/AC-03): opens the
    /// wrapped RSA private key of <paramref name="handle"/> with the configured
    /// <c>Squid:RecoveryKey</c> and keeps it only in this instance's memory — the returned signer
    /// signs, it never hands the key back. Returns null when no secret is configured: the federated
    /// output is disabled while local reads and writes keep working (AC-01). A secret that does not
    /// open the key fails here as <see cref="SigningKeyActivationException"/> with no key material in
    /// the message (AC-03); an unknown handle stays <see cref="ActorNotFoundException"/>.
    /// </summary>
    public async Task<ActorSigner?> ActivateSigningKeyAsync(string handle)
    {
        if (_signingKeys is null)
        {
            return null; // no runtime secret: only the federated output stays disabled
        }

        var state = await GetStateAsync(handle) ?? throw new ActorNotFoundException(handle);
        if (_signingKey is null)
        {
            try
            {
                _signingKey = _signingKeys.OpenSigningKey(state.Keys.WrappedSigningKey);
            }
            catch (Exception failure) when (failure is CryptographicException or FormatException or ArgumentException)
            {
                // Auditability without leakage: the typed failure names the actor and the cause,
                // and neither the wrapped bytes nor the configured secret travel with it.
                throw new SigningKeyActivationException(handle, failure);
            }
        }

        return _signingKey is null ? null : new ActorSigner(_signingKey);
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
