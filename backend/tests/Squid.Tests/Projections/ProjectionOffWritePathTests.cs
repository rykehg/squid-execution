using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Squid.ReadModels.Projections;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Squid.Worker.Projection;

namespace Squid.Tests.Projections;

/// <summary>
/// AC-03 of TASK-0024 — the projection is OFF the write path (R-18): this class is the only test
/// of its fixture, so on a brand-new database a publish succeeds while the projection tables stay
/// empty; only when the test runs the pump do the lines appear. The host registers the projector
/// and its log as services — it never starts them.
/// </summary>
[Collection("ServiceHost")]
public sealed class ProjectionOffWritePathTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public ProjectionOffWritePathTests(AuthHostFixture host) => _host = host;

    [Fact]
    public async Task Ac03_publishing_projects_nothing_until_the_pump_runs_then_the_lines_appear()
    {
        // Fresh fixture, nothing was ever projected in this file.
        Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(_host.StoragePath, "SELECT COUNT(*) FROM timeline_item"));
        Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(_host.StoragePath, "SELECT COUNT(*) FROM home_timeline"));

        var (owner, handle) = await PostRoutes.SignedInAsync(_host, "proj03");
        var slug = PostRoutes.Slug("foradocaminho");
        Assert.Equal(HttpStatusCode.Created, (await PostRoutes.SendAsync(owner, HttpMethod.Post,
            "/api/containers", new { kind = "group", name = "Fora do caminho", slug }, owner.Csrf)).StatusCode);

        var published = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "post sem projecao" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);

        string containerId;
        using (var view = JsonDocument.Parse(await published.Content.ReadAsStringAsync()))
        {
            containerId = view.RootElement.GetProperty("containerId").GetString()!;
        }

        // The write went through (201 above) and not one projection row exists: no hosted service
        // ran, because the pump is a Squid.Worker service the host does not register.
        Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(_host.StoragePath,
            "SELECT COUNT(*) FROM timeline_item"));
        Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(_host.StoragePath,
            "SELECT COUNT(*) FROM home_timeline"));

        // Executing the projection — the pump's own pass, invoked by the test — is what makes the lines appear.
        var pump = new ActivityProjectionPump(
            _host.App.Services.GetRequiredService<SqliteActivityLog>(),
            _host.App.Services.GetRequiredService<IActivityProjector>(),
            NullLogger<ActivityProjectionPump>.Instance);

        Assert.True(await pump.RunOnceAsync() >= 1, "the pump consumed nothing while the log held events");

        Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(_host.StoragePath,
            "SELECT COUNT(*) FROM timeline_item WHERE container_id = @ContainerId",
            new { ContainerId = containerId }));
        Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(_host.StoragePath,
            "SELECT COUNT(*) FROM home_timeline WHERE container_id = @ContainerId",
            new { ContainerId = containerId }));
        Assert.Equal($"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}/users/{handle}",
            await ProjectionDb.ScalarAsync<string>(_host.StoragePath,
                "SELECT actor_id FROM home_timeline WHERE container_id = @ContainerId",
                new { ContainerId = containerId }));
    }
}
