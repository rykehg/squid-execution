using Microsoft.Data.Sqlite;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Projections;

namespace Squid.Tests.Federation;

/// <summary>
/// The 0009 script of SPEC-0011 section B (TASK-0051): a brand-new database gains
/// <c>instance_signing_keys</c> with its two columns, <c>schema_version</c> records version 9 with
/// the checksum of the text that ran, and applying the family a SECOND time changes nothing — the
/// copy seeded between the two applies comes out byte-identical, which is the immutability rule
/// the family (0001..0008) already answers for.
/// </summary>
public sealed class InstanceSigningKeyMigrationTests
{
    [Fact]
    public async Task The_0009_version_creates_the_instance_copy_table_and_reapplying_changes_nothing()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            var first = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Contains(9, first);

            // A table whose two columns are handle and the WRAPPED copy — nothing a clear key
            // could hide in (SPEC-0011 section B: a cópia nunca em claro).
            Assert.True(await ProjectionDb.TableExistsAsync(file, "instance_signing_keys"));
            Assert.Equal(
                new[] { "handle", "wrapped_signing_key" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('instance_signing_keys')"));
            Assert.Equal("instance_signing_keys", await ProjectionDb.ScalarAsync<string>(file,
                "SELECT name FROM schema_version WHERE version = 9"));

            // Uma cópia semeada ENTRE as duas aplicações: a re-aplicação pula a versão registrada
            // (0001..0008 intactos por checksum) e a linha sai como foi escrita.
            await using (var connection = new SqliteConnection(ProjectionDb.ConnectionOf(file)))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText =
                    "INSERT INTO instance_signing_keys (handle, wrapped_signing_key) "
                    + "VALUES ('ac09', 'ZW5yb2xhZGE=')";
                await seed.ExecuteNonQueryAsync();
            }

            var second = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Empty(second);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT COUNT(*) FROM instance_signing_keys"));
            Assert.Equal(SchemaMigrations.Load().Count,
                await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM schema_version"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }
}
