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
        // ParticipantActorIds is the whole list. The LEFT JOIN carries the READER's own marker of
        // 0004 (FR-033, born 0 with no row) — r is keyed to @ActorId alone, so it is the same
        // value on every row of a thread and only the first row of the group is ever read.
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.kind, t.created_by, t.message_seq, t.created_at, t.version, p.actor_id,
                   r.last_read_seq
            FROM thread AS t
            JOIN thread_participant AS p ON p.thread_id = t.id
            LEFT JOIN thread_read AS r ON r.thread_id = t.id AND r.actor_id = @ActorId
            WHERE t.id IN (SELECT thread_id FROM thread_participant WHERE actor_id = @ActorId)
            ORDER BY t.rowid, p.rowid
            """;
        command.Parameters.AddWithValue("@ActorId", actorId);

        var items = new List<ThreadListItem>();
        ThreadState? current = null;
        List<string>? actors = null;
        var lastRead = 0L;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            if (current is null || !string.Equals(current.Id, id, StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    items.Add(new ThreadListItem(current, actors!, LastReadSeq: lastRead));
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
                lastRead = reader.IsDBNull(7) ? 0L : reader.GetInt64(7);
            }

            actors!.Add(reader.GetString(6));
        }

        if (current is not null)
        {
            items.Add(new ThreadListItem(current, actors!, LastReadSeq: lastRead));
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
    public async Task<AppendResult> AppendMessageAsync(
        string threadId, string messageId, string authorActorId, string content, DateTimeOffset createdAt,
        string? replyToId = null)
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
                    return new AppendResult(AppendOutcome.NoSuchThread, null); // no thread: the 404, nothing written
                }

                version = Convert.ToInt32(found, CultureInfo.InvariantCulture);
            }

            // §9.6 gate, inside the attempt and BEFORE a number is taken: the ack of FR-032 is one
            // row per (thread, SENDER), so actor A's acceptance never unlocks actor B — and the
            // refusal consumes no sequence (leaving the scope without commit rolls back), which is
            // why the send after the ack still lands on the first free number.
            await using (var ack = connection.CreateCommand())
            {
                ack.Transaction = transaction;
                ack.CommandText = """
                    SELECT EXISTS(
                        SELECT 1 FROM thread_no_e2ee_ack WHERE thread_id = @ThreadId AND actor_id = @ActorId
                    )
                    """;
                ack.Parameters.AddWithValue("@ThreadId", threadId);
                ack.Parameters.AddWithValue("@ActorId", authorActorId);
                var accepted = Convert.ToInt64(
                    await ack.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0;
                if (!accepted)
                {
                    return new AppendResult(AppendOutcome.AckRequired, null);
                }
            }

            // SPEC-0010 section E (FR-057, §9.9), inside the same attempt and AFTER the gate:
            // contract §6 makes 428 absolute ("sem aceite, o envio responde 428"), so a send that
            // misses BOTH the acceptance and the target is answered 428. Another thread's message
            // and a message that does not exist share this ONE lookup, so the 422 never tells a
            // participant which case it was — and refusing before the CAS means no sequence is
            // taken and no row is written (the scope rolls back without commit).
            if (replyToId is not null)
            {
                await using (var target = connection.CreateCommand())
                {
                    target.Transaction = transaction;
                    target.CommandText = "SELECT thread_id FROM message WHERE id = @ReplyToId";
                    target.Parameters.AddWithValue("@ReplyToId", replyToId);
                    var owner = await target.ExecuteScalarAsync();
                    if (!string.Equals(owner as string, threadId, StringComparison.Ordinal))
                    {
                        return new AppendResult(AppendOutcome.InvalidReplyTarget, null);
                    }
                }
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
                    INSERT INTO message (id, thread_id, author_actor_id, content, seq, created_at, reply_to_id)
                    VALUES (@Id, @ThreadId, @Author, @Content, @Seq, @CreatedAt, @ReplyToId)
                    """;
                insert.Parameters.AddWithValue("@Id", messageId);
                insert.Parameters.AddWithValue("@ThreadId", threadId);
                insert.Parameters.AddWithValue("@Author", authorActorId);
                insert.Parameters.AddWithValue("@Content", content);
                insert.Parameters.AddWithValue("@Seq", seq);
                insert.Parameters.AddWithValue("@CreatedAt",
                    createdAt.ToString("O", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("@ReplyToId", (object?)replyToId ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync();
            }

            // Commit is the one step that makes the number and its row real at the same instant.
            await transaction.CommitAsync();
            return new AppendResult(AppendOutcome.Appended,
                new MessageState(messageId, threadId, authorActorId, content, seq, createdAt, replyToId));
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
        // polling client with only what is new, never a repeat. reply_to_id (0005) rides along:
        // null when the message answers nothing (FR-057), the reference the row was written with.
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, thread_id, author_actor_id, content, seq, created_at, reply_to_id
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
                    CultureInfo.InvariantCulture, DateTimeStyles.None),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return items;
    }

    /// <inheritdoc />
    public async Task<bool> MessageVisibleAsync(string threadId, string messageId, string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // One probe for the three facts of the reaction wall (FR-031 + SPEC-0010 section F): the
        // thread row, the caller's seat in it and the message OF THAT thread — all three misses
        // read the same false, which is what lets an absent thread, a stranger and a message of
        // another thread share ONE byte-identical 404 without the answer ever telling them apart
        // (SC-005), and what keeps reactions and counts inside §9.10's participant wall.
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM message AS m
                WHERE m.id = @MessageId
                  AND m.thread_id = @ThreadId
                  AND EXISTS(
                      SELECT 1 FROM thread_participant
                      WHERE thread_id = @ThreadId AND actor_id = @ActorId
                  )
            )
            """;
        command.Parameters.AddWithValue("@ThreadId", threadId);
        command.Parameters.AddWithValue("@MessageId", messageId);
        command.Parameters.AddWithValue("@ActorId", actorId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0;
    }

    /// <inheritdoc />
    public async Task<ReactionOutcome> AddReactionAsync(
        string threadId, string messageId, string actorId, string emoji, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(emoji);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        await using var transaction = connection.BeginTransaction(deferred: false);

        // The target check INSIDE the attempt (the defensive twin of MessageVisibleAsync): a
        // message that left or belongs to another thread means nothing is written and the caller
        // answers the byte-identical 404 — decided before the insert, so no row can be born for a
        // target nobody can see.
        await using (var target = connection.CreateCommand())
        {
            target.Transaction = transaction;
            target.CommandText =
                "SELECT EXISTS(SELECT 1 FROM message WHERE id = @MessageId AND thread_id = @ThreadId)";
            target.Parameters.AddWithValue("@MessageId", messageId);
            target.Parameters.AddWithValue("@ThreadId", threadId);
            if (Convert.ToInt64(await target.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
            {
                return ReactionOutcome.Missing;
            }
        }

        int affected;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO message_reaction (message_id, actor_id, emoji, kind, created_at)
                VALUES (@MessageId, @ActorId, @Emoji, @Kind, @CreatedAt)
                ON CONFLICT (message_id, actor_id, emoji) DO NOTHING
                """;
            insert.Parameters.AddWithValue("@MessageId", messageId);
            insert.Parameters.AddWithValue("@ActorId", actorId);
            insert.Parameters.AddWithValue("@Emoji", emoji);
            // data-model section 2.14 Kind: the default 👍 is Like, every other whitelisted emoji
            // is Custom — the word travels with the row, no federation distinction at this phase.
            insert.Parameters.AddWithValue("@Kind",
                emoji == ReactionOptions.Like ? ReactionKinds.Like : ReactionKinds.Custom);
            insert.Parameters.AddWithValue("@CreatedAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
            affected = await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        // The idempotence of AC-01 IS this number: 1 = the row is new, 0 = the SAME (message,
        // actor, emoji) already existed, so the primary key of 0006 kept the second attempt from
        // writing anything and the count a reader derives cannot move on a repeat.
        return affected > 0 ? ReactionOutcome.Created : ReactionOutcome.Repeated;
    }

    /// <inheritdoc />
    public async Task<ReactionOutcome> RemoveReactionAsync(
        string threadId, string messageId, string actorId, string emoji)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(emoji);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        await using var transaction = connection.BeginTransaction(deferred: false);

        // Same defensive twin as the append: the message must still belong to this thread, or the
        // refusal is the ONE 404 and nothing is touched.
        await using (var target = connection.CreateCommand())
        {
            target.Transaction = transaction;
            target.CommandText =
                "SELECT EXISTS(SELECT 1 FROM message WHERE id = @MessageId AND thread_id = @ThreadId)";
            target.Parameters.AddWithValue("@MessageId", messageId);
            target.Parameters.AddWithValue("@ThreadId", threadId);
            if (Convert.ToInt64(await target.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
            {
                return ReactionOutcome.Missing;
            }
        }

        int affected;
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            // Only the ACTOR's own row for this emoji: somebody else's reaction of the same emoji
            // is a different row and survives (one row per (message, actor, emoji)).
            delete.CommandText = """
                DELETE FROM message_reaction
                WHERE message_id = @MessageId AND actor_id = @ActorId AND emoji = @Emoji
                """;
            delete.Parameters.AddWithValue("@MessageId", messageId);
            delete.Parameters.AddWithValue("@ActorId", actorId);
            delete.Parameters.AddWithValue("@Emoji", emoji);
            affected = await delete.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        // Rows leave the table, they are never decremented anywhere: 0 means "no such reaction"
        // (AC-03's 404) and the count of the next read simply reflects the rows that remain.
        return affected > 0 ? ReactionOutcome.Removed : ReactionOutcome.Missing;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReactionCount>> ListReactionsAsync(
        string threadId, long since, string readerActorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(readerActorId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // The derived projection of section F over the SAME delta the message read walks: one row
        // per (message, emoji) with COUNT(*) of the rows that exist right now and whether the
        // READER holds one of them — computed here, on every read, so there is no stored number
        // anywhere that could drift (data-model section 2.14: projection, never source). The join
        // through message carries the thread filter, and MIN(rowid) keeps the first-seen order.
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.message_id, r.emoji, COUNT(*) AS total,
                   SUM(CASE WHEN r.actor_id = @Reader THEN 1 ELSE 0 END) AS mine
            FROM message_reaction AS r
            JOIN message AS m ON m.id = r.message_id
            WHERE m.thread_id = @ThreadId AND m.seq > @Since
            GROUP BY r.message_id, r.emoji
            ORDER BY MIN(r.rowid)
            """;
        command.Parameters.AddWithValue("@ThreadId", threadId);
        command.Parameters.AddWithValue("@Since", since);
        command.Parameters.AddWithValue("@Reader", readerActorId);

        return await ReadCountsAsync(command);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReactionCount>> ReactionsOfAsync(
        string threadId, string messageId, string readerActorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(readerActorId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // The same projection for ONE message — the answer POST/DELETE .../reactions return right
        // after the effect, so what the caller reads back is COUNT(*) of the rows as they are NOW.
        // The join re-checks the thread, so a message of another thread projects nothing (404 up
        // front anyway, this is the defensive twin).
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.message_id, r.emoji, COUNT(*) AS total,
                   SUM(CASE WHEN r.actor_id = @Reader THEN 1 ELSE 0 END) AS mine
            FROM message_reaction AS r
            JOIN message AS m ON m.id = r.message_id AND m.thread_id = @ThreadId
            WHERE r.message_id = @MessageId
            GROUP BY r.emoji
            ORDER BY MIN(r.rowid)
            """;
        command.Parameters.AddWithValue("@ThreadId", threadId);
        command.Parameters.AddWithValue("@MessageId", messageId);
        command.Parameters.AddWithValue("@Reader", readerActorId);

        return await ReadCountsAsync(command);
    }

    /// <summary>Materializes one projected (message, emoji) group — the two reads above share only this.</summary>
    private static async Task<IReadOnlyList<ReactionCount>> ReadCountsAsync(SqliteCommand command)
    {
        var counts = new List<ReactionCount>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts.Add(new ReactionCount(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3) != 0));
        }

        return counts;
    }

    /// <inheritdoc />
    public async Task AckNoE2eeAsync(string threadId, string actorId, string notice, DateTimeOffset ackedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(notice);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        // Insert only, and a repeat keeps the FIRST ack: the primary key of 0004 is what makes
        // the route idempotent (AC-01) — two acks racing write the same row, neither rewrites it.
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO thread_no_e2ee_ack (thread_id, actor_id, acked_at, notice)
            VALUES (@ThreadId, @ActorId, @AckedAt, @Notice)
            ON CONFLICT (thread_id, actor_id) DO NOTHING
            """;
        insert.Parameters.AddWithValue("@ThreadId", threadId);
        insert.Parameters.AddWithValue("@ActorId", actorId);
        insert.Parameters.AddWithValue("@AckedAt", ackedAt.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("@Notice", notice);
        await insert.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task<long> MarkReadAsync(string threadId, string actorId, long seq)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await BusyTimeoutAsync(connection);

        // One statement is the whole marker rule (GAP-38): the upsert only MOVES the value when
        // the request is ahead of it, so a smaller seq changes nothing and never regresses — and
        // the read below answers with the value in force even when the WHERE kept the old one.
        await using (var mark = connection.CreateCommand())
        {
            mark.CommandText = """
                INSERT INTO thread_read (thread_id, actor_id, last_read_seq)
                VALUES (@ThreadId, @ActorId, @Seq)
                ON CONFLICT (thread_id, actor_id) DO UPDATE
                SET last_read_seq = excluded.last_read_seq
                WHERE excluded.last_read_seq > thread_read.last_read_seq
                """;
            mark.Parameters.AddWithValue("@ThreadId", threadId);
            mark.Parameters.AddWithValue("@ActorId", actorId);
            mark.Parameters.AddWithValue("@Seq", seq);
            await mark.ExecuteNonQueryAsync();
        }

        await using var read = connection.CreateCommand();
        read.CommandText =
            "SELECT last_read_seq FROM thread_read WHERE thread_id = @ThreadId AND actor_id = @ActorId";
        read.Parameters.AddWithValue("@ThreadId", threadId);
        read.Parameters.AddWithValue("@ActorId", actorId);
        var value = await read.ExecuteScalarAsync();
        return value is null or DBNull
            ? 0L
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<long> LastReadSeqAsync(string threadId, string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // No row = the marker born 0 (FR-033); the caller already probed participation, so this
        // value only ever leaves through the participant's own read routes (section D).
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT last_read_seq FROM thread_read WHERE thread_id = @ThreadId AND actor_id = @ActorId";
        command.Parameters.AddWithValue("@ThreadId", threadId);
        command.Parameters.AddWithValue("@ActorId", actorId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull
            ? 0L
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
