using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Squid.ReadModels.Federation;

/// <summary>
/// One <c>peer_delivery</c> row as the pump reads it (data-model §2.9, SPEC-0011 section G).
/// Dates travel as the ISO-8601 "O" text the store writes and integers as Int64 (Dapper's exact
/// binding rule); the record cannot borrow <c>Squid.Api</c>'s <c>PeerDelivery</c> because this
/// project may not reference the edge (the dependency rule), so the pump maps it over.
/// </summary>
public sealed record PeerQueueRow(
    string ActivityId,
    string RecipientActorId,
    string Activity,
    string ObjectId,
    string Payload,
    long PolicyVersion,
    long Attempts,
    string NextAttemptAt,
    string State,
    string? Reason);

/// <summary>
/// The queue/DLQ read side of SPEC-0011 section G (TASK-0046's touch in <c>Squid.ReadModels</c>):
/// the two questions the delivery motor asks of <c>peer_delivery</c> — which Pending rows have
/// come due, and what the DLQ holds. Read only, off the write path (R-18); the state words are the
/// enum names the store persists, spelled here because this project cannot see the enum.
/// </summary>
public sealed class SqlitePeerQueue
{
    private const string Pending = "Pending";

    private const string Failed = "Failed";

    private const string Columns =
        "activity_id AS ActivityId, recipient_actor_id AS RecipientActorId, activity_type AS Activity, "
        + "object_id AS ObjectId, payload AS Payload, policy_version_at_enqueue AS PolicyVersion, "
        + "attempts AS Attempts, next_attempt_at AS NextAttemptAt, state AS State, reason AS Reason";

    private readonly string _connectionString;

    /// <summary>Reads the same migrated file the host writes (its schema exists by then).</summary>
    public SqlitePeerQueue(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// Up to <paramref name="limit"/> Pending rows whose <c>NextAttemptAt</c> has arrived, oldest
    /// first. shortcut: the comparison is lexicographic over ISO-8601 "O" text — correct while
    /// every writer stamps UTC in that house format (all of them do); move to ticks if a row ever
    /// lands with another offset.
    /// </summary>
    public async Task<IReadOnlyList<PeerQueueRow>> DueAsync(DateTimeOffset due, int limit)
    {
        await using var connection = Open();
        var rows = await connection.QueryAsync<PeerQueueRow>(
            $"SELECT {Columns} FROM peer_delivery WHERE state = @Pending AND next_attempt_at <= @Due "
            + "ORDER BY next_attempt_at, rowid LIMIT @Limit",
            new { Pending, Due = due.ToString("O", CultureInfo.InvariantCulture), Limit = limit });
        return rows.AsList();
    }

    /// <summary>The DLQ itself (data-model §2.9's invariant: Failed goes to DLQ with a reason) — newest first.</summary>
    public async Task<IReadOnlyList<PeerQueueRow>> DlqAsync(int limit)
    {
        await using var connection = Open();
        var rows = await connection.QueryAsync<PeerQueueRow>(
            $"SELECT {Columns} FROM peer_delivery WHERE state = @Failed ORDER BY rowid DESC LIMIT @Limit",
            new { Failed, Limit = limit });
        return rows.AsList();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
