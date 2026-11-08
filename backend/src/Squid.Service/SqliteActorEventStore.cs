using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Grains.Actors;
using Squid.Persistence;

namespace Squid.Service;

/// <summary>
/// Production <see cref="IActorEventStore"/> over the SQLite <c>IGrainStorage</c> of TASK-0003 —
/// the composition root ships the adapter that today exists only in the test tree (backlog S-21),
/// because ADR-35 forbids <c>Squid.Grains</c> from referencing <c>Squid.Persistence</c>. It mirrors
/// <c>Squid.Tests/Actors/SqliteActorEventStore.cs</c> on purpose: same store name, same row key,
/// same ETag contract, so what the suite proves is what runs (doc 05 §4.7, store travels as the
/// <c>stateName</c> parameter, never inside SQL).
/// </summary>
public sealed class SqliteActorEventStore : IActorEventStore
{
    /// <summary>Store name of the actor rows (the stateName argument of the storage).</summary>
    public const string StoreName = "actors";

    private readonly SqliteGrainStorage _storage;

    /// <summary>Opens <paramref name="connectionString"/> and creates the state table if needed.</summary>
    public SqliteActorEventStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _storage = new SqliteGrainStorage(connectionString);
    }

    /// <summary>Row key of an actor: the handle travels as the grain key (uniqueness = the row itself).</summary>
    private static GrainId ActorId(string handle) => GrainId.Create("actor", handle);

    /// <inheritdoc />
    public async Task<ActorStreamRecord?> ReadAsync(string handle)
    {
        var state = new GrainState<ActorEventStream>();
        await _storage.ReadStateAsync(StoreName, ActorId(handle), state);
        return state.State is null ? null : new ActorStreamRecord(state.State, state.ETag);
    }

    /// <inheritdoc />
    public async Task<string> WriteAsync(string handle, ActorEventStream stream, string? expectedETag)
    {
        var state = new GrainState<ActorEventStream>(stream, expectedETag);
        try
        {
            await _storage.WriteStateAsync(StoreName, ActorId(handle), state);
        }
        catch (InconsistentStateException ex)
        {
            // The port carries its own conflict type: the grain layer cannot see Orleans types.
            throw new ActorStoreConflictException(handle, ex);
        }

        return state.ETag!;
    }
}
