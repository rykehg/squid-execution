using System.Globalization;
using Microsoft.Data.Sqlite;
using Squid.Grains.Backup;

namespace Squid.Worker.Backup;

/// <summary>
/// <see cref="IBackupStatusSource"/> over the registry of data-model section 2.10: the row count
/// and the byte total are the panel numbers of contract section 6, <c>last_success_at</c> is the
/// newest confirmed <c>verified_at</c>, and <c>verify_failures</c> counts the envelopes that were
/// registered WITHOUT a confirmed re-read — the pipeline registers a divergent hash as `Corrupt`
/// with <c>verified_at</c> NULL, so that column is exactly the failure counter section 6 alerts on.
/// The two <c>restore.*</c> fields come from the drill ledger of TASK-0056 — null only while this
/// database has never run a drill, which is the honest answer, never a hardcoded one.
/// </summary>
public sealed class SqliteBackupStatusSource(string connectionString, SqliteRestoreDrillLedger drills)
    : IBackupStatusSource
{
    /// <inheritdoc />
    public async Task<BackupStatus> ReadAsync(CancellationToken ct)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*), COALESCE(SUM(size_bytes), 0), MAX(verified_at), "
            + "SUM(CASE WHEN verified_at IS NULL THEN 1 ELSE 0 END) FROM backup_envelope";

        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt64(0);
        var bytes = reader.GetInt64(1);
        var lastSuccessAt = reader.IsDBNull(2) ? null : Parse(reader.GetString(2));
        var verifyFailures = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);

        var drill = await drills.LatestAsync(ct);
        return new BackupStatus(lastSuccessAt, count, bytes, verifyFailures, drill?.RanAt,
            drill is null ? null : $"{drill.Drill}: {drill.Result}");
    }

    /// <summary>Reads an `O`-format instant back; an unparseable value is not a verified success.</summary>
    private static DateTimeOffset? Parse(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
