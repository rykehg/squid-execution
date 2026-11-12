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

    /// <summary>Re-reads before giving up: the guard can only lose if the state moved under it.</summary>
    private const int Attempts = 3;

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

        // One read answers the whole list: the semi-join keeps only the threads the caller sits
        // in (threads they are not in cannot appear — FR-031), while the participant rows of
        // those threads are read IN FULL — filtering the outer p by actor_id (the first shape of
        // this query) collapsed every item to the caller's own seat, and data-model 2.7's
        // ParticipantActorIds is the whole list. lastReadSeq is 0 while the monotonic marker
        // belongs to TASK-0036 (GAP-38: the field exists from here).
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.kind, t.created_by, t.message_seq, t.created_at, t.version, p.actor_id
            FROM thread AS t
            JOIN thread_participant AS p ON p.thread_id = t.id
            WHERE t.id IN (SELECT thread_id FROM thread_participant WHERE actor_id = @ActorId)
            ORDER BY t.rowid, p.rowid
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

    /// <inheritdoc />
    public async Task<bool> IsParticipantAsync(string threadId, string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // One probe answers both halves of FR-031: a thread the actor is not in and a thread that
        // does not exist both have NO participant row, which is exactly what lets every route of
        // the thread refuse with the same 404 without revealing which case it was (SC-005) — and
        // the participant list itself never leaves this query.
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1 FROM thread_participant WHERE thread_id = @ThreadId AND actor_id = @ActorId
            )
            """;
        command.Parameters.AddWithValue("@ThreadId", threadId);
        command.Parameters.AddWithValue("@ActorId", actorId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0;
    }

    /// <inheritdoc />
    public async Task<ThreadSnapshot?> FindAsync(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        ThreadState? thread;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText =
                "SELECT id, kind, created_by, message_seq, created_at, version FROM thread WHERE id = @Id";
            read.Parameters.AddWithValue("@Id", threadId);
            await using var reader = await read.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            thread = new ThreadState(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                DateTimeOffset.Parse(reader.GetString(4),
                    CultureInfo.InvariantCulture, DateTimeStyles.None),
                reader.GetInt32(5));
        }

        // Every seat in join order (rowid): the participant routes need the roles and the
        // joined_at the succession of GAP-32 ranks, and a thread nobody is in cannot exist.
        await using (var seats = connection.CreateCommand())
        {
            seats.CommandText = """
                SELECT actor_id, role, joined_at
                FROM thread_participant
                WHERE thread_id = @ThreadId
                ORDER BY rowid
                """;
            seats.Parameters.AddWithValue("@ThreadId", threadId);
            var participants = new List<ThreadParticipant>();
            await using var reader = await seats.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                participants.Add(new ThreadParticipant(
                    threadId,
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTimeOffset.Parse(reader.GetString(2),
                        CultureInfo.InvariantCulture, DateTimeStyles.None)));
            }

            return new ThreadSnapshot(thread!, participants);
        }
    }

    /// <inheritdoc />
    public async Task AddParticipantAsync(ThreadParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        // Insert only, and a seat that already exists is simply kept: the primary key of 0002 is
        // one row per (thread, actor), and an add that races another add writes the same row.
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO thread_participant (thread_id, actor_id, role, joined_at)
            VALUES (@ThreadId, @ActorId, @Role, @JoinedAt)
            ON CONFLICT (thread_id, actor_id) DO NOTHING
            """;
        insert.Parameters.AddWithValue("@ThreadId", participant.ThreadId);
        insert.Parameters.AddWithValue("@ActorId", participant.ActorId);
        insert.Parameters.AddWithValue("@Role", participant.Role);
        insert.Parameters.AddWithValue("@JoinedAt",
            participant.JoinedAt.ToString("O", CultureInfo.InvariantCulture));
        await insert.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task<ParticipantRemoval> RemoveParticipantAsync(string threadId, string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        // BEGIN IMMEDIATE (deferred: false): the count, the delete and the succession below run
        // under the write lock, so two removals queue instead of racing each other's count — and
        // leaving the scope without commit rolls the whole refusal back, nothing written.
        await using var transaction = connection.BeginTransaction(deferred: false);

        string? createdBy;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT created_by FROM thread WHERE id = @Id";
            read.Parameters.AddWithValue("@Id", threadId);
            var found = await read.ExecuteScalarAsync();
            if (found is null or DBNull)
            {
                return ParticipantRemoval.NoSuchParticipant;
            }

            createdBy = (string)found;
        }

        await using (var exists = connection.CreateCommand())
        {
            exists.Transaction = transaction;
            exists.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM thread_participant WHERE thread_id = @ThreadId AND actor_id = @ActorId
                )
                """;
            exists.Parameters.AddWithValue("@ThreadId", threadId);
            exists.Parameters.AddWithValue("@ActorId", actorId);
            if (Convert.ToInt64(await exists.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
            {
                return ParticipantRemoval.NoSuchParticipant;
            }
        }

        int seats;
        await using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM thread_participant WHERE thread_id = @ThreadId";
            count.Parameters.AddWithValue("@ThreadId", threadId);
            seats = Convert.ToInt32(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        if (seats <= 1)
        {
            return ParticipantRemoval.LastParticipant; // the refusal of GAP-38: the thread keeps its last seat
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                "DELETE FROM thread_participant WHERE thread_id = @ThreadId AND actor_id = @ActorId";
            delete.Parameters.AddWithValue("@ThreadId", threadId);
            delete.Parameters.AddWithValue("@ActorId", actorId);
            if (await delete.ExecuteNonQueryAsync() == 0)
            {
                return ParticipantRemoval.NoSuchParticipant;
            }
        }

        if (string.Equals(actorId, createdBy, StringComparison.Ordinal))
        {
            // GAP-32 succession, inside the removal's own transaction: the oldest admin assumes
            // the creator seat, and with no admin left the oldest member becomes admin AND
            // creator — the invariant that a Group with participants always has an admin. The
            // string order of joined_at is chronological because every writer stores round-trip
            // UTC ("O"); rowid breaks the tie of rows born in the same request.
            string? successor;
            await using (var oldestAdmin = connection.CreateCommand())
            {
                oldestAdmin.Transaction = transaction;
                oldestAdmin.CommandText = """
                    SELECT actor_id FROM thread_participant
                    WHERE thread_id = @ThreadId AND role = @Admin
                    ORDER BY joined_at, rowid
                    LIMIT 1
                    """;
                oldestAdmin.Parameters.AddWithValue("@ThreadId", threadId);
                oldestAdmin.Parameters.AddWithValue("@Admin", ThreadRoles.Admin);
                successor = (string?)await oldestAdmin.ExecuteScalarAsync();
            }

            if (successor is null)
            {
                await using var oldestMember = connection.CreateCommand();
                oldestMember.Transaction = transaction;
                oldestMember.CommandText = """
                    SELECT actor_id FROM thread_participant
                    WHERE thread_id = @ThreadId
                    ORDER BY joined_at, rowid
                    LIMIT 1
                    """;
                oldestMember.Parameters.AddWithValue("@ThreadId", threadId);
                successor = (string?)await oldestMember.ExecuteScalarAsync();

                await using var promote = connection.CreateCommand();
                promote.Transaction = transaction;
                promote.CommandText =
                    "UPDATE thread_participant SET role = @Role WHERE thread_id = @ThreadId AND actor_id = @ActorId";
                promote.Parameters.AddWithValue("@Role", ThreadRoles.Admin);
                promote.Parameters.AddWithValue("@ThreadId", threadId);
                promote.Parameters.AddWithValue("@ActorId", successor ?? string.Empty);
                await promote.ExecuteNonQueryAsync();
            }

            await using var handover = connection.CreateCommand();
            handover.Transaction = transaction;
            handover.CommandText = "UPDATE thread SET created_by = @Successor WHERE id = @Id";
            handover.Parameters.AddWithValue("@Successor", successor ?? string.Empty);
            handover.Parameters.AddWithValue("@Id", threadId);
            await handover.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return ParticipantRemoval.Removed;
    }

    /// <inheritdoc />
    public async Task<bool> SetRoleAsync(string threadId, string actorId, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        await using var update = connection.CreateCommand();
        update.CommandText =
            "UPDATE thread_participant SET role = @Role WHERE thread_id = @ThreadId AND actor_id = @ActorId";
        update.Parameters.AddWithValue("@Role", role);
        update.Parameters.AddWithValue("@ThreadId", threadId);
        update.Parameters.AddWithValue("@ActorId", actorId);
        return await update.ExecuteNonQueryAsync() > 0;
    }

    /// <inheritdoc />
    public async Task<MessageState?> AppendMessageAsync(
        string threadId, string messageId, string authorActorId, string content, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorActorId);
        ArgumentNullException.ThrowIfNull(content);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            // BEGIN IMMEDIATE (deferred: false): two senders queue here for the write lock instead
            // of racing, so the guard below reads a version nobody can move before the UPDATE —
            // and leaving the scope without commit rolls the whole attempt back.
            await using var transaction = connection.BeginTransaction(deferred: false);

            long version;
            await using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT version FROM thread WHERE id = @Id";
                read.Parameters.AddWithValue("@Id", threadId);
                var found = await read.ExecuteScalarAsync();
                if (found is null or DBNull)
                {
                    return null; // no thread: the caller answers its 404, nothing was written
                }

                version = Convert.ToInt32(found, CultureInfo.InvariantCulture);
            }

            // The same version-guarded CAS NextMessageSeqAsync runs (GAP-29): message_seq and
            // version move together, and RETURNING hands back exactly the number THIS update took.
            long seq;
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE thread
                    SET message_seq = message_seq + 1, version = version + 1
                    WHERE id = @Id AND version = @Version
                    RETURNING message_seq
                    """;
                update.Parameters.AddWithValue("@Id", threadId);
                update.Parameters.AddWithValue("@Version", version);
                var value = await update.ExecuteScalarAsync();
                if (value is null or DBNull)
                {
                    continue; // the guard lost (should not happen under BEGIN IMMEDIATE): re-read
                }

                seq = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO message (id, thread_id, author_actor_id, content, seq, created_at)
                    VALUES (@Id, @ThreadId, @Author, @Content, @Seq, @CreatedAt)
                    """;
                insert.Parameters.AddWithValue("@Id", messageId);
                insert.Parameters.AddWithValue("@ThreadId", threadId);
                insert.Parameters.AddWithValue("@Author", authorActorId);
                insert.Parameters.AddWithValue("@Content", content);
                insert.Parameters.AddWithValue("@Seq", seq);
                insert.Parameters.AddWithValue("@CreatedAt",
                    createdAt.ToString("O", CultureInfo.InvariantCulture));
                await insert.ExecuteNonQueryAsync();
            }

            // Commit is the one step that makes the number and its row real at the same instant.
            await transaction.CommitAsync();
            return new MessageState(
                messageId, threadId, authorActorId, content, seq, createdAt);
        }

        throw new IOException(
            $"the message append of thread '{threadId}' kept conflicting after {Attempts} attempts");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageState>> ListMessagesAsync(string threadId, long since)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // The exact delta of §9.3: seq > since, one row per number, ORDER BY seq — the
        // (thread_id, seq) index of 0003 is what this reads through, so a live thread answers a
        // polling client with only what is new, never a repeat.
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, thread_id, author_actor_id, content, seq, created_at
            FROM message
            WHERE thread_id = @ThreadId AND seq > @Since
            ORDER BY seq
            """;
        command.Parameters.AddWithValue("@ThreadId", threadId);
        command.Parameters.AddWithValue("@Since", since);

        var items = new List<MessageState>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new MessageState(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                DateTimeOffset.Parse(reader.GetString(5),
                    CultureInfo.InvariantCulture, DateTimeStyles.None)));
        }

        return items;
    }

    /// <summary>Keeps contended writers waiting for the file lock instead of failing (see the CAS).</summary>
    private static async Task BusyTimeoutAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds}";
        await command.ExecuteScalarAsync();
    }
}
