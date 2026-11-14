using Microsoft.Data.Sqlite;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Projections;

namespace Squid.Tests.Federation;

/// <summary>
/// The 0008 script of SPEC-0011 sections F/G (TASK-0045): a brand-new database gains
/// <c>peer_delivery</c>, <c>peer</c> and <c>follow</c> with their columns, <c>schema_version</c>
/// records version 8, and applying the family a SECOND time changes nothing — the rows seeded
/// between the two applies come out byte-identical, which is the immutability rule the family
/// (0001..0007) already answers for.
/// </summary>
public sealed class FederationMigrationTests
{
    [Fact]
    public async Task The_0008_version_creates_the_federation_tables_and_reapplying_changes_nothing()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            var first = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Contains(8, first);

            // data-model §2.9, em ordem: o par que identifica a entrega, as colunas que esta task
            // soma além dele (qual atividade, de qual objeto, o payload cru) e a máquina de estado.
            Assert.Equal(
                new[]
                {
                    "activity_id", "recipient_actor_id", "activity_type", "object_id", "payload",
                    "policy_version_at_enqueue", "attempts", "next_attempt_at", "state", "reason",
                },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('peer_delivery')"));
            Assert.Equal(
                new[] { "domain", "status", "backoff_until", "failure_count", "last_success_at", "rate_limit_per_minute" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('peer')"));
            Assert.Equal(
                new[] { "handle", "direction", "actor_uri", "established", "created_at" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('follow')"));
            Assert.Equal("federation", await ProjectionDb.ScalarAsync<string>(file,
                "SELECT name FROM schema_version WHERE version = 8"));
            Assert.True(await ProjectionDb.IndexExistsAsync(file, "ix_peer_delivery_object"),
                "peer_delivery has no index on (object_id, activity_type) — the Delete fan-out reads by object");

            // Uma entrega semeada ENTRE as duas aplicações: a re-aplicação pula a versão
            // registrada (0001..0007 intactos por checksum) e a linha sai como foi escrita.
            await using (var connection = new SqliteConnection(ProjectionDb.ConnectionOf(file)))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO peer_delivery (
                        activity_id, recipient_actor_id, activity_type, object_id, payload,
                        policy_version_at_enqueue, next_attempt_at, state)
                    VALUES ('http://127.0.0.1/activities/semente', 'https://remote.example/users/bob',
                            'Create', 'http://127.0.0.1/o/semente', '{"type":"Create"}', 1,
                            '2026-11-14T00:00:00.0000000+00:00', 'Pending');
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            var second = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Empty(second);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT COUNT(*) FROM peer_delivery"));
            Assert.Equal("Pending", await ProjectionDb.ScalarAsync<string>(file,
                "SELECT state FROM peer_delivery WHERE activity_id = @ActivityId",
                new { ActivityId = "http://127.0.0.1/activities/semente" }));
            Assert.Equal(SchemaMigrations.Load().Count,
                await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM schema_version"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }
}
