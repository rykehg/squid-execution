using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Squid.Persistence;

/// <summary>One versioned script of the schema family: its number, its file name and its text.</summary>
/// <param name="Version">The number in front of the file name (<c>0001_…</c>); applied in this order, once each.</param>
/// <param name="Name">The rest of the file name — what <c>schema_version</c> records beside the number.</param>
/// <param name="Sql">The script text, byte for byte as the repository ships it.</param>
public sealed record MigrationScript(int Version, string Name, string Sql);

/// <summary>
/// Applies the versioned scripts of <c>schema/</c> in order, once each — SPEC-0009 section B and
/// AC-01 of TASK-0024. Two rules define "once":
/// <list type="bullet">
/// <item>the record of a run (version, file name, SHA-256 of the text, instant applied) is written
/// inside the SAME transaction as the script, so a script lands complete with its record or not at
/// all — there is no half-applied version to reconcile;</item>
/// <item>a version already recorded is skipped while its checksum still matches the text, and a
/// text that changed under a recorded version throws: a migration that has run is never edited,
/// it is followed by a higher number.</item>
/// </list>
/// Applying a second time therefore runs nothing and changes nothing (the idempotence the gate
/// tests with "new database + re-apply"). The bookkeeping table <c>schema_version</c> is this
/// class's own: it is created before the first script runs, so a script only ever owns its tables.
/// </summary>
public static class SchemaMigrator
{
    /// <summary>The ledger table this class owns (scripts own their tables, this one owns its own).</summary>
    public const string VersionTable = "schema_version";

    private const string CreateVersionTable = """
        CREATE TABLE IF NOT EXISTS schema_version (
            version    INTEGER PRIMARY KEY,
            name       TEXT    NOT NULL,
            checksum   TEXT    NOT NULL,
            applied_at TEXT    NOT NULL
        )
        """;

    /// <summary>SHA-256 of <paramref name="sql"/> in lowercase hex — the identity of one script text.</summary>
    public static string ChecksumOf(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
    }

    /// <summary>
    /// Applies every pending script of <paramref name="scripts"/> and returns the versions this
    /// call applied — <c>[1]</c> on a new database, empty on the re-apply that proves idempotence.
    /// </summary>
    public static async Task<IReadOnlyList<int>> ApplyAsync(
        string connectionString, IReadOnlyList<MigrationScript> scripts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(scripts);

        var ordered = scripts.OrderBy(script => script.Version).ToList();
        var duplicate = ordered.GroupBy(script => script.Version)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"migration version {duplicate.Key} appears more than once ({string.Join(", ", duplicate.Select(script => script.Name))})");
        }

        var applied = new List<int>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // The ledger is the applier's own table: it exists before the first script runs, so a
        // script never has to know it (and re-running is a no-op, like everything else here).
        await using (var ledger = connection.CreateCommand())
        {
            ledger.CommandText = CreateVersionTable;
            await ledger.ExecuteNonQueryAsync();
        }

        foreach (var script in ordered)
        {
            var checksum = ChecksumOf(script.Sql);
            var recorded = await RecordedChecksumAsync(connection, script.Version);
            if (recorded is not null)
            {
                if (!string.Equals(recorded, checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"migration {script.Version} ({script.Name}) no longer matches the checksum recorded in {VersionTable}: "
                        + "a script that has run is immutable — add a new numbered script instead");
                }

                continue; // already applied: the second run of the gate's idempotence check
            }

            await using var transaction = connection.BeginTransaction(deferred: false);

            await using (var apply = connection.CreateCommand())
            {
                apply.Transaction = transaction;
                apply.CommandText = script.Sql;
                await apply.ExecuteNonQueryAsync();
            }

            await using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText =
                    $"INSERT INTO {VersionTable} (version, name, checksum, applied_at) "
                    + "VALUES (@Version, @Name, @Checksum, @AppliedAt)";
                record.Parameters.AddWithValue("@Version", script.Version);
                record.Parameters.AddWithValue("@Name", script.Name);
                record.Parameters.AddWithValue("@Checksum", checksum);
                record.Parameters.AddWithValue("@AppliedAt",
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await record.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            applied.Add(script.Version);
        }

        return applied;
    }

    /// <summary>The checksum on record for <paramref name="version"/>, or null when it never ran.</summary>
    private static async Task<string?> RecordedChecksumAsync(SqliteConnection connection, int version)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT checksum FROM {VersionTable} WHERE version = @Version";
        command.Parameters.AddWithValue("@Version", version);
        return (string?)await command.ExecuteScalarAsync();
    }
}
