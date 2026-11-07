using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace Squid.Persistence;

/// <summary>
/// <c>IGrainStorage</c> over a SQLite file (doc 05 §4.7 #1 and #2): the store name travels as a
/// parameter on every statement, and each write and clear is a compare-and-swap on the ETag. The
/// ETag is the row version encoded as base64 (GAP-04), compared exactly by
/// <c>WHERE Version = @Version</c>; a divergent expectation throws <c>InconsistentStateException</c>
/// and nothing is written.
/// </summary>
public sealed class SqliteGrainStorage : IGrainStorage
{
    private const string CreateSchema = """
        CREATE TABLE IF NOT EXISTS GrainState (
            StoreName TEXT NOT NULL,
            GrainId   TEXT NOT NULL,
            State     TEXT NOT NULL,
            Version   TEXT NOT NULL,
            PRIMARY KEY (StoreName, GrainId)
        )
        """;

    private static readonly string FirstVersion = EncodeVersion(1);

    private readonly string _connectionString;

    /// <summary>Opens <paramref name="connectionString"/> and ensures the state table exists.</summary>
    public SqliteGrainStorage(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = CreateSchema;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads the row of <paramref name="stateName"/> + <paramref name="grainId"/>; a missing row
    /// is <c>State = null</c> and <c>ETag = null</c>, never an exception.
    /// </summary>
    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        var grainKey = grainId.ToString();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT State, Version FROM GrainState WHERE StoreName = @StoreName AND GrainId = @GrainId";
        command.Parameters.AddWithValue("@StoreName", stateName);
        command.Parameters.AddWithValue("@GrainId", grainKey);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            grainState.State = default!;
            grainState.ETag = null;
            grainState.RecordExists = false;
            return;
        }

        grainState.State = JsonSerializer.Deserialize<T>(reader.GetString(0))!;
        grainState.ETag = reader.GetString(1);
        grainState.RecordExists = true;
    }

    /// <summary>
    /// Writes with compare-and-swap: a null expected ETag only ever inserts a new row, and a
    /// non-null one only updates the row still carrying exactly that version — otherwise
    /// <c>InconsistentStateException</c> and the row keeps the state already on disk.
    /// </summary>
    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        var grainKey = grainId.ToString();
        var expected = grainState.ETag;
        var newVersion = expected is null ? FirstVersion : NextVersion(expected);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@StoreName", stateName);
        command.Parameters.AddWithValue("@GrainId", grainKey);
        command.Parameters.AddWithValue("@State", JsonSerializer.Serialize(grainState.State, typeof(T)));
        command.Parameters.AddWithValue("@NewVersion", newVersion);

        int written;
        if (expected is null)
        {
            // No expected version: only a brand-new row may appear, and only once.
            command.CommandText = """
                INSERT INTO GrainState (StoreName, GrainId, State, Version)
                VALUES (@StoreName, @GrainId, @State, @NewVersion)
                ON CONFLICT (StoreName, GrainId) DO NOTHING
                """;
            written = await command.ExecuteNonQueryAsync();
        }
        else
        {
            // Exact ETag comparison in SQL (GAP-04): zero rows changed means a divergent version.
            command.CommandText = """
                UPDATE GrainState
                SET State = @State, Version = @NewVersion
                WHERE StoreName = @StoreName AND GrainId = @GrainId AND Version = @Version
                """;
            command.Parameters.AddWithValue("@Version", expected);
            written = await command.ExecuteNonQueryAsync();
        }

        if (written == 0)
        {
            var stored = await CurrentVersionAsync(connection, stateName, grainKey);
            throw Conflict(stateName, grainKey, expected, stored);
        }

        grainState.ETag = newVersion;
        grainState.RecordExists = true;
    }

    /// <summary>
    /// Deletes the row under the same ETag rule as the write; afterwards the read is absent again.
    /// </summary>
    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        var grainKey = grainId.ToString();
        var expected = grainState.ETag;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@StoreName", stateName);
        command.Parameters.AddWithValue("@GrainId", grainKey);

        if (expected is null)
        {
            // Nothing to clear when the row is already absent; an existing row without its ETag
            // is the same conflict a write would report.
            var stored = await CurrentVersionAsync(connection, stateName, grainKey);
            if (stored is not null) throw Conflict(stateName, grainKey, expected, stored);
        }
        else
        {
            command.CommandText = """
                DELETE FROM GrainState
                WHERE StoreName = @StoreName AND GrainId = @GrainId AND Version = @Version
                """;
            command.Parameters.AddWithValue("@Version", expected);
            if (await command.ExecuteNonQueryAsync() == 0)
            {
                var stored = await CurrentVersionAsync(connection, stateName, grainKey);
                throw Conflict(stateName, grainKey, expected, stored);
            }
        }

        grainState.State = default!;
        grainState.ETag = null;
        grainState.RecordExists = false;
    }

    private static async Task<string?> CurrentVersionAsync(SqliteConnection connection, string stateName, string grainKey)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM GrainState WHERE StoreName = @StoreName AND GrainId = @GrainId";
        command.Parameters.AddWithValue("@StoreName", stateName);
        command.Parameters.AddWithValue("@GrainId", grainKey);
        return (string?)await command.ExecuteScalarAsync(); // no row -> null
    }

    private static InconsistentStateException Conflict(string store, string grainKey, string? expected, string? stored) =>
        new(
            $"ETag mismatch for '{store}' / '{grainKey}': expected '{expected ?? "<none>"}', stored '{stored ?? "<absent>"}'.",
            stored,
            expected);

    /// <summary>First ETag of a row: version 1 encoded as base64 (GAP-04).</summary>
    private static string EncodeVersion(long version) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(version.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Next ETag: the stored version incremented by one, still encoded as base64.</summary>
    private static string NextVersion(string current)
    {
        var version = long.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(current)), CultureInfo.InvariantCulture);
        return EncodeVersion(version + 1);
    }
}
