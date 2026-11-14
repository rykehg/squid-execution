using System.Globalization;
using Microsoft.Data.Sqlite;
using Squid.Grains.Peers;

namespace Squid.Service;

/// <summary>
/// <see cref="IPeerStateStore"/> over the <c>peer</c> table of <c>0008_federation.sql</c>
/// (data-model §2.9): one row per remote domain, upserted whole — the grain reads it before every
/// admission and writes it after every outcome, so last write wins. Dates travel as ISO-8601 "O"
/// text and enum columns as their names, the house shape of the federation tables.
/// </summary>
public sealed class SqlitePeerStateStore : IPeerStateStore
{
    private const string Columns =
        "domain, status, backoff_until, failure_count, last_success_at, rate_limit_per_minute";

    private readonly string _connectionString;

    /// <summary>Binds the store to the connection the host already migrates and opens.</summary>
    public SqlitePeerStateStore(string connectionString) =>
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    /// <inheritdoc />
    public async Task<PeerState?> FindAsync(string domain)
    {
        await using var connection = await OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = $"SELECT {Columns} FROM peer WHERE domain = @Domain";
        query.Parameters.AddWithValue("@Domain", domain);
        await using var reader = await query.ExecuteReaderAsync();
        return await reader.ReadAsync() ? RowOf(reader) : null;
    }

    /// <inheritdoc />
    public async Task SaveAsync(PeerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var connection = await OpenAsync();
        await using var upsert = connection.CreateCommand();
        upsert.CommandText =
            "INSERT INTO peer (domain, status, backoff_until, failure_count, last_success_at, rate_limit_per_minute) "
            + "VALUES (@Domain, @Status, @BackoffUntil, @FailureCount, @LastSuccessAt, @RateLimit) "
            + "ON CONFLICT(domain) DO UPDATE SET status = excluded.status, "
            + "backoff_until = excluded.backoff_until, failure_count = excluded.failure_count, "
            + "last_success_at = excluded.last_success_at, rate_limit_per_minute = excluded.rate_limit_per_minute";
        upsert.Parameters.AddWithValue("@Domain", state.Domain);
        upsert.Parameters.AddWithValue("@Status", state.Status.ToString());
        upsert.Parameters.AddWithValue("@BackoffUntil", (object?)Stamp(state.BackoffUntil) ?? DBNull.Value);
        upsert.Parameters.AddWithValue("@FailureCount", state.FailureCount);
        upsert.Parameters.AddWithValue("@LastSuccessAt", (object?)Stamp(state.LastSuccessAt) ?? DBNull.Value);
        upsert.Parameters.AddWithValue("@RateLimit", state.RateLimitPerMinute);
        await upsert.ExecuteNonQueryAsync();
    }

    private static string? Stamp(DateTimeOffset? at) =>
        at?.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>One row bound the way the table writes it — INTEGER columns arrive as Int64.</summary>
    private static PeerState RowOf(SqliteDataReader reader) => new(
        reader.GetString(0),
        Enum.Parse<PeerStatus>(reader.GetString(1)),
        reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
        checked((int)reader.GetInt64(3)),
        reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
        checked((int)reader.GetInt64(5)));

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
