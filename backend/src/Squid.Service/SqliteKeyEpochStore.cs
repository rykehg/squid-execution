using System.Globalization;
using Microsoft.Data.Sqlite;
using Squid.Api.Crypto;

namespace Squid.Service;

/// <summary>
/// <see cref="IKeyEpochStore"/> over the <c>key_epoch</c> table of <c>0010_backup.sql</c>
/// (data-model section 2.10): the ledger the backup registry reads its stamp from and key
/// rotation writes — never a row anywhere else (SPEC-0012 section E). The raw ADO pattern of the
/// other <c>Sqlite*Store</c> composition-root stores, over the same migrated file the host opens;
/// <see cref="AdvanceAsync"/> is one transaction, so a rotation cannot retire an epoch without
/// opening the next.
/// </summary>
public sealed class SqliteKeyEpochStore : IKeyEpochStore
{
    private readonly string _connectionString;

    /// <summary>Binds the store to the connection the host already migrates and opens.</summary>
    public SqliteKeyEpochStore(string connectionString) =>
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    /// <inheritdoc />
    public async Task<int> CurrentEpochAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(epoch), 1) FROM key_epoch";
        var value = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task AdvanceAsync(
        int previousEpoch,
        string previousFingerprint,
        int newEpoch,
        string newFingerprint,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFingerprint);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var stamp = at.ToString("O", CultureInfo.InvariantCulture);

        // Materialise the previous row when the ledger has never been written (first rotation, or
        // a ledger a disaster wiped): AC-01's RetiredAt must exist even there. A row that already
        // exists keeps its own CreatedAt and fingerprint — INSERT OR IGNORE never rewrites one.
        await using (var previous = connection.CreateCommand())
        {
            previous.Transaction = (SqliteTransaction)transaction;
            previous.CommandText =
                "INSERT OR IGNORE INTO key_epoch (epoch, created_at, public_fingerprint, retired_at) "
                + "VALUES (@Epoch, @At, @Fingerprint, @At)";
            previous.Parameters.AddWithValue("@Epoch", previousEpoch);
            previous.Parameters.AddWithValue("@At", stamp);
            previous.Parameters.AddWithValue("@Fingerprint", previousFingerprint);
            await previous.ExecuteNonQueryAsync(ct);
        }

        await using (var retire = connection.CreateCommand())
        {
            retire.Transaction = (SqliteTransaction)transaction;
            retire.CommandText =
                "UPDATE key_epoch SET retired_at = @At WHERE epoch = @Epoch AND retired_at IS NULL";
            retire.Parameters.AddWithValue("@At", stamp);
            retire.Parameters.AddWithValue("@Epoch", previousEpoch);
            await retire.ExecuteNonQueryAsync(ct);
        }

        await using (var next = connection.CreateCommand())
        {
            next.Transaction = (SqliteTransaction)transaction;
            next.CommandText =
                "INSERT INTO key_epoch (epoch, created_at, public_fingerprint, retired_at) "
                + "VALUES (@Epoch, @At, @Fingerprint, NULL)";
            next.Parameters.AddWithValue("@Epoch", newEpoch);
            next.Parameters.AddWithValue("@At", stamp);
            next.Parameters.AddWithValue("@Fingerprint", newFingerprint);
            await next.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
