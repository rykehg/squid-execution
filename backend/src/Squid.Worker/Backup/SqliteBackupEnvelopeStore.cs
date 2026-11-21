using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Squid.Worker.Backup;

/// <summary>
/// The registry of data-model section 2.10 over the <c>backup_envelope</c> table that
/// <c>0010_backup.sql</c> creates: the pipeline registers AFTER the re-read (contract
/// backup-recovery section 3), so <c>verified_at</c> is set only for a confirmed hash and stays
/// NULL for a <c>Corrupt</c> envelope — which restore never uses. The raw ADO pattern of the
/// <c>Sqlite*Store</c> composition-root stores, over the same migrated file the host migrates.
/// </summary>
public sealed class SqliteBackupEnvelopeStore
{
    private readonly string _connectionString;

    /// <summary>Binds the store to the connection the host already migrates and opens.</summary>
    public SqliteBackupEnvelopeStore(string connectionString) =>
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    /// <summary>Writes — or rewrites under the same id — the one row of <paramref name="envelope"/>.</summary>
    public async Task RegisterAsync(BackupEnvelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT OR REPLACE INTO backup_envelope "
            + "(id, key_epoch, seq_from, seq_to, content_hash, size_bytes, target_id, created_at, verified_at) "
            + "VALUES (@Id, @KeyEpoch, @SeqFrom, @SeqTo, @ContentHash, @SizeBytes, @TargetId, @CreatedAt, @VerifiedAt)";
        insert.Parameters.AddWithValue("@Id", envelope.Id);
        insert.Parameters.AddWithValue("@KeyEpoch", envelope.KeyEpoch);
        insert.Parameters.AddWithValue("@SeqFrom", envelope.SeqFrom);
        insert.Parameters.AddWithValue("@SeqTo", envelope.SeqTo);
        insert.Parameters.AddWithValue("@ContentHash", envelope.ContentHash);
        insert.Parameters.AddWithValue("@SizeBytes", envelope.SizeBytes);
        insert.Parameters.AddWithValue("@TargetId", envelope.TargetId);
        insert.Parameters.AddWithValue("@CreatedAt",
            envelope.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("@VerifiedAt",
            (object?)envelope.VerifiedAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(ct);
    }
}
