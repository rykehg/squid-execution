using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-02..AC-04 of TASK-0033 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// Direct = exactly 2 participants with the creator among them and Group = 2..50 with the creator
/// as its admin (GAP-30), out of range refusing with 400 (AC-02); the list that answers only with
/// the threads the actor participates in, both counters born 0 (AC-03); and session + CSRF on both
/// routes with the thread id on the request's own scheme+Host — the P-03 emenda (AC-04).
/// </summary>
[Collection("ServiceHost")]
public sealed class ThreadRoutesTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ThreadRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-02 (direct)
    public async Task A_direct_thread_has_exactly_two_participants_with_the_creator_among_them()
    {
        var (creator, creatorHandle) = await PostRoutes.SignedInAsync(_host, "thrcria");
        var (other, otherHandle) = await PostRoutes.SignedInAsync(_host, "throutro");
        var origin = PostRoutes.Origin(_host);

        var created = await PostRoutes.SendAsync(creator, HttpMethod.Post, "/api/threads",
            new { kind = "direct", participantActorIds = new[] { $"{origin}/users/{otherHandle}" } },
            creator.Csrf);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("direct", root.GetProperty("kind").GetString());
        Assert.Equal(0, root.GetProperty("messageSeq").GetInt64());
        Assert.Equal(0, root.GetProperty("lastReadSeq").GetInt64());
        Assert.Equal(
            new[] { $"{origin}/users/{creatorHandle}", $"{origin}/users/{otherHandle}" },
            root.GetProperty("participants").EnumerateArray().Select(value => value.GetString()));

        // The creator spelled in the body counts once: the SET is what the range measures.
        var withCreator = await PostRoutes.SendAsync(creator, HttpMethod.Post, "/api/threads",
            new
            {
                kind = "direct",
                participantActorIds = new[]
                {
                    $"{origin}/users/{creatorHandle}",
                    $"{origin}/users/{otherHandle}",
                },
            },
            creator.Csrf);
        Assert.Equal(HttpStatusCode.Created, withCreator.StatusCode);
        using var again = JsonDocument.Parse(await withCreator.Content.ReadAsStringAsync());
        Assert.Equal(2, again.RootElement.GetProperty("participants").GetArrayLength());

        // Out of range: one participant is not a direct, three is not a direct, and a kind word
        // outside the contract is refused — 400 every time (a refusal writes nothing).
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRoutes.SendAsync(creator, HttpMethod.Post,
            "/api/threads", new { kind = "direct", participantActorIds = Array.Empty<string>() },
            creator.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRoutes.SendAsync(creator, HttpMethod.Post,
            "/api/threads", new { kind = "direct", participantActorIds = new[] { "foraum", "foradois" } },
            creator.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRoutes.SendAsync(creator, HttpMethod.Post,
            "/api/threads", new { kind = "comunidade" }, creator.Csrf)).StatusCode);
    }

    [Fact] // AC-02 (group)
    public async Task A_group_thread_runs_the_creator_as_admin_and_refuses_anything_outside_two_to_fifty()
    {
        var (creator, creatorHandle) = await PostRoutes.SignedInAsync(_host, "thrgrupo");
        var origin = PostRoutes.Origin(_host);

        // The floor: creator + one more = 2, and the creator's row carries the admin seat (GAP-30).
        var created = await PostRoutes.SendAsync(creator, HttpMethod.Post, "/api/threads",
            new { kind = "group", participantActorIds = new[] { "membro1" } }, creator.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString();
        Assert.Equal(2, json.RootElement.GetProperty("participants").GetArrayLength());
        Assert.Equal("admin", await RoleAsync(id, $"{origin}/users/{creatorHandle}"));
        Assert.Equal("member", await RoleAsync(id, $"{origin}/users/membro1"));

        // The ceiling: 49 in the body + the creator = 50, still created.
        var full = await PostRoutes.SendAsync(creator, HttpMethod.Post, "/api/threads",
            new { kind = "group", participantActorIds = Enumerable.Range(1, 49).Select(i => $"membro{i}") },
            creator.Csrf);
        Assert.Equal(HttpStatusCode.Created, full.StatusCode);

        // One past the ceiling (51) and one below the floor (1): 400, both.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRoutes.SendAsync(creator, HttpMethod.Post,
            "/api/threads", new { kind = "group", participantActorIds = Enumerable.Range(1, 50).Select(i => $"fora{i}") },
            creator.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRoutes.SendAsync(creator, HttpMethod.Post,
            "/api/threads", new { kind = "group", participantActorIds = Array.Empty<string>() },
            creator.Csrf)).StatusCode);
    }

    [Fact] // AC-03
    public async Task The_list_answers_only_with_the_threads_the_actor_is_in_and_both_counters_start_at_zero()
    {
        var (alice, aliceHandle) = await PostRoutes.SignedInAsync(_host, "thralice");
        var (bob, bobHandle) = await PostRoutes.SignedInAsync(_host, "thrbob");
        var origin = PostRoutes.Origin(_host);

        // Alice in a direct with Bob and in a group; Bob opens a thread of his own.
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(alice, HttpMethod.Post,
            "/api/threads", new { kind = "direct", participantActorIds = new[] { $"{origin}/users/{bobHandle}" } },
            alice.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(alice, HttpMethod.Post,
            "/api/threads", new { kind = "group", participantActorIds = new[] { "convidado" } },
            alice.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(bob, HttpMethod.Post,
            "/api/threads", new { kind = "direct", participantActorIds = new[] { "terceiro" } },
            bob.Csrf)).StatusCode);

        var aliceItems = await ListAsync(alice);
        Assert.Equal(2, aliceItems.Count);
        foreach (var item in aliceItems)
        {
            Assert.Equal(0, item.GetProperty("messageSeq").GetInt64());
            Assert.Equal(0, item.GetProperty("lastReadSeq").GetInt64());
            Assert.Contains($"{origin}/users/{aliceHandle}", Participants(item));
        }

        var aliceDirectId = Item(aliceItems, "direct").GetProperty("id").GetString();
        var aliceGroupId = Item(aliceItems, "group").GetProperty("id").GetString();

        // Bob sits in the direct Alice started and in his own thread — never in her group (FR-031).
        var bobItems = await ListAsync(bob);
        Assert.Equal(2, bobItems.Count);
        var bobIds = bobItems.Select(item => item.GetProperty("id").GetString()).ToList();
        Assert.Contains(aliceDirectId, bobIds);
        Assert.DoesNotContain(aliceGroupId, bobIds);

        var bobOwnId = bobIds.Single(value => value != aliceDirectId);
        Assert.DoesNotContain(bobOwnId, aliceItems.Select(item => item.GetProperty("id").GetString()));
        Assert.Contains($"{origin}/users/{bobHandle}", Participants(Item(bobItems, "direct",
            item => item.GetProperty("id").GetString() == bobOwnId)));
        foreach (var item in bobItems)
        {
            Assert.Equal(0, item.GetProperty("messageSeq").GetInt64());
            Assert.Equal(0, item.GetProperty("lastReadSeq").GetInt64());
        }
    }

    [Fact] // AC-04
    public async Task Both_routes_demand_a_session_the_mutation_its_csrf_and_the_id_follows_the_origin()
    {
        var anonymous = _host.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(HttpMethod.Post,
            "/api/threads", new { kind = "direct", participantActorIds = new[] { "qualquer" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.SendAsync(HttpMethod.Get, "/api/threads")).StatusCode);

        var (creator, _) = await PostRoutes.SignedInAsync(_host, "throrigem");
        // A session without the CSRF header is refused by the middleware (double submission).
        Assert.Equal(HttpStatusCode.Forbidden, (await creator.SendAsync(HttpMethod.Post,
            "/api/threads", new { kind = "direct", participantActorIds = new[] { "qualquer" } })).StatusCode);

        var (_, otherHandle) = await PostRoutes.SignedInAsync(_host, "throrigem2");
        var created = await PostRoutes.SendAsync(creator, HttpMethod.Post, "/api/threads",
            new
            {
                kind = "direct",
                participantActorIds = new[] { $"{PostRoutes.Origin(_host)}/users/{otherHandle}" },
            },
            creator.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString();

        // Emenda P-03: scheme + Host of the very request that asked — this host speaks http, so
        // the id is born http, never a configured https (the rule ContainerRoutesTests pins).
        Assert.StartsWith(
            $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}/t/", id);
    }

    /// <summary>Every item of <c>GET /api/threads</c>, parsed once.</summary>
    private async Task<List<JsonElement>> ListAsync(AuthClient client)
    {
        var response = await PostRoutes.SendAsync(client, HttpMethod.Get, "/api/threads");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.Clone()).ToList();
    }

    private static JsonElement Item(
        IReadOnlyList<JsonElement> items, string kind, Func<JsonElement, bool>? match = null) =>
        items.Single(item =>
            item.GetProperty("kind").GetString() == kind
            && (match is null || match(item)));

    private static IEnumerable<string?> Participants(JsonElement item) =>
        item.GetProperty("participants").EnumerateArray().Select(value => value.GetString());

    /// <summary>The role a participant row landed with — AC-02's admin seat is store state.</summary>
    private async Task<string?> RoleAsync(string? threadId, string actorId)
    {
        await using var connection = new SqliteConnection($"Data Source={_host.StoragePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT role FROM thread_participant WHERE thread_id = @ThreadId AND actor_id = @ActorId";
        command.Parameters.AddWithValue("@ThreadId", threadId ?? string.Empty);
        command.Parameters.AddWithValue("@ActorId", actorId);
        return (string?)await command.ExecuteScalarAsync();
    }
}
