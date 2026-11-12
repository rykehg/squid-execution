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

    [Fact] // TASK-0036 — a 0004 dá casa ao aceite (FR-032) e ao marcador (FR-033), sem tocar 0002/0003
    public async Task The_0004_version_creates_the_ack_and_read_tables_and_reapplying_changes_nothing()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            var first = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Contains(4, first);
            Assert.Equal(
                new[] { "thread_id", "actor_id", "acked_at", "notice" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('thread_no_e2ee_ack')"));
            Assert.Equal(
                new[] { "thread_id", "actor_id", "last_read_seq" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('thread_read')"));
            Assert.Equal("no_e2ee_ack_and_read", await ProjectionDb.ScalarAsync<string>(file,
                "SELECT name FROM schema_version WHERE version = 4"));

            // Uma linha de cada semeada ENTRE as duas aplicações: a re-aplicação pula a versão
            // registrada (0002/0003 intactos por checksum) e nenhuma das duas linhas se move.
            await using (var connection = new SqliteConnection(ProjectionDb.ConnectionOf(file)))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO thread_no_e2ee_ack (thread_id, actor_id, acked_at, notice)
                    VALUES ('http://127.0.0.1/t/semente', 'http://127.0.0.1/users/a',
                            '2026-11-12T00:00:00.0000000+00:00', 'aviso');
                    INSERT INTO thread_read (thread_id, actor_id, last_read_seq)
                    VALUES ('http://127.0.0.1/t/semente', 'http://127.0.0.1/users/a', 7);
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            var second = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Empty(second);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT COUNT(*) FROM thread_no_e2ee_ack"));
            Assert.Equal(7, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT last_read_seq FROM thread_read"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }

    [Fact] // TASK-0037 — a 0005 dá casa ao replyToId (FR-057) somando coluna nova, sem tocar 0002/0003/0004
    public async Task The_0005_version_adds_the_reply_column_and_reapplying_changes_nothing()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            var first = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Contains(5, first);
            // A coluna nova chega POR CIMA das seis de 0003, na mesma ordem — nada do que já
            // existia se move (é o ALTER TABLE da família 0005, não uma reescrita do 0003).
            Assert.Equal(
                new[] { "id", "thread_id", "author_actor_id", "content", "seq", "created_at", "reply_to_id" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('message')"));
            Assert.Equal("message_reply_to_id", await ProjectionDb.ScalarAsync<string>(file,
                "SELECT name FROM schema_version WHERE version = 5"));

            // Uma mensagem semeada ENTRE as duas aplicações: a re-aplicação pula a versão
            // registrada (0002/0003/0004 intactos por checksum) e a linha sai como foi escrita.
            await using (var connection = new SqliteConnection(ProjectionDb.ConnectionOf(file)))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO message (id, thread_id, author_actor_id, content, seq, created_at, reply_to_id)
                    VALUES ('http://127.0.0.1/m/semente', 'http://127.0.0.1/t/semente',
                            'http://127.0.0.1/users/a', 'oi', 1,
                            '2026-11-12T00:00:00.0000000+00:00', 'http://127.0.0.1/m/original');
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            var second = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Empty(second);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM message"));
            Assert.Equal("http://127.0.0.1/m/original", await ProjectionDb.ScalarAsync<string>(file,
                "SELECT reply_to_id FROM message"));
            Assert.Equal(SchemaMigrations.Load().Count,
                await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM schema_version"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }

    [Fact] // TASK-0038 — a 0006 dá casa à reação (§2.14) sem tocar 0002..0005 e sem contador algum
    public async Task The_0006_version_creates_the_reaction_table_and_reapplying_changes_nothing()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            var first = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Contains(6, first);
            // A tabela nova casa o agregado Reaction do data-model §2.14: o alvo, quem reagiu, o
            // emoji da whitelist, o Kind Like|Custom e o instante — e a CHAVE é a própria tríade,
            // que é o que faz repetir idempotente (AC-01) e remover apagar a linha (AC-03).
            Assert.Equal(
                new[] { "message_id", "actor_id", "emoji", "kind", "created_at" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('message_reaction')"));
            Assert.Equal("message_reaction", await ProjectionDb.ScalarAsync<string>(file,
                "SELECT name FROM schema_version WHERE version = 6"));

            // Nunca fonte: a mensagem NÃO ganhou coluna de contagem alguma — os sete campos dela
            // seguem os da 0005, porque counts.reactions é projeção das linhas de 0006 lida em
            // cada leitura, nunca um número guardado (AC-03, §2.14).
            Assert.Equal(
                new[] { "id", "thread_id", "author_actor_id", "content", "seq", "created_at", "reply_to_id" },
                await ProjectionDb.QueryAsync<string>(file,
                    "SELECT name FROM pragma_table_info('message')"));

            // Uma linha semeada ENTRE as duas aplicações: a re-aplicação pula a versão registrada
            // (0002..0005 intactos por checksum) e a linha sai como foi escrita.
            await using (var connection = new SqliteConnection(ProjectionDb.ConnectionOf(file)))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO message_reaction (message_id, actor_id, emoji, kind, created_at)
                    VALUES ('http://127.0.0.1/m/semente', 'http://127.0.0.1/users/a', '👍', 'like',
                            '2026-11-12T00:00:00.0000000+00:00');
                    """;
                await seed.ExecuteNonQueryAsync();

                // A MESMA tríade de novo é recusada pelo próprio esquema (AC-01: uma linha por
                // (mensagem, ator, emoji) — a chave primária é a invariante, não a store).
                await using var repeat = connection.CreateCommand();
                repeat.CommandText = """
                    INSERT INTO message_reaction (message_id, actor_id, emoji, kind, created_at)
                    VALUES ('http://127.0.0.1/m/semente', 'http://127.0.0.1/users/a', '👍', 'like',
                            '2026-11-12T00:00:01.0000000+00:00');
                    """;
                await Assert.ThrowsAsync<SqliteException>(() => repeat.ExecuteNonQueryAsync());
            }

            var second = await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            Assert.Empty(second);
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM message_reaction"));
            Assert.Equal(SchemaMigrations.Load().Count,
                await ProjectionDb.ScalarAsync<long>(file, "SELECT COUNT(*) FROM schema_version"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }
}
