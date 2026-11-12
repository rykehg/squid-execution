using Squid.Api.Threads;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Projections;
using ThreadState = Squid.Api.Threads.ThreadState; // System.Threading.ThreadState rides the implicit usings

namespace Squid.Tests.Threads;

/// <summary>
/// The store half of AC-01/GAP-29 (TASK-0033): <c>MessageSeq</c> is handed out by the single
/// version-guarded UPDATE of the thread row — a stale expectation moves nothing, and of two
/// racing senders exactly one ever receives the number.
/// </summary>
public sealed class ThreadStoreTests
{
    [Fact]
    public async Task The_message_seq_is_taken_only_under_the_matching_version_and_never_twice()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            var store = new SqliteThreadStore(ProjectionDb.ConnectionOf(file));

            const string creator = "http://127.0.0.1:5199/users/criador";
            var id = $"http://127.0.0.1:5199/t/{Guid.NewGuid():N}";
            await store.CreateAsync(
                new ThreadState(id, "direct", creator, 0, DateTimeOffset.UtcNow, 1),
                [new ThreadParticipant(id, creator, "member", DateTimeOffset.UtcNow)]);

            // Two senders that both read version 1: SQLite serializes the writes, the guard lets
            // exactly one land — the loser takes no number (GAP-29).
            var racing = await Task.WhenAll(
                store.NextMessageSeqAsync(id, 1),
                store.NextMessageSeqAsync(id, 1));
            Assert.Single(racing.Where(seq => seq is not null));
            Assert.Equal(1L, racing.Single(seq => seq is not null));

            // The winner's successor: version 2 hands out 2, and the stale version 1 moves nothing.
            Assert.Equal(2L, await store.NextMessageSeqAsync(id, 2));
            Assert.Null(await store.NextMessageSeqAsync(id, 2));
            Assert.Equal(2, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT message_seq FROM thread WHERE id = @Id", new { Id = id }));
            Assert.Equal(3, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT version FROM thread WHERE id = @Id", new { Id = id }));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }
}
