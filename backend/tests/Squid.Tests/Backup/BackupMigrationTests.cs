using Microsoft.Data.Sqlite;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Projections;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-01 of TASK-0054: migration <c>0010_*.sql</c> creates <c>backup_envelope</c> and
/// <c>key_epoch</c> with the columns of data-model section 2.10 through the EXISTING mechanism
/// (the embedded family + <see cref="SchemaMigrator"/>), key_epoch is NOT NULL — an envelope row
/// without an epoch cannot be written — and applying the family a second time changes nothing: a
/// row seeded between the two applies comes out intact.
/// </summary>
public sealed class BackupMigrationTests
{
    [Fact]
    public async Task Ac01_The_0010_version_creates_both_2_10_tables_and_reapplying_changes_nothing()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            var first = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Contains(10, first);

            Assert.True(await ProjectionDb.TableExistsAsync(file, "backup_envelope"));
            Assert.True(await ProjectionDb.TableExistsAsync(file, "key_epoch"));

            // The exact columns of data-model section 2.10, in table order.
            Assert.Equal(
                new[] { "id", "key_epoch", "seq_from", "seq_to", "content_hash", "size_bytes", "target_id", "created_at", "verified_at" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('backup_envelope')"));
            Assert.Equal(
                new[] { "epoch", "created_at", "public_fingerprint", "retired_at" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('key_epoch')"));
            Assert.Equal("backup", await ProjectionDb.ScalarAsync<string>(file,
                "SELECT name FROM schema_version WHERE version = 10"));

            // "KeyEpoch obrigatoria em todo envelope": refused by the column itself, not by code.
            var refusal = await Assert.ThrowsAsync<SqliteException>(
                () => InsertEnvelopeAsync(file, keyEpoch: null));
            Assert.Contains("NOT NULL", refusal.Message, StringComparison.OrdinalIgnoreCase);

            // Seeded BETWEEN the two applies: re-apply skips the recorded version and the row survives.
            await InsertEnvelopeAsync(file, keyEpoch: 3);
            var second = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Empty(second);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM backup_envelope"));
            Assert.Equal(SchemaMigrations.Load().Count,
                await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM schema_version"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }

    private static async Task InsertEnvelopeAsync(string file, int? keyEpoch)
    {
        await using var connection = new SqliteConnection(ProjectionDb.ConnectionOf(file));
        await connection.OpenAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO backup_envelope "
            + "(id, key_epoch, seq_from, seq_to, content_hash, size_bytes, target_id, created_at, verified_at) "
            + "VALUES ('bk-ac01', @KeyEpoch, 0, 1, 'sha256:aa', 10, 'local-disk', '2026-11-21T00:00:00.0000000+00:00', NULL)";
        insert.Parameters.AddWithValue("@KeyEpoch", (object?)keyEpoch ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync();
    }
}
