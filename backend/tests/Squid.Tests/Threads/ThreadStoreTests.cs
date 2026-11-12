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

    [Fact] // AC-04 (GAP-32) — explicit joined_at on every row: the clock is the argument, nothing sleeps
    public async Task The_succession_ranks_by_joined_at_and_a_group_with_seats_never_loses_its_admin()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());
            var store = new SqliteThreadStore(ProjectionDb.ConnectionOf(file));

            const string creator = "http://127.0.0.1:5199/users/criador";
            const string ana = "http://127.0.0.1:5199/users/ana";
            const string bia = "http://127.0.0.1:5199/users/bia";
            const string dani = "http://127.0.0.1:5199/users/dani";
            const string fora = "http://127.0.0.1:5199/users/fora";
            var t0 = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
            var t1 = t0.AddMinutes(1);
            var t2 = t0.AddMinutes(2);

            // Road 1 (an admin remains): Bia's joined_at is the smallest even though her ROW comes
            // after Ana's — the ranking is time, not insertion order, which only breaks ties.
            var withAdmins = $"http://127.0.0.1:5199/t/{Guid.NewGuid():N}";
            await store.CreateAsync(
                new ThreadState(withAdmins, "group", creator, 0, t0, 1),
                [
                    new ThreadParticipant(withAdmins, creator, "admin", t0),
                    new ThreadParticipant(withAdmins, ana, "admin", t2),
                    new ThreadParticipant(withAdmins, bia, "admin", t1),
                ]);
            await store.AppendMessageAsync(withAdmins, $"{withAdmins}/m/um", creator, "oi", t0);

            Assert.Equal(ParticipantRemoval.Removed,
                await store.RemoveParticipantAsync(withAdmins, creator));
            var snapshot = await store.FindAsync(withAdmins);
            Assert.NotNull(snapshot);
            Assert.Equal(bia, snapshot!.Thread.CreatedBy);
            Assert.Equal(2, snapshot.Participants.Count(participant => participant.Role == "admin"));

            // The history is never part of the removal: whoever stayed still reads it.
            Assert.Equal("oi", (await store.ListMessagesAsync(withAdmins, 0)).Single().Content);

            // A seat that was never in the thread answers NoSuchParticipant and writes nothing.
            Assert.Equal(ParticipantRemoval.NoSuchParticipant,
                await store.RemoveParticipantAsync(withAdmins, fora));
            Assert.Equal(2, (await store.FindAsync(withAdmins))!.Participants.Count);

            // Road 2 (no admin remains): the OLDEST member is promoted to admin AND creator.
            var noAdmin = $"http://127.0.0.1:5199/t/{Guid.NewGuid():N}";
            await store.CreateAsync(
                new ThreadState(noAdmin, "group", creator, 0, t0, 1),
                [
                    new ThreadParticipant(noAdmin, creator, "admin", t0),
                    new ThreadParticipant(noAdmin, ana, "member", t2),
                    new ThreadParticipant(noAdmin, dani, "member", t1),
                ]);

            Assert.Equal(ParticipantRemoval.Removed,
                await store.RemoveParticipantAsync(noAdmin, creator));
            var promoted = await store.FindAsync(noAdmin);
            Assert.NotNull(promoted);
            Assert.Equal(dani, promoted!.Thread.CreatedBy);
            Assert.Equal("admin", promoted.Participants.Single(participant => participant.ActorId == dani).Role);
            Assert.Equal("member", promoted.Participants.Single(participant => participant.ActorId == ana).Role);
            Assert.Contains(promoted.Participants, participant => participant.Role == "admin");

            // The last seat never leaves: the refusal leaves the thread exactly as it was.
            var alone = $"http://127.0.0.1:5199/t/{Guid.NewGuid():N}";
            await store.CreateAsync(
                new ThreadState(alone, "group", creator, 0, t0, 1),
                [new ThreadParticipant(alone, creator, "admin", t0)]);
            Assert.Equal(ParticipantRemoval.LastParticipant,
                await store.RemoveParticipantAsync(alone, creator));
            var kept = await store.FindAsync(alone);
            Assert.NotNull(kept);
            Assert.Equal(creator, kept!.Thread.CreatedBy);
            Assert.Single(kept.Participants);

            // No thread at all is the same nothing the 404 is built from, and the admin routes'
            // own write only lands on a seat that exists.
            Assert.Equal(ParticipantRemoval.NoSuchParticipant,
                await store.RemoveParticipantAsync($"http://127.0.0.1:5199/t/{Guid.NewGuid():N}", creator));
            Assert.True(await store.SetRoleAsync(noAdmin, ana, "admin"));
            Assert.False(await store.SetRoleAsync(noAdmin, fora, "admin"));
            Assert.Equal("admin", (await store.FindAsync(noAdmin))!
                .Participants.Single(participant => participant.ActorId == ana).Role);
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }
}
