using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Posts;
using Squid.Domain.Objects;

namespace Squid.Service;

/// <summary>
/// <see cref="IActorStatusStore"/> over the same SQLite <c>IGrainStorage</c> (pattern
/// <c>SqliteAuthStore</c>): no new table and no migration — one keyed document per handle, and
/// no document at all is the §3.1 default <see cref="ActorStatus.Active"/>, so registration and
/// every other phase keep writing nothing here until an actor is actually suspended.
/// </summary>
public sealed class SqliteActorStatusStore : IActorStatusStore
{
    /// <summary>Store name of the status rows (one document per handle).</summary>
    public const string StoreName = "actor-status";

    /// <summary>Re-reads before giving up when the write races another writer.</summary>
    private const int Attempts = 5;

    private readonly IGrainStorage _storage;

    /// <summary>Binds the store to the storage the host already created (schema exists with it).</summary>
    public SqliteActorStatusStore(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    private static GrainId Row(string handle) => GrainId.Create("actorstatus", handle);

    /// <inheritdoc />
    public async Task<ActorStatus> GetAsync(string handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        var state = new GrainState<StatusDocument>();
        await _storage.ReadStateAsync(StoreName, Row(handle), state);
        return state.State?.Status ?? ActorStatus.Active;
    }

    /// <inheritdoc />
    public async Task SuspendAsync(string handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<StatusDocument>();
            await _storage.ReadStateAsync(StoreName, Row(handle), read);
            if (read.State is { Status: ActorStatus.Suspended }) return; // already suspended: one row, idempotent

            var write = new GrainState<StatusDocument>(
                new StatusDocument(ActorStatus.Suspended), read.ETag);
            try
            {
                await _storage.WriteStateAsync(StoreName, Row(handle), write);
                return;
            }
            catch (InconsistentStateException)
            {
                // Another writer moved the row in between: re-read it and apply again.
            }
        }

        throw new IOException($"the actor status of '{handle}' kept conflicting after {Attempts} attempts");
    }

    /// <summary>The status row: exactly the §3.1 state, nothing else rides in it.</summary>
    private sealed record StatusDocument(ActorStatus Status);
}
