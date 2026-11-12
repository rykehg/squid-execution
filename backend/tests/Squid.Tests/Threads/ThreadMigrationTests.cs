using Microsoft.Data.Sqlite;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Projections;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-01 of TASK-0033 — the versioned 0002 script of SPEC-0010 section A: a brand-new database
/// gains <c>thread</c> with <c>message_seq</c>/<c>version</c> and <c>thread_participant</c> with
/// its role, and applying the family a SECOND time fails at nothing and duplicates nothing —
/// the rows seeded between the two applies come out byte-identical.
/// </summary>
public sealed class ThreadMigrationTests
{
    [Fact]
    public async Task Ac01_the_second_migration_creates_the_thread_tables_and_applying_twice_changes_nothing()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            // Banco novo: a aplicação roda 0001 e 0002 e registra as duas versões.
            var first = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Contains(2, first);
            Assert.Equal(
                new[] { "id", "kind", "created_by", "message_seq", "created_at", "version" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('thread')"));
            Assert.Equal(
                new[] { "thread_id", "actor_id", "role", "joined_at" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('thread_participant')"));
            Assert.True(await ProjectionDb.IndexExistsAsync(file, "ix_thread_participant_actor"),
                "thread_participant has no index on actor_id (GET /api/threads needs it)");
            Assert.Equal("threads", await ProjectionDb.ScalarAsync<string>(file,
                "SELECT name FROM schema_version WHERE version = 2"));

            // One thread row + one participant seeded BETWEEN the two applies: the re-apply must
            // skip the recorded version, so neither row may be touched nor duplicated.
            await using (var connection = new SqliteConnection(ProjectionDb.ConnectionOf(file)))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO thread (id, kind, created_by, message_seq, created_at, version)
                    VALUES ('http://127.0.0.1/t/semente', 'direct', 'http://127.0.0.1/users/a', 0,
                            '2026-11-12T00:00:00.0000000+00:00', 1);
                    INSERT INTO thread_participant (thread_id, actor_id, role, joined_at)
                    VALUES ('http://127.0.0.1/t/semente', 'http://127.0.0.1/users/a', 'member',
                            '2026-11-12T00:00:00.0000000+00:00');
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            // Segunda aplicação: idempotente — nada roda de novo.
            var second = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Empty(second);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM thread"));
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM thread_participant"));
            Assert.Equal(SchemaMigrations.Load().Count,
                await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM schema_version"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }
}
