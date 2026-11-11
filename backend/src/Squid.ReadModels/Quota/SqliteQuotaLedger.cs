using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Squid.ReadModels.Quota;

/// <summary>
/// Dapper access to <c>quota_ledger</c> (SPEC-0009 section F, GAP-20): the table the versioned
/// migration already creates — this class never touches the script. The gate is a single
/// conditional UPDATE, so concurrent uploads cannot together pass a limit they would break, and a
/// refused reservation writes NOTHING: not a byte of <c>used_bytes</c>, not even <c>limit_bytes</c>
/// — which is what lets the route answer 507 with the row byte-identical to before (AC-01 hashes
/// it before and after). <c>updated_at</c> is the RECONCILIATION stamp (data-model §2.13), so the
/// reservation does not move it; only <see cref="SetUsedAsync"/> does.
/// </summary>
public sealed class SqliteQuotaLedger : IQuotaLedger
{
    private const string Count = "SELECT COUNT(*) FROM quota_ledger WHERE id = 1";

    private const string Insert = """
        INSERT INTO quota_ledger (id, used_bytes, limit_bytes, updated_at)
        VALUES (1, @Bytes, @LimitBytes, @Now)
        ON CONFLICT (id) DO NOTHING
        """;

    private const string Reserve = """
        UPDATE quota_ledger
        SET used_bytes = used_bytes + @Bytes, limit_bytes = @LimitBytes
        WHERE id = 1 AND used_bytes + @Bytes <= @LimitBytes
        """;

    private const string SetUsed = "UPDATE quota_ledger SET used_bytes = @UsedBytes, updated_at = @Now WHERE id = 1";

    private readonly string _connectionString;

    /// <summary>Reads the same migrated file the host writes (its schema exists by then).</summary>
    public SqliteQuotaLedger(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<bool> TryReserveAsync(long bytes, long limitBytes, DateTimeOffset now)
    {
        await using var connection = Open();

        if (await connection.ExecuteScalarAsync<long>(Count) == 0)
        {
            // First use (AC-03): the row is born here with this reservation already counted and the
            // configured limit. A racing first use can lose one increment at most — the sweep fixes it.
            return bytes <= limitBytes
                && await connection.ExecuteAsync(
                    Insert, new { Bytes = bytes, LimitBytes = limitBytes, Now = Stamp(now) }) > 0;
        }

        return await connection.ExecuteAsync(
            Reserve, new { Bytes = bytes, LimitBytes = limitBytes }) > 0;
    }

    /// <inheritdoc />
    public async Task SetUsedAsync(long usedBytes, DateTimeOffset now)
    {
        await using var connection = Open();
        await connection.ExecuteAsync(SetUsed, new { UsedBytes = usedBytes, Now = Stamp(now) });
    }

    /// <summary>UTC round-trip "O" — the same shape the migration's strftime writes, so one column reads one way.</summary>
    private static string Stamp(DateTimeOffset now) =>
        now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
