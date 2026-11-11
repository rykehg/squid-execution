using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.Domain.Containers;
using Squid.ReadModels.Projections;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Projections;

/// <summary>
/// QA-0023 (TASK-0024) - two scenarios the eight DEV tests do not reach. (1) The counter seam:
/// the global <c>Activity.Seq</c> counter moved into <c>activity_counter</c>, so append and
/// counter must be one indivisible step - a write that fails AFTER the increment must not keep
/// the number, and every number handed out must be readable in the log. (2) The membership
/// boundary of the SPEC-0009 section B amendment (2026-11-09 15:50:43): membership is read when
/// the projection RUNS, so a member who leaves between the event and its projection gets nothing.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0024Tests : IClassFixture<AuthHostFixture>
{
    /// <summary>Injected failure: a SQLite trigger that aborts the LAST write of an append.</summary>
    private const string FailureTrigger = "qa_injected_write_failure";

    private readonly AuthHostFixture _host;

    public QaTask0024Tests(AuthHostFixture host) => _host = host;

    private IActivityStore Activities =>
        _host.App.Services.GetRequiredService<IActivityStore>();

    private IActivityProjector Projector =>
        _host.App.Services.GetRequiredService<IActivityProjector>();

    private SqliteActivityLog Log =>
        _host.App.Services.GetRequiredService<SqliteActivityLog>();

    private IContainerStore Containers =>
        _host.App.Services.GetRequiredService<IContainerStore>();

    [Fact] // seam of AC-01/AC-05: the counter table this migration creates is the one append uses
    public async Task Counter_and_log_never_diverge_a_failed_append_takes_no_seq_and_a_consumed_seq_lands_in_the_log()
    {
        // At rest: the single row of activity_counter and the log itself agree (GAP-23).
        var counter0 = await CounterAsync();
        var atRest = await Activities.ReadAsync();
        Assert.Equal(atRest.Count == 0 ? 0 : atRest.Max(entry => entry.Seq), counter0);

        // A successful append hands out the number the counter holds, and the log carries it.
        var seed = await Activities.AppendAsync(Probe("seed"));
        Assert.Equal(counter0 + 1, seed.Seq);
        Assert.Equal(seed.Seq, await CounterAsync());
        Assert.Contains(await Activities.ReadAsync(), entry => entry.Seq == seed.Seq);

        // The failure is injected on the LAST write of the append (the log row), which happens
        // AFTER the counter increment of the same transaction: had the two been separate, the
        // counter would keep a number no event ever got.
        await ExecuteAsync(
            "CREATE TRIGGER " + FailureTrigger + " BEFORE UPDATE ON GrainState "
            + "WHEN NEW.StoreName = 'activities' "
            + "BEGIN SELECT RAISE(ABORT, 'injected write failure (QA-0023)'); END");

        var counterBeforeFailure = await CounterAsync();
        try
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(
                () => Activities.AppendAsync(Probe("failed")));
            Assert.Contains("injected write failure", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            await ExecuteAsync("DROP TRIGGER IF EXISTS " + FailureTrigger);
        }

        // The proof of atomicity: the increment rolled back with the write that never landed.
        Assert.Equal(counterBeforeFailure, await CounterAsync());
        var afterFailure = await Activities.ReadAsync();
        Assert.DoesNotContain(afterFailure, entry => entry.Seq > counterBeforeFailure);

        // The retry is handed the number the failed append took, and a delta read sees it:
        // no skipped number, no half-append, counter == max(seq) of the log at the end.
        var retried = await Activities.AppendAsync(Probe("retried"));
        Assert.Equal(counterBeforeFailure + 1, retried.Seq);
        Assert.Equal(retried.Seq, await CounterAsync());

        var delta = await Activities.ReadAsync(counterBeforeFailure);
        Assert.Equal(retried.Seq, Assert.Single(delta).Seq);

        var afterRetry = await Activities.ReadAsync();
        Assert.Equal(retried.Seq, afterRetry.Max(entry => entry.Seq));
        Assert.Equal(afterRetry.Max(entry => entry.Seq), await CounterAsync());
    }

    [Fact] // boundary of AC-02 under the SPEC-0009 B amendment of 2026-11-09 15:50:43
    public async Task A_member_who_leaves_before_the_projection_runs_gets_no_line_membership_is_read_at_projection_time()
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, "qa0023dono");
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "qa0023membro");
        var origin = PostRoutes.Origin(_host);
        var slug = PostRoutes.Slug("qa0023borda");

        // A community admits joiners, so the second account becomes Active by the entry route.
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(owner, HttpMethod.Post,
            "/api/containers", new { kind = "community", name = "Borda", slug }, owner.Csrf)).StatusCode);
        var joined = await PostRoutes.SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");

        var published = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "post da borda" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);

        string postId, containerId;
        using (var view = JsonDocument.Parse(await published.Content.ReadAsStringAsync()))
        {
            postId = view.RootElement.GetProperty("id").GetString()!;
            containerId = view.RootElement.GetProperty("containerId").GetString()!;
        }

        // The event is in the log and nothing has projected it yet.
        var activity = Assert.Single(await Log.ReadAsync(), entry => entry.ObjectId == postId);

        // THE BOUNDARY: the member leaves BETWEEN the event and its projection. The projector
        // reads membership when it runs, not when the event happened (spec amendment).
        await Containers.UpsertMembershipAsync(new ContainerMembership
        {
            Slug = slug,
            ActorId = $"{origin}/users/{memberHandle}",
            Handle = memberHandle,
            Role = MembershipRole.Member,
            State = MembershipState.Left,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await Projector.ProjectAsync(activity);

        // The summary line exists with the post as it was published...
        Assert.Equal("post da borda", await ScalarAsync<string>(
            "SELECT content FROM timeline_item WHERE container_id = @ContainerId AND seq = @Seq",
            new { ContainerId = containerId, Seq = activity.Seq }));

        // ...and only the members Active AT THAT MOMENT got a home line: the owner still is,
        // the member who left is not.
        var actors = await ActorsAsync(containerId, activity.Seq);
        Assert.Equal(new[] { $"{origin}/users/{ownerHandle}" }, actors);
        Assert.DoesNotContain($"{origin}/users/{memberHandle}", actors);
    }

    [Fact] // first clause of AC-04: projecting the same event again never doubles a line
    public async Task Projecting_the_same_event_a_second_time_leaves_exactly_the_same_lines()
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, "qa0023rep");
        var (member, memberHandle) = await PostRoutes.SignedInAsync(_host, "qa0023repmembro");
        var origin = PostRoutes.Origin(_host);
        var slug = PostRoutes.Slug("qa0023repete");

        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(owner, HttpMethod.Post,
            "/api/containers", new { kind = "community", name = "Repete", slug }, owner.Csrf)).StatusCode);
        var joined = await PostRoutes.SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf);
        Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");

        var published = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "post repetido" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);

        string postId, containerId;
        using (var view = JsonDocument.Parse(await published.Content.ReadAsStringAsync()))
        {
            postId = view.RootElement.GetProperty("id").GetString()!;
            containerId = view.RootElement.GetProperty("containerId").GetString()!;
        }

        var activity = Assert.Single(await Log.ReadAsync(), entry => entry.ObjectId == postId);

        await Projector.ProjectAsync(activity);
        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM timeline_item WHERE container_id = @ContainerId AND seq = @Seq",
            new { ContainerId = containerId, Seq = activity.Seq }));
        var actorsAfterFirst = await ActorsAsync(containerId, activity.Seq);
        Assert.Equal(
            new[] { $"{origin}/users/{ownerHandle}", $"{origin}/users/{memberHandle}" }
                .Order(StringComparer.Ordinal),
            actorsAfterFirst.Order(StringComparer.Ordinal));

        // The same event handed to the projection again - a pump that repeated an event.
        await Projector.ProjectAsync(activity);

        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM timeline_item WHERE container_id = @ContainerId AND seq = @Seq",
            new { ContainerId = containerId, Seq = activity.Seq }));
        Assert.Equal(actorsAfterFirst, await ActorsAsync(containerId, activity.Seq));
    }

    /// <summary>One activity event the store is asked to number itself (Seq is the store's).</summary>
    private static ActivityEvent Probe(string token) => new(
        Id: $"urn:squid:activity:qa0023-{token}",
        Type: "Create",
        ActorId: "urn:squid:users/qa0023",
        ObjectId: $"urn:squid:o/qa0023-{token}",
        Seq: 0,
        Payload: $"{{\"qa\":\"QA-0023 counter probe {token}\"}}",
        CreatedAt: DateTimeOffset.UtcNow);

    private async Task<long> CounterAsync()
    {
        var value = await ScalarAsync<long?>(
            "SELECT last_seq FROM activity_counter WHERE id = 1");
        return value ?? throw new InvalidOperationException(
            "the activity_counter row of 0001_init_projections.sql is missing");
    }

    private async Task<List<string>> ActorsAsync(string containerId, long seq)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT actor_id FROM home_timeline WHERE container_id = @ContainerId AND seq = @Seq "
            + "ORDER BY actor_id";
        command.Parameters.AddWithValue("@ContainerId", containerId);
        command.Parameters.AddWithValue("@Seq", seq);

        var actors = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) actors.Add(reader.GetString(0));
        return actors;
    }

    private async Task<T?> ScalarAsync<T>(string sql, object? param = null)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (param is not null)
        {
            foreach (var property in param.GetType().GetProperties())
            {
                command.Parameters.AddWithValue("@" + property.Name, property.GetValue(param)!);
            }
        }

        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull) return default;
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, type);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(ProjectionDb.ConnectionOf(_host.StoragePath));
        await connection.OpenAsync();
        return connection;
    }
}
