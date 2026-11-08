using Microsoft.Data.Sqlite;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Grains.Actors;
using Squid.Persistence;

namespace Squid.Tests.Actors;

/// <summary>
/// <see cref="IActorEventStore"/> over the real SQLite <c>IGrainStorage</c> with ETag of TASK-0003,
/// so the suite persists through the production storage instead of a fake. It lives in the test
/// tree on purpose: ADR-35 forbids <c>Squid.Grains</c> from referencing <c>Squid.Persistence</c>,
/// so the composition root ships this same adapter when it registers the actor (TASK-0014).
/// Store name travels as the <c>stateName</c> parameter, never inside SQL (doc 05 §4.7 #2).
/// </summary>
public sealed class SqliteActorEventStore : IActorEventStore
{
    /// <summary>Store name of the actor rows (the stateName argument of the storage).</summary>
    public const string StoreName = "actors";

    private readonly string _connectionString;
    private readonly SqliteGrainStorage _storage;

    /// <summary>Opens <paramref name="connectionString"/> and creates the state table if needed.</summary>
    public SqliteActorEventStore(string connectionString)
    {
        _connectionString = connectionString;
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

    /// <summary>Rows that exist for this store — "nothing was persisted" evidence for AC-02.</summary>
    public async Task<int> CountRowsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM GrainState WHERE StoreName = @StoreName";
        command.Parameters.AddWithValue("@StoreName", StoreName);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>The JSON exactly as it sits in the State column — what AC-03 and AC-05 inspect.</summary>
    public async Task<string?> ReadPersistedJsonAsync(string handle)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT State FROM GrainState WHERE StoreName = @StoreName AND GrainId = @GrainId";
        command.Parameters.AddWithValue("@StoreName", StoreName);
        command.Parameters.AddWithValue("@GrainId", ActorId(handle).ToString());
        return (string?)await command.ExecuteScalarAsync();
    }
}
