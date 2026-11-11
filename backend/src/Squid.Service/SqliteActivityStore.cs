using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Posts;

namespace Squid.Service;

/// <summary>
/// <see cref="IActivityStore"/> over the same SQLite file everything else writes (pattern
/// <c>SqlitePolicyAuditStore</c>). The log itself stays the one keyed document it was born as —
/// the persisted shape and the raw-payload guarantee of QA-0022 are untouched — but the global
/// counter of GAP-23 moved behind the interface into the migration's single-row table (TASK-0024):
/// <c>activity_counter</c> advances through one atomic SQL update, inside the same transaction
/// that appends the event, so a number is only ever handed out together with the event carrying
/// it. <see cref="ReadAsync"/> keeps its delta semantics (<c>Seq &gt; afterSeq</c>, in order).
/// </summary>
public sealed class SqliteActivityStore : IActivityStore
{
    /// <summary>Store name of the event log (one document with every event).</summary>
    public const string StoreName = "activities";

    /// <summary>The GAP-23 counter table (schema/0001_init_projections.sql): one row, id = 1.</summary>
    private const string CounterTable = "activity_counter";

    /// <summary>How long a writer waits for the file lock instead of failing (two publishes racing).</summary>
    private const int BusyTimeoutMilliseconds = 30_000;

    private readonly IGrainStorage _storage;

    private readonly string _connectionString;

    /// <summary>Binds the log to the storage the host already created and to the migrated file.</summary>
    public SqliteActivityStore(IGrainStorage storage, string connectionString)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    private static GrainId Row() => GrainId.Create("activity", "log");

    /// <inheritdoc />
    public async Task<ActivityEvent> AppendAsync(ActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        // One immediate-write transaction holds read → increment → write: SQLite serializes the
        // writers, so the same number is never handed out twice, and a number never survives a
        // write that did not land — the log keeps exactly the contiguous numbering its own test
        // holds it to, while the counter itself lives in SQL (GAP-23).
        await using var transaction = connection.BeginTransaction(deferred: false);

        var (state, version) = await ReadRowAsync(connection, transaction);
        var document = state is null
            ? new ActivityDocument(0, [])
            : JsonSerializer.Deserialize<ActivityDocument>(state)
                ?? throw new IOException("the stored activity log document will not parse");

        // The counter owns the next number; the floor keeps a database whose log predates the
        // migration (counter still behind the document) from repeating a number already given out.
        var seq = await NextSeqAsync(connection, transaction, document.LastSeq);
        var stored = activity with { Seq = seq };
        document.Events.Add(stored);
        var numbered = document with { LastSeq = seq };

        await WriteRowAsync(connection, transaction, JsonSerializer.Serialize(numbered), version);
        await transaction.CommitAsync();
        return stored;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActivityEvent>> ReadAsync(long afterSeq = 0)
    {
        var state = new GrainState<ActivityDocument>();
        await _storage.ReadStateAsync(StoreName, Row(), state);
        var events = state.State?.Events;
        return events is null
            ? []
            : events.Where(activity => activity.Seq > afterSeq)
                .OrderBy(activity => activity.Seq)
                .ToList();
    }

    /// <summary>Reads the log document (and its ETag form) inside the append transaction.</summary>
    private static async Task<(string? State, string? Version)> ReadRowAsync(
        SqliteConnection connection, SqliteTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT State, Version FROM GrainState WHERE StoreName = @StoreName AND GrainId = @GrainId";
        command.Parameters.AddWithValue("@StoreName", StoreName);
        command.Parameters.AddWithValue("@GrainId", Row().ToString());

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetString(0), reader.GetString(1)) : (null, null);
    }

    /// <summary>
    /// The atomic single-row increment of GAP-23: one UPDATE takes the counter to
    /// <c>max(counter, floor) + 1</c>, and the SELECT beside it (same transaction) reads the
    /// number this append owns. <paramref name="floor"/> is the last number the document already
    /// carries, so an existing log is never renumbered when the counter arrives after it.
    /// </summary>
    private static async Task<long> NextSeqAsync(
        SqliteConnection connection, SqliteTransaction transaction, long floor)
    {
        await using var increment = connection.CreateCommand();
        increment.Transaction = transaction;
        increment.CommandText = $"UPDATE {CounterTable} SET last_seq = MAX(last_seq, @Floor) + 1 WHERE id = 1";
        increment.Parameters.AddWithValue("@Floor", floor);
        if (await increment.ExecuteNonQueryAsync() == 0)
        {
            throw new IOException(
                $"the {CounterTable} row is missing — the versioned schema/0001 migration must run before the log is appended to");
        }

        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = $"SELECT last_seq FROM {CounterTable} WHERE id = 1";
        return Convert.ToInt64(await read.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes the document under the same ETag rule the storage layer uses: a row that is absent
    /// is inserted once, an existing row only moves when its stored version still matches — the
    /// compare-and-swap of doc 05 §4.7 #1, kept even though the transaction already serialized us.
    /// </summary>
    private static async Task WriteRowAsync(
        SqliteConnection connection, SqliteTransaction transaction, string state, string? version)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("@StoreName", StoreName);
        command.Parameters.AddWithValue("@GrainId", Row().ToString());
        command.Parameters.AddWithValue("@State", state);
        command.Parameters.AddWithValue("@NewVersion", NextVersion(version));

        int written;
        if (version is null)
        {
            command.CommandText = """
                INSERT INTO GrainState (StoreName, GrainId, State, Version)
                VALUES (@StoreName, @GrainId, @State, @NewVersion)
                ON CONFLICT (StoreName, GrainId) DO NOTHING
                """;
            written = await command.ExecuteNonQueryAsync();
        }
        else
        {
            command.CommandText = """
                UPDATE GrainState
                SET State = @State, Version = @NewVersion
                WHERE StoreName = @StoreName AND GrainId = @GrainId AND Version = @Version
                """;
            command.Parameters.AddWithValue("@Version", version);
            written = await command.ExecuteNonQueryAsync();
        }

        if (written == 0)
        {
            throw new IOException(
                $"the activity log row moved under the append (expected version '{version ?? "<absent>"}')");
        }
    }

    /// <summary>Keeps contended writers waiting for the file lock instead of failing (see the append).</summary>
    private static async Task BusyTimeoutAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds}";
        await command.ExecuteScalarAsync();
    }

    /// <summary>First ETag of a row: version 1 encoded as base64 (GAP-04), as the storage writes it.</summary>
    private static string EncodeVersion(long version) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(version.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Next ETag: the stored version incremented by one, still encoded as base64.</summary>
    private static string NextVersion(string? current)
    {
        if (current is null) return EncodeVersion(1);
        var version = long.Parse(
            Encoding.UTF8.GetString(Convert.FromBase64String(current)), CultureInfo.InvariantCulture);
        return EncodeVersion(version + 1);
    }

    /// <summary>The one row of the log: the global counter's last number beside a growing list.</summary>
    private sealed record ActivityDocument(long LastSeq, List<ActivityEvent> Events);
}
