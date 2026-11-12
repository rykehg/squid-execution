using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-01..AC-05 of TASK-0035 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// adding a seat is Group-only, admin-only and capped at 50, with a byte-identical 404 for an
/// outsider (AC-01); a removal ends access at once while the history stays with whoever remains,
/// third parties never touch the creator, and the last seat answers <c>409 last_participant</c>
/// (AC-02, plus GAP-40's Direct 2→1); any admin elects or demotes admins and the creator's seat
/// never changes (AC-03); the creator's own leave hands the seat to the oldest admin or promotes
/// the oldest member (AC-04, GAP-32); and a seat added later reads the whole history (AC-05,
/// GAP-40).
/// </summary>
[Collection("ServiceHost")]
public sealed class ThreadParticipantRoutesTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ThreadParticipantRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Adding_a_seat_is_admin_only_group_only_and_refuses_past_fifty()
    {
        var (admin, adminHandle) = await PostRoutes.SignedInAsync(_host, "addadm");
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "addmem");
        var (outsider, outsiderHandle) = await PostRoutes.SignedInAsync(_host, "addout");
        var origin = PostRoutes.Origin(_host);
        var outsiderId = $"{origin}/users/{outsiderHandle}";
        var threadId = await CreateAsync(admin, "group", memberHandle);

        // A common participant is refused by the seat, never by the thread (403 policy_denied).
        var byMember = await AddAsync(member, threadId, new { actorId = outsiderHandle });
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Contains("policy_denied", await byMember.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // An outsider cannot tell the thread exists: their 404 IS the absent thread's 404, byte
        // for byte, without a traceId that would differ per request (FR-031, SC-005).
        var foreign = await PostRoutes.SendAsync(outsider, HttpMethod.Post,
            AddPath(threadId), new { actorId = memberHandle }, outsider.Csrf);
        var absent = await PostRoutes.SendAsync(outsider, HttpMethod.Post,
            AddPath(AbsentThreadId(origin)), new { actorId = memberHandle }, outsider.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        var foreignBody = await foreign.Content.ReadAsByteArrayAsync();
        Assert.Equal(foreignBody, await absent.Content.ReadAsByteArrayAsync());
        var refusal = Encoding.UTF8.GetString(foreignBody);
        Assert.Contains("not_found", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(memberHandle, refusal, StringComparison.Ordinal);

        // Direct never accepts additions — and the kind refuses BEFORE the admin ladder, because
        // nobody in a Direct holds the admin seat (AC-01's "Direct → 400" would be unreachable).
        var directId = await CreateAsync(admin, "direct", memberHandle);
        var onDirect = await AddAsync(admin, directId, new { actorId = outsiderHandle });
        Assert.Equal(HttpStatusCode.BadRequest, onDirect.StatusCode);
        Assert.Contains("validation_failed", await onDirect.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The ceiling: a Group already carrying 50 seats does not take a fifty-first (400).
        var fullId = await CreateAsync(admin, "group",
            Enumerable.Range(1, 49).Select(index => $"addcap{index}").ToArray());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await AddAsync(admin, fullId, new { actorId = outsiderHandle })).StatusCode);

        // A body that names no local identity is a payload this route cannot take (400).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await AddAsync(admin, threadId, new { actorId = "@@@" })).StatusCode);

        // The admin's own add: 201, and the seat shows up everywhere — in the answer (both
        // spellings of the identity reach the same canonical row), in the admin's list with all
        // three names of data-model §2.7 in join order, and in the newcomer's own list.
        var added = await AddAsync(admin, threadId, new { actorId = outsiderId });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        using (var json = JsonDocument.Parse(await added.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                new[] { $"{origin}/users/{adminHandle}", $"{origin}/users/{memberHandle}", outsiderId },
                json.RootElement.GetProperty("participants").EnumerateArray()
                    .Select(value => value.GetString()));
        }

        Assert.Equal(
            new[] { $"{origin}/users/{adminHandle}", $"{origin}/users/{memberHandle}", outsiderId },
            await ParticipantsAsync(admin, threadId));
        Assert.Contains(await ListAsync(outsider),
            item => item.GetProperty("id").GetString() == threadId);

        // Re-adding the same seat is a no-op that answers 200 and writes nothing (still three).
        var again = await AddAsync(admin, threadId, new { actorId = outsiderHandle });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(3, (await ParticipantsAsync(admin, threadId)).Count);
    }

    [Fact] // AC-02
    public async Task Removal_ends_access_at_once_keeps_the_history_and_never_lets_the_last_one_go()
    {
        var (creator, creatorHandle) = await PostRoutes.SignedInAsync(_host, "rmcria");
        var (one, oneHandle) = await PostRoutes.SignedInAsync(_host, "rmmem1");
        var (two, twoHandle) = await PostRoutes.SignedInAsync(_host, "rmmem2");
        var origin = PostRoutes.Origin(_host);
        var creatorId = $"{origin}/users/{creatorHandle}";
        var oneId = $"{origin}/users/{oneHandle}";
        var twoId = $"{origin}/users/{twoHandle}";
        var threadId = await CreateAsync(creator, "group", oneHandle, twoHandle);

        // Two messages before anyone leaves: the history the AC keeps for whoever stays.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(creator, threadId, "primeira")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(one, threadId, "segunda")).StatusCode);

        // A common participant never removes a third party (403 policy_denied).
        var byCommon = await RemoveAsync(one, threadId, twoId);
        Assert.Equal(HttpStatusCode.Forbidden, byCommon.StatusCode);
        Assert.Contains("policy_denied", await byCommon.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The admin removes a member: 200, naming the seat that left.
        var removed = await RemoveAsync(creator, threadId, twoId);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Contains(twoId, await removed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The removed seat loses EVERY thread route at once — read and write alike, byte-identical
        // with a thread that never existed, no traceId, no participant list (FR-031/SC-005).
        var reads = new[]
        {
            await PostRoutes.SendAsync(two, HttpMethod.Get, $"{MessagesPath(threadId)}?since=0"),
            await PostRoutes.SendAsync(two, HttpMethod.Post, MessagesPath(threadId),
                new { content = "ainda estou aqui?" }, two.Csrf),
            await PostRoutes.SendAsync(two, HttpMethod.Get,
                $"{MessagesPath(AbsentThreadId(origin))}?since=0"),
        };
        Assert.All(reads, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var bodies = new List<byte[]>();
        foreach (var response in reads)
        {
            bodies.Add(await response.Content.ReadAsByteArrayAsync());
        }

        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal(bodies[0], bodies[2]);
        var text = Encoding.UTF8.GetString(bodies[0]);
        Assert.Contains("not_found", text, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", text, StringComparison.Ordinal);
        Assert.DoesNotContain(twoHandle, text, StringComparison.Ordinal);

        // And their own list drops the thread altogether.
        Assert.DoesNotContain(await ListAsync(two),
            item => item.GetProperty("id").GetString() == threadId);

        // History is untouched for whoever stays: both still read seq 1 and 2, in order.
        Assert.Equal(new long[] { 1, 2 }, await SeqsAsync(creator, threadId));
        Assert.Equal(new long[] { 1, 2 }, await SeqsAsync(one, threadId));

        // Third parties never take the creator's seat: the promotion that makes `one` an admin…
        var promoted = await PromoteAsync(creator, threadId, oneId);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal("admin", await RoleAsync(threadId, oneId));

        // …still leaves the creator out of their reach (403), with the seat untouched.
        var atCreator = await RemoveAsync(one, threadId, creatorId);
        Assert.Equal(HttpStatusCode.Forbidden, atCreator.StatusCode);
        Assert.Contains("policy_denied", await atCreator.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(creatorId, await CreatedByAsync(threadId));

        // Any participant takes THEMSELVES out, admin seat included.
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(one, threadId, oneId)).StatusCode);

        // And the very last seat does not leave: 409 with the new code, nothing written — the
        // creator still owns the thread and still reads its history.
        var last = await RemoveAsync(creator, threadId, creatorId);
        Assert.Equal(HttpStatusCode.Conflict, last.StatusCode);
        Assert.Contains("last_participant", await last.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(creatorId, await CreatedByAsync(threadId));
        Assert.Single(await ParticipantsAsync(creator, threadId));
        Assert.Equal(new long[] { 1, 2 }, await SeqsAsync(creator, threadId));
    }

    [Fact] // AC-02 (GAP-40: Direct sai de 2 para 1 com histórico legível)
    public async Task A_direct_thread_goes_from_two_to_one_with_its_history_readable_by_the_remaining_member()
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, "dircria");
        var (peer, peerHandle) = await PostRoutes.SignedInAsync(_host, "dirpar1");
        var origin = PostRoutes.Origin(_host);
        var ownerId = $"{origin}/users/{ownerHandle}";
        var peerId = $"{origin}/users/{peerHandle}";
        var threadId = await CreateAsync(owner, "direct", peerHandle);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, threadId, "uma")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, threadId, "duas")).StatusCode);

        // The peer takes themselves out: 2 → 1 (Direct accepts no third-party removal and no
        // addition at all), and the seat is gone byte-identically with an absent thread.
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(peer, threadId, peerId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostRoutes.SendAsync(peer, HttpMethod.Get,
            $"{MessagesPath(threadId)}?since=0")).StatusCode);

        // The remaining member keeps reading the whole history (GAP-40), and the last seat stays.
        Assert.Equal(new long[] { 1, 2 }, await SeqsAsync(owner, threadId));
        Assert.Equal(HttpStatusCode.Conflict, (await RemoveAsync(owner, threadId, ownerId)).StatusCode);
    }

    [Fact] // AC-03
    public async Task Any_admin_elects_admins_but_the_creator_seat_never_changes()
    {
        var (creator, creatorHandle) = await PostRoutes.SignedInAsync(_host, "admcria");
        var (one, oneHandle) = await PostRoutes.SignedInAsync(_host, "admmem1");
        var (two, twoHandle) = await PostRoutes.SignedInAsync(_host, "admmem2");
        var (outsider, outsiderHandle) = await PostRoutes.SignedInAsync(_host, "admout1");
        var origin = PostRoutes.Origin(_host);
        var creatorId = $"{origin}/users/{creatorHandle}";
        var oneId = $"{origin}/users/{oneHandle}";
        var twoId = $"{origin}/users/{twoHandle}";
        var threadId = await CreateAsync(creator, "group", oneHandle, twoHandle);

        // A plain member changes no papel, either verb (403 policy_denied).
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PromoteAsync(one, threadId, twoId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await DemoteAsync(one, threadId, creatorId)).StatusCode);

        // An outsider probes nothing: the admin route answers the SAME 404 body as an absent
        // thread — existence and participant list stay unreachable (FR-031).
        var foreign = await PostRoutes.SendAsync(outsider, HttpMethod.Post,
            AdminPath(threadId, twoId), null, outsider.Csrf);
        var absent = await PostRoutes.SendAsync(outsider, HttpMethod.Post,
            AdminPath(AbsentThreadId(origin), twoId), null, outsider.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await foreign.Content.ReadAsByteArrayAsync(),
            await absent.Content.ReadAsByteArrayAsync());

        // The creator elects an admin: 200, the answer names the seat and the new papel, and the
        // row carries it (role is store state, the way TASK-0033 pins the creator's own seat).
        var elected = await PromoteAsync(creator, threadId, oneId);
        Assert.Equal(HttpStatusCode.OK, elected.StatusCode);
        using (var json = JsonDocument.Parse(await elected.Content.ReadAsStringAsync()))
        {
            Assert.Equal(oneId, json.RootElement.GetProperty("actorId").GetString());
            Assert.Equal("admin", json.RootElement.GetProperty("role").GetString());
        }

        Assert.Equal("admin", await RoleAsync(threadId, oneId));

        // Admins elected by an admin elect in turn (AC-03: "inclusive admins eleitos")…
        Assert.Equal(HttpStatusCode.OK, (await PromoteAsync(one, threadId, twoId)).StatusCode);
        Assert.Equal("admin", await RoleAsync(threadId, twoId));

        // …and demote in turn: `two` goes back to member.
        var demoted = await DemoteAsync(one, threadId, twoId);
        Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);
        Assert.Equal("member", await RoleAsync(threadId, twoId));

        // A member again cannot touch papéis (403), and a seat outside the thread is the shared
        // 404 (nothing was written).
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PromoteAsync(two, threadId, oneId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PromoteAsync(creator, threadId, $"{origin}/users/{outsiderHandle}")).StatusCode);

        // The creator's seat: never demoted by another admin (403), still admin, still creator.
        var atCreator = await DemoteAsync(one, threadId, creatorId);
        Assert.Equal(HttpStatusCode.Forbidden, atCreator.StatusCode);
        Assert.Equal("admin", await RoleAsync(threadId, creatorId));
        Assert.Equal(creatorId, await CreatedByAsync(threadId));

        // But any admin demotes any OTHER admin — the creator demotes the one they elected.
        Assert.Equal(HttpStatusCode.OK, (await DemoteAsync(creator, threadId, oneId)).StatusCode);
        Assert.Equal("member", await RoleAsync(threadId, oneId));
    }

    [Fact] // AC-04 (GAP-32)
    public async Task The_creator_leave_hands_the_seat_to_the_oldest_admin_or_promotes_the_oldest_member()
    {
        var (creator, creatorHandle) = await PostRoutes.SignedInAsync(_host, "succria");
        var (one, oneHandle) = await PostRoutes.SignedInAsync(_host, "sucmem1");
        var (two, twoHandle) = await PostRoutes.SignedInAsync(_host, "sucmem2");
        var origin = PostRoutes.Origin(_host);
        var creatorId = $"{origin}/users/{creatorHandle}";
        var oneId = $"{origin}/users/{oneHandle}";
        var twoId = $"{origin}/users/{twoHandle}";

        // Road 2 of GAP-32 — no admin remains: the group is born with the creator as its only
        // admin, `one` and `two` share the clock of creation (rowid breaks the tie, no sleeping).
        var threadId = await CreateAsync(creator, "group", oneHandle, twoHandle);
        Assert.Equal(1, await AdminCountAsync(threadId));

        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(creator, threadId, creatorId)).StatusCode);

        // The oldest MEMBER became admin AND creator; the invariant holds: a group with seats
        // keeps at least one admin.
        Assert.Equal(oneId, await CreatedByAsync(threadId));
        Assert.Equal("admin", await RoleAsync(threadId, oneId));
        Assert.Equal("member", await RoleAsync(threadId, twoId));
        Assert.True(await AdminCountAsync(threadId) >= 1);

        // The new creator is protected exactly like the old one: an admin they just elected can
        // neither demote nor remove them (403 both), and the seat does not move.
        Assert.Equal(HttpStatusCode.OK, (await PromoteAsync(one, threadId, twoId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DemoteAsync(two, threadId, oneId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RemoveAsync(two, threadId, oneId)).StatusCode);
        Assert.Equal(oneId, await CreatedByAsync(threadId));
        Assert.Equal("admin", await RoleAsync(threadId, oneId));

        // Road 1 — an admin remains: `one` is elected into a second group, `two` joins LATER (a
        // larger joined_at) and is elected too; when the creator leaves, the OLDEST admin assumes.
        var secondId = await CreateAsync(creator, "group", oneHandle);
        Assert.Equal(HttpStatusCode.OK, (await PromoteAsync(creator, secondId, oneId)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(creator, secondId, new { actorId = twoHandle })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PromoteAsync(creator, secondId, twoId)).StatusCode);
        Assert.Equal(3, await AdminCountAsync(secondId)); // creator + the two elected admins

        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(creator, secondId, creatorId)).StatusCode);
        Assert.Equal(oneId, await CreatedByAsync(secondId));
        Assert.Equal("admin", await RoleAsync(secondId, oneId));
        Assert.Equal("admin", await RoleAsync(secondId, twoId)); // the later admin keeps the papel
        Assert.True(await AdminCountAsync(secondId) >= 1);
    }

    [Fact] // AC-05 (GAP-40)
    public async Task A_seat_added_later_reads_the_whole_history_and_joins_the_same_order()
    {
        var (creator, creatorHandle) = await PostRoutes.SignedInAsync(_host, "histcria");
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "histmem");
        var (late, lateHandle) = await PostRoutes.SignedInAsync(_host, "histlate");
        var origin = PostRoutes.Origin(_host);
        var lateId = $"{origin}/users/{lateHandle}";
        var threadId = await CreateAsync(creator, "group", memberHandle);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(creator, threadId, "antes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(member, threadId, "antes again")).StatusCode);

        // The seat added afterwards is a participant from the commit on: their own list carries
        // the thread with every seat…
        var added = await AddAsync(creator, threadId, new { actorId = lateHandle });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Contains(await ListAsync(late),
            item => item.GetProperty("id").GetString() == threadId);
        Assert.Equal(3, (await ParticipantsAsync(creator, threadId)).Count);

        // …and they read the WHOLE history, seq 1 onwards (GAP-40), not a delta from their entry.
        Assert.Equal(new long[] { 1, 2 }, await SeqsAsync(late, threadId));

        // Their own message joins the same total order, and everyone still sees all of it.
        var sent = await SendAsync(late, threadId, "depois");
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        using (var json = JsonDocument.Parse(await sent.Content.ReadAsStringAsync()))
        {
            Assert.Equal(3, json.RootElement.GetProperty("seq").GetInt64());
        }

        Assert.Equal(new long[] { 1, 2, 3 }, await SeqsAsync(creator, threadId));
        Assert.Equal(new long[] { 1, 2, 3 }, await SeqsAsync(late, threadId));
    }

    /// <summary>Creates a thread of <paramref name="kind"/> with these handle spellings (the creator counts).</summary>
    private async Task<string> CreateAsync(AuthClient owner, string kind, params string[] handles)
    {
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/threads",
            new { kind, participantActorIds = handles }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> AddAsync(AuthClient client, string threadId, object body) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, AddPath(threadId), body, client.Csrf);

    private Task<HttpResponseMessage> RemoveAsync(AuthClient client, string threadId, string actorId) =>
        PostRoutes.SendAsync(client, HttpMethod.Delete, SeatPath(threadId, actorId), null, client.Csrf);

    private Task<HttpResponseMessage> PromoteAsync(AuthClient client, string threadId, string actorId) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, AdminPath(threadId, actorId), null, client.Csrf);

    private Task<HttpResponseMessage> DemoteAsync(AuthClient client, string threadId, string actorId) =>
        PostRoutes.SendAsync(client, HttpMethod.Delete, AdminPath(threadId, actorId), null, client.Csrf);

    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client, string threadId, string content) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, MessagesPath(threadId),
            new { content }, client.Csrf);

    /// <summary>The seq of every message since 0 — the whole history, in the order of §9.3.</summary>
    private async Task<long[]> SeqsAsync(AuthClient client, string threadId)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get,
            $"{MessagesPath(threadId)}?since=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("seq").GetInt64()).ToArray();
    }

    /// <summary>Every item of <c>GET /api/threads</c>, parsed once (ThreadRoutesTests' shape).</summary>
    private async Task<List<JsonElement>> ListAsync(AuthClient client)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get, "/api/threads");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.Clone()).ToList();
    }

    /// <summary>The participants the list answers with for this thread — data-model §2.7's list.</summary>
    private async Task<List<string>> ParticipantsAsync(AuthClient client, string threadId)
    {
        var items = await ListAsync(client);
        var item = items.Single(value => value.GetProperty("id").GetString() == threadId);
        return item.GetProperty("participants").EnumerateArray()
            .Select(value => value.GetString()!).ToList();
    }

    /// <summary>The role a participant row carries — store state, the way TASK-0033 reads it.</summary>
    private async Task<string?> RoleAsync(string threadId, string actorId)
    {
        await using var connection = new SqliteConnection($"Data Source={_host.StoragePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT role FROM thread_participant WHERE thread_id = @ThreadId AND actor_id = @ActorId";
        command.Parameters.AddWithValue("@ThreadId", threadId);
        command.Parameters.AddWithValue("@ActorId", actorId);
        return (string?)await command.ExecuteScalarAsync();
    }

    /// <summary>The protected seat of GAP-30/32 — the row succession moves.</summary>
    private async Task<string?> CreatedByAsync(string threadId)
    {
        await using var connection = new SqliteConnection($"Data Source={_host.StoragePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT created_by FROM thread WHERE id = @ThreadId";
        command.Parameters.AddWithValue("@ThreadId", threadId);
        return (string?)await command.ExecuteScalarAsync();
    }

    /// <summary>The invariant of AC-04 in one number: a group with seats keeps an admin.</summary>
    private async Task<int> AdminCountAsync(string threadId)
    {
        await using var connection = new SqliteConnection($"Data Source={_host.StoragePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM thread_participant
            WHERE thread_id = @ThreadId AND role = 'admin'
            """;
        command.Parameters.AddWithValue("@ThreadId", threadId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>The route as the contract spells it: a thread id is a URI, so one escaped segment.</summary>
    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";

    private static string AddPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/participants";

    private static string SeatPath(string threadId, string actorId) =>
        $"{AddPath(threadId)}/{Uri.EscapeDataString(actorId)}";

    private static string AdminPath(string threadId, string actorId) =>
        $"{SeatPath(threadId, actorId)}/admin";

    private static string AbsentThreadId(string origin) =>
        $"{origin}/t/{Guid.NewGuid():N}";
}
