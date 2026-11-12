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

    [Fact]
    public async Task The_append_lands_number_and_row_together_and_the_delta_reads_only_what_is_new()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            var store = new SqliteThreadStore(ProjectionDb.ConnectionOf(file));

            const string author = "http://127.0.0.1:5199/users/autora";
            const string peer = "http://127.0.0.1:5199/users/par";
            var now = DateTimeOffset.UtcNow;
            var id = $"http://127.0.0.1:5199/t/{Guid.NewGuid():N}";
            await store.CreateAsync(
                new ThreadState(id, "group", author, 0, now, 1),
                [
                    new ThreadParticipant(id, author, "admin", now),
                    new ThreadParticipant(id, peer, "member", now),
                ]);

            // FR-031 one probe at a time: the two seats answer yes; a stranger and a thread that
            // does not exist answer no — the SAME no every route turns into the identical 404.
            Assert.True(await store.IsParticipantAsync(id, author));
            Assert.False(await store.IsParticipantAsync(id, "http://127.0.0.1:5199/users/estranho"));
            Assert.False(await store.IsParticipantAsync(
                $"http://127.0.0.1:5199/t/{Guid.NewGuid():N}", author));

            // Number and row in ONE step: the seq the caller gets back is already in the delta,
            // so a 200 can never name a message a ?since= read would miss (GAP-29, §9.3).
            var first = await store.AppendMessageAsync(id, $"{id}/m/um", author, "oi", now);
            Assert.NotNull(first);
            Assert.Equal(1L, first.Seq);
            Assert.Equal("oi", (await store.ListMessagesAsync(id, 0)).Single().Content);

            var second = await store.AppendMessageAsync(id, $"{id}/m/dois", peer, "tudo bem?", now);
            Assert.NotNull(second);
            Assert.Equal(2L, second.Seq);

            // Two appends racing the same thread — the route's AC-01 at store level: both land,
            // each with its own number (BEGIN IMMEDIATE queues them, the guard never doubles one).
            var racing = await Task.WhenAll(
                store.AppendMessageAsync(id, $"{id}/m/tres", author, "da corrida A", now),
                store.AppendMessageAsync(id, $"{id}/m/quatro", peer, "da corrida B", now));
            Assert.All(racing, stored => Assert.NotNull(stored));
            Assert.Equal(new[] { 3L, 4L },
                racing.Select(stored => stored!.Seq).OrderBy(value => value).ToArray());

            // No thread: null comes back, nothing is written, the delta it never had stays empty.
            var absent = $"http://127.0.0.1:5199/t/{Guid.NewGuid():N}";
            Assert.Null(await store.AppendMessageAsync(absent, $"{id}/m/nada", author, "nada", now));
            Assert.Empty(await store.ListMessagesAsync(absent, 0));

            // The exact delta of contract §9.3: seq > since only, ordered, never a repeat.
            Assert.Equal(new[] { 2L, 3L, 4L },
                (await store.ListMessagesAsync(id, 1)).Select(stored => stored.Seq).ToArray());
            Assert.Empty(await store.ListMessagesAsync(id, 4));
            var all = await store.ListMessagesAsync(id, 0);
            Assert.Equal(new[] { 1L, 2L, 3L, 4L }, all.Select(stored => stored.Seq).ToArray());
            Assert.Equal(new[] { "oi", "tudo bem?" },
                all.Take(2).Select(stored => stored.Content).ToArray());
            Assert.Contains(all, stored => stored.Content == "da corrida A");
            Assert.Contains(all, stored => stored.Content == "da corrida B");

            // On disk: four rows (one per number) and the thread's counter and version both moved.
            Assert.Equal(4, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT COUNT(*) FROM message WHERE thread_id = @Id", new { Id = id }));
            Assert.Equal(4, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT message_seq FROM thread WHERE id = @Id", new { Id = id }));
            Assert.Equal(5, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT version FROM thread WHERE id = @Id", new { Id = id }));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }
}
