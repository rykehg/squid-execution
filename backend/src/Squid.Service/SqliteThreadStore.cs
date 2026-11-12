using System.Globalization;
using Microsoft.Data.Sqlite;
using Squid.Api.Threads;
using ThreadState = Squid.Api.Threads.ThreadState; // System.Threading.ThreadState rides the implicit usings

namespace Squid.Service;

/// <summary>
/// <see cref="IThreadStore"/> over the migrated thread tables (SPEC-0010 section A): the total
/// order of <c>MessageSeq</c> is a compare-and-swap on the <c>version</c> column, executed by
/// one UPDATE — GAP-29's decision puts the serialization in SQLite, never in a grain (thread
/// does not federate, ADR-1 keeps Orleans off it). Reads are plain SQL over the same file the
/// host migrated before the first request.
/// </summary>
public sealed class SqliteThreadStore : IThreadStore
{
    /// <summary>How long a writer waits for the file lock instead of failing (two senders racing).</summary>
    private const int BusyTimeoutMilliseconds = 30_000;

    private readonly string _connectionString;

    /// <summary>Binds the store to the same SQLite file the composition root migrated.</summary>
    public SqliteThreadStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task CreateAsync(ThreadState thread, IReadOnlyList<ThreadParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(participants);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        // Thread row and participant rows land together or not at all: a thread with a missing
        // participant (or a participant pointing at no thread) is state nobody can reason about.
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO thread (id, kind, created_by, message_seq, created_at, version)
                    VALUES (@Id, @Kind, @CreatedBy, @MessageSeq, @CreatedAt, @Version)
                    """;
                insert.Parameters.AddWithValue("@Id", thread.Id);
                insert.Parameters.AddWithValue("@Kind", thread.Kind);
                insert.Parameters.AddWithValue("@CreatedBy", thread.CreatedBy);
                insert.Parameters.AddWithValue("@MessageSeq", thread.MessageSeq);
                insert.Parameters.AddWithValue("@CreatedAt",
                    thread.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("@Version", thread.Version);
                await insert.ExecuteNonQueryAsync();
            }

            foreach (var participant in participants)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO thread_participant (thread_id, actor_id, role, joined_at)
                    VALUES (@ThreadId, @ActorId, @Role, @JoinedAt)
                    """;
                insert.Parameters.AddWithValue("@ThreadId", participant.ThreadId);
                insert.Parameters.AddWithValue("@ActorId", participant.ActorId);
                insert.Parameters.AddWithValue("@Role", participant.Role);
                insert.Parameters.AddWithValue("@JoinedAt",
                    participant.JoinedAt.ToString("O", CultureInfo.InvariantCulture));
                await insert.ExecuteNonQueryAsync();
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19) // SQLITE_CONSTRAINT: the id already exists
        {
            throw new IOException(
                $"the thread id '{thread.Id}' already exists (insert only, never a rewrite)");
        }

        await transaction.CommitAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ThreadListItem>> ListForAsync(string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // One read answers the whole list: the join starts from the caller's own participant row
        // (threads they are not in cannot appear — FR-031) and carries every participant of the
        // threads that match, so the grouping below only closes each item. lastReadSeq is 0 while
        // the monotonic marker belongs to TASK-0036 (GAP-38: the field exists from here).
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.kind, t.created_by, t.message_seq, t.created_at, t.version, p.actor_id
            FROM thread AS t
            JOIN thread_participant AS p ON p.thread_id = t.id
            WHERE p.actor_id = @ActorId
            ORDER BY t.rowid
            """;
        command.Parameters.AddWithValue("@ActorId", actorId);

        var items = new List<ThreadListItem>();
        ThreadState? current = null;
        List<string>? actors = null;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            if (current is null || !string.Equals(current.Id, id, StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    items.Add(new ThreadListItem(current, actors!, LastReadSeq: 0));
                }

                current = new ThreadState(
                    id,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    DateTimeOffset.Parse(reader.GetString(4),
                        CultureInfo.InvariantCulture, DateTimeStyles.None),
                    reader.GetInt32(5));
                actors = new List<string>();
            }

            actors!.Add(reader.GetString(6));
        }

        if (current is not null)
        {
            items.Add(new ThreadListItem(current, actors!, LastReadSeq: 0));
        }

        return items;
    }

    /// <inheritdoc />
    public async Task<long?> NextMessageSeqAsync(string id, int expectedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        // One statement is the whole critical section: the WHERE is the CAS on version, the
        // RETURNING hands back exactly the number THIS update took — a racing sender either
        // waits for the lock and then fails the guard, or goes first and takes the number, so
        // the same sequence is never handed out twice (GAP-29).
        await using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE thread
            SET message_seq = message_seq + 1, version = version + 1
            WHERE id = @Id AND version = @Version
            RETURNING message_seq
            """;
        update.Parameters.AddWithValue("@Id", id);
        update.Parameters.AddWithValue("@Version", expectedVersion);

        var value = await update.ExecuteScalarAsync();
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Keeps contended writers waiting for the file lock instead of failing (see the CAS).</summary>
    private static async Task BusyTimeoutAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds}";
        await command.ExecuteScalarAsync();
    }
}
