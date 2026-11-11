using Squid.Persistence;
using Squid.Service;

namespace Squid.Tests.Projections;

/// <summary>
/// AC-01 of TASK-0024 â€” the versioned migration (SPEC-0009 section B, human gate 2026-11-09):
/// a brand-new database gains every table of the family with its seq index, <c>schema_version</c>
/// records the version with the checksum of the text that ran, and applying it a SECOND time
/// changes nothing. The two other facts pin the applier's refusals: a text that changed under a
/// recorded version, and one version listed twice.
/// </summary>
public sealed class SchemaMigrationTests
{
    /// <summary>The tables 0001 creates â€” every one of them, or the migration did not run.</summary>
    private static readonly string[] Tables =
        ["schema_version", "activity_counter", "timeline_item", "home_timeline", "quota_ledger"];

    [Fact]
    public async Task Ac01_a_new_database_gets_every_table_and_the_second_apply_changes_nothing()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            // Banco novo: a primeira aplicaÃ§Ã£o roda o script e registra a versÃ£o.
            var first = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Equal(new[] { 1 }, first);

            foreach (var table in Tables)
            {
                Assert.True(await ProjectionDb.TableExistsAsync(file, table),
                    $"'{table}' was not created by 0001_init_projections.sql");
            }

            // AC-01: as duas projeÃ§Ãµes com Ã­ndice em seq (Ã© ele que o delta ?since= usa).
            Assert.True(await ProjectionDb.IndexExistsAsync(file, "ix_timeline_item_seq"),
                "timeline_item has no index on seq");
            Assert.True(await ProjectionDb.IndexExistsAsync(file, "ix_home_timeline_seq"),
                "home_timeline has no index on seq");

            // schema_version registra a versÃ£o, com o checksum do texto que rodou.
            var row = Assert.Single(await ProjectionDb.QueryAsync<VersionRow>(file,
                "SELECT version AS Version, name AS Name, checksum AS Checksum FROM schema_version"));
            Assert.Equal(1, row.Version);
            Assert.Equal("init_projections", row.Name);
            var script = Assert.Single(SchemaMigrations.Load());
            Assert.Equal(SchemaMigrator.ChecksumOf(script.Sql), row.Checksum);

            // Os counters que nascem com a migration (GAP-23 e a seÃ§Ã£o F): zero e 10 GiB.
            Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT last_seq FROM activity_counter WHERE id = 1"));
            Assert.Equal(10_737_418_240L, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT limit_bytes FROM quota_ledger WHERE id = 1"));

            // ReaplicaÃ§Ã£o: idempotente â€” nada roda de novo e a tabela continua com uma linha.
            var second = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Empty(second);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM schema_version"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }

    [Fact]
    public async Task A_text_that_changed_under_a_recorded_version_is_refused_instead_of_rerun()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            var connection = ProjectionDb.ConnectionOf(file);
            var scripts = new[] { new MigrationScript(1, "demo", "CREATE TABLE demo (id INTEGER PRIMARY KEY);") };

            Assert.Equal(new[] { 1 }, await SchemaMigrator.ApplyAsync(connection, scripts));
            Assert.Empty(await SchemaMigrator.ApplyAsync(connection, scripts)); // the same text: already there

            var edited = new[] { new MigrationScript(1, "demo", "CREATE TABLE demo (id INTEGER PRIMARY KEY, extra TEXT);") };
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SchemaMigrator.ApplyAsync(connection, edited));
            Assert.Contains("checksum", refusal.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }

    [Fact]
    public async Task One_version_listed_twice_is_a_wiring_error_not_a_second_table()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            var scripts = new[]
            {
                new MigrationScript(1, "one", "CREATE TABLE one (id INTEGER PRIMARY KEY);"),
                new MigrationScript(1, "other", "CREATE TABLE other (id INTEGER PRIMARY KEY);"),
            };

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), scripts));
            Assert.Contains("1", refusal.Message, StringComparison.Ordinal);
            Assert.Empty(await ProjectionDb.QueryAsync<string>(file,
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('one', 'other')"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }

    /// <summary>One <c>schema_version</c> row, bound the way Dapper binds a constructor.</summary>
    private sealed record VersionRow(long Version, string Name, string Checksum);
}
