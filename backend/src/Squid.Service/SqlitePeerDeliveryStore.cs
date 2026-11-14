using System.Globalization;
using Microsoft.Data.Sqlite;
using Squid.Api.Federation;

namespace Squid.Service;

/// <summary>
/// <see cref="IPeerDeliveryStore"/> over the <c>peer_delivery</c> table of
/// <c>0008_federation.sql</c> (data-model §2.9, SPEC-0011 section F): the queue lives in a
/// migrated table, not in grain documents, because its rows are read by pair, by object and by
/// state — queries a document store cannot answer. Dates travel as ISO-8601 text ("O"), the
/// house shape of the thread tables; enum columns travel as their names, parsed back here so no
/// caller ever compares strings.
/// </summary>
public sealed class SqlitePeerDeliveryStore : IPeerDeliveryStore
{
    private const string Columns =
        "activity_id, recipient_actor_id, activity_type, object_id, payload, "
        + "policy_version_at_enqueue, attempts, next_attempt_at, state, reason";

    private readonly string _connectionString;

    /// <summary>Binds the store to the connection the host already migrates and opens.</summary>
    public SqlitePeerDeliveryStore(string connectionString) =>
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    /// <inheritdoc />
    public async Task AddAsync(PeerDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        await using var connection = await OpenAsync();
        await using var insert = connection.CreateCommand();
        // INSERT OR IGNORE: the pair is the primary key, so a repeated fan-out of the same
        // activity is one row — the idempotency every re-run of the publish path owes.
        insert.CommandText =
            "INSERT OR IGNORE INTO peer_delivery ("
            + "activity_id, recipient_actor_id, activity_type, object_id, payload, "
            + "policy_version_at_enqueue, attempts, next_attempt_at, state, reason) "
            + "VALUES (@ActivityId, @Recipient, @Activity, @ObjectId, @Payload, "
            + "@PolicyVersion, @Attempts, @NextAttemptAt, @State, @Reason)";
        Bind(insert, delivery);
        await insert.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task<PeerDelivery?> FindAsync(string activityId, string recipientActorId)
    {
        await using var connection = await OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText =
            $"SELECT {Columns} FROM peer_delivery "
            + "WHERE activity_id = @ActivityId AND recipient_actor_id = @Recipient";
        query.Parameters.AddWithValue("@ActivityId", activityId);
        query.Parameters.AddWithValue("@Recipient", recipientActorId);
        await using var reader = await query.ExecuteReaderAsync();
        return await reader.ReadAsync() ? RowOf(reader) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PeerDelivery>> ForObjectAsync(string objectId, PeerActivity activity)
    {
        await using var connection = await OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText =
            $"SELECT {Columns} FROM peer_delivery "
            + "WHERE object_id = @ObjectId AND activity_type = @Activity ORDER BY rowid";
        query.Parameters.AddWithValue("@ObjectId", objectId);
        query.Parameters.AddWithValue("@Activity", activity.ToString());
        return await ReadAllAsync(query);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PeerDelivery>> ForRecipientAsync(string recipientActorId)
    {
        await using var connection = await OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText =
            $"SELECT {Columns} FROM peer_delivery WHERE recipient_actor_id = @Recipient ORDER BY rowid";
        query.Parameters.AddWithValue("@Recipient", recipientActorId);
        return await ReadAllAsync(query);
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(PeerDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        await using var connection = await OpenAsync();
        await using var update = connection.CreateCommand();
        update.CommandText =
            "UPDATE peer_delivery SET state = @State, reason = @Reason, attempts = @Attempts, "
            + "next_attempt_at = @NextAttemptAt "
            + "WHERE activity_id = @ActivityId AND recipient_actor_id = @Recipient";
        update.Parameters.AddWithValue("@State", delivery.State.ToString());
        update.Parameters.AddWithValue("@Reason", (object?)delivery.Reason ?? DBNull.Value);
        update.Parameters.AddWithValue("@Attempts", delivery.Attempts);
        update.Parameters.AddWithValue("@NextAttemptAt", delivery.NextAttemptAt.ToString("O", CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("@ActivityId", delivery.ActivityId);
        update.Parameters.AddWithValue("@Recipient", delivery.RecipientActorId);
        return await update.ExecuteNonQueryAsync() == 1;
    }

    private static void Bind(SqliteCommand insert, PeerDelivery delivery)
    {
        insert.Parameters.AddWithValue("@ActivityId", delivery.ActivityId);
        insert.Parameters.AddWithValue("@Recipient", delivery.RecipientActorId);
        insert.Parameters.AddWithValue("@Activity", delivery.Activity.ToString());
        insert.Parameters.AddWithValue("@ObjectId", delivery.ObjectId);
        insert.Parameters.AddWithValue("@Payload", delivery.Payload);
        insert.Parameters.AddWithValue("@PolicyVersion", delivery.PolicyVersionAtEnqueue);
        insert.Parameters.AddWithValue("@Attempts", delivery.Attempts);
        insert.Parameters.AddWithValue("@NextAttemptAt",
            delivery.NextAttemptAt.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("@State", delivery.State.ToString());
        insert.Parameters.AddWithValue("@Reason", (object?)delivery.Reason ?? DBNull.Value);
    }

    /// <summary>Every row of an open reader, in the order the query returned them.</summary>
    private static async Task<IReadOnlyList<PeerDelivery>> ReadAllAsync(SqliteCommand query)
    {
        var rows = new List<PeerDelivery>();
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(RowOf(reader));
        }

        return rows;
    }

    /// <summary>One row bound the way the table writes it — INTEGER columns arrive as Int64 (Dapper's exact-binding rule, kept in raw ADO too).</summary>
    private static PeerDelivery RowOf(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        Enum.Parse<PeerActivity>(reader.GetString(2)),
        reader.GetString(3),
        reader.GetString(4),
        checked((int)reader.GetInt64(5)),
        checked((int)reader.GetInt64(6)),
        DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
        Enum.Parse<PeerDeliveryState>(reader.GetString(8)),
        reader.IsDBNull(9) ? null : reader.GetString(9));

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
