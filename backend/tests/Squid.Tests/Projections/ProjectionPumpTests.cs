using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Squid.Api.Posts;
using Squid.ReadModels.Projections;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Squid.Worker.Projection;

namespace Squid.Tests.Projections;

/// <summary>
/// AC-04 and AC-05 of TASK-0024. AC-04: a pass that fails in the middle of a batch leaves the
/// events before it projected, and the next pass starts at the first event without a row â€” never
/// repeating one, never losing one. AC-05: the pump is a hosted service of the Squid.Worker
/// composition root and consumes the log in Seq order, and the versioned migration is applied by
/// the host's init (Build) before any test action touches the file.
/// </summary>
[Collection("ServiceHost")]
public sealed class ProjectionPumpTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ProjectionPumpTests(AuthHostFixture host) => _host = host;

    private SqliteActivityLog Log =>
        _host.App.Services.GetRequiredService<SqliteActivityLog>();

    private IActivityProjector Projector =>
        _host.App.Services.GetRequiredService<IActivityProjector>();

    private IActivityStore Activities =>
        _host.App.Services.GetRequiredService<IActivityStore>();

    [Fact]
    public async Task Ac05_building_the_host_applies_the_migration_before_any_request_or_test_runs()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            // Build is the init of dev/test/CI (AC-05): the application object exists, nothing was
            // started, no request was made â€” and the schema is already there on a brand-new file.
            var app = SquidServiceHost.Build(
                ["--Storage:Path=" + file, "--Logging:LogLevel:Default=Warning"]);

            foreach (var table in new[] { "schema_version", "activity_counter", "timeline_item", "home_timeline", "quota_ledger" })
            {
                Assert.True(await ProjectionDb.TableExistsAsync(file, table),
                    $"'{table}' was not applied by the host init");
            }

            // Every version of the family landed with its checksum (TASK-0033 added 0002_threads).
            var rows = await ProjectionDb.QueryAsync<VersionRow>(file,
                "SELECT version AS Version, name AS Name, checksum AS Checksum FROM schema_version ORDER BY version");
            Assert.Equal(SchemaMigrations.Load().Select(script => (long)script.Version),
                rows.Select(row => row.Version));
            Assert.Equal(1, rows[0].Version);
            Assert.False(string.IsNullOrWhiteSpace(rows[0].Checksum));

            await app.DisposeAsync();
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }

    [Fact]
    public async Task Ac05_the_pump_is_a_worker_hosted_service_and_consumes_the_log_in_seq_order()
    {
        Assert.True(typeof(IHostedService).IsAssignableFrom(typeof(ActivityProjectionPump)),
            "the pump must be a hosted service");
        Assert.Equal("Squid.Worker", typeof(ActivityProjectionPump).Assembly.GetName().Name);

        // One publish guarantees at least one event in the window this pass will read.
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "proj05ordem");
        var slug = PostRoutes.Slug("ordem");
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(owner, HttpMethod.Post,
            "/api/containers", new { kind = "group", name = "Ordem", slug }, owner.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "primeiro da ordem" }, owner.Csrf)).StatusCode);

        var since = await Projector.LastProjectedSeqAsync();
        var expected = await Log.ReadAsync(since);
        Assert.NotEmpty(expected);

        var recorder = new RecordingProjector(Projector);
        var pump = new ActivityProjectionPump(Log, recorder, NullLogger<ActivityProjectionPump>.Instance);
        var consumed = await pump.RunOnceAsync();

        Assert.Equal(expected.Count, consumed);
        Assert.Equal(expected.Select(entry => entry.Seq), recorder.Seqs);
        Assert.True(recorder.Seqs.SequenceEqual(recorder.Seqs.OrderBy(seq => seq)),
            "the pump projected events out of Seq order: " + string.Join(", ", recorder.Seqs));
    }

    [Fact]
    public async Task Ac04_a_pass_failing_midway_resumes_at_the_next_event_and_never_duplicates_a_line()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "proj04retomada");
        var slug = PostRoutes.Slug("retomada");
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(owner, HttpMethod.Post,
            "/api/containers", new { kind = "group", name = "Retomada", slug }, owner.Csrf)).StatusCode);

        // Leave the projection caught up, so the batch below is exactly these three publishes.
        await new ActivityProjectionPump(Log, Projector, NullLogger<ActivityProjectionPump>.Instance)
            .RunOnceAsync();

        var ids = new List<string>();
        string containerId = string.Empty;
        for (var position = 1; position <= 3; position++)
        {
            var published = await PostRoutes.SendAsync(owner, HttpMethod.Post,
                $"/api/containers/{slug}/posts", new { content = $"retomada {position}" }, owner.Csrf);
            Assert.Equal(HttpStatusCode.Created, published.StatusCode);
            using var view = JsonDocument.Parse(await published.Content.ReadAsStringAsync());
            containerId = view.RootElement.GetProperty("containerId").GetString()!;
            ids.Add(view.RootElement.GetProperty("id").GetString()!);
        }

        var events = (await Activities.ReadAsync())
            .Where(entry => ids.Contains(entry.ObjectId))
            .OrderBy(entry => entry.Seq)
            .ToArray();
        Assert.Equal(3, events.Length);

        // Pass 1: the projection fails on the SECOND event of the batch â€” the crash of AC-04.
        var failing = new ActivityProjectionPump(
            Log, new FailingProjector(Projector, 1),
            NullLogger<ActivityProjectionPump>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RunOnceAsync());

        Assert.Equal(1, await RowCountAsync(containerId, events[0].Seq));

        // Pass 2: a fresh pump resumes from the last seq the tables hold and continues â€” the
        // recorded order IS the proof of where it restarted.
        var recorder = new RecordingProjector(Projector);
        var resumed = new ActivityProjectionPump(Log, recorder, NullLogger<ActivityProjectionPump>.Instance);
        var consumed = await resumed.RunOnceAsync();

        Assert.Equal(2, consumed);
        Assert.Equal(new[] { events[1].Seq, events[2].Seq }, recorder.Seqs);
        Assert.Equal(3, await RowCountAsync(containerId, events[2].Seq));

        // Pass 3: everything is projected â€” a re-run finds nothing and writes nothing twice.
        Assert.Equal(0, await resumed.RunOnceAsync());
        Assert.Equal(3, await RowCountAsync(containerId, events[2].Seq));
    }

    /// <summary>The lines of this test's own feed source up to a seq â€” the numbers prove no row was doubled.</summary>
    private Task<long> RowCountAsync(string containerId, long throughSeq) =>
        ProjectionDb.ScalarAsync<long>(_host.StoragePath,
            "SELECT COUNT(*) FROM timeline_item WHERE container_id = @ContainerId AND seq <= @ThroughSeq",
            new { ContainerId = containerId, ThroughSeq = throughSeq });

    /// <summary>One <c>schema_version</c> row, bound the way Dapper binds a constructor.</summary>
    private sealed record VersionRow(long Version, string Name, string Checksum);
}
