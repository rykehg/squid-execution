using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Storage;
using Squid.Service;

namespace Squid.Tests.ServiceHost;

/// <summary>
/// AC-01..AC-05 of TASK-0005, against the host <see cref="SquidServiceHost"/> builds. Each test
/// starts its own host on a dynamic port with its own temp storage file; hosts never overlap,
/// because localhost clustering fixes the silo ports for every silo — the shared ServiceHost
/// collection is what keeps the other host-starting classes from overlapping this one.
/// </summary>
[Collection("ServiceHost")]
public sealed class ServiceHostTests
{
    [Fact] // AC-01
    public async Task Health_returns_200_while_the_host_lives()
    {
        await using var host = await StartHostAsync();

        var response = await host.Http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] // AC-02
    public async Task Ready_is_200_with_storage_and_503_when_the_file_is_renamed()
    {
        await using var host = await StartHostAsync();
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/ready")).StatusCode);

        var renamed = host.StoragePath + ".renamed";
        File.Move(host.StoragePath, renamed);
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Http.GetAsync("/ready")).StatusCode);
            // liveness never touches the database: the missing file does not affect /health
            Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/health")).StatusCode);
        }
        finally
        {
            File.Move(renamed, host.StoragePath);
        }

        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/ready")).StatusCode);
    }

    [Fact] // AC-03
    public async Task Grain_persists_state_and_a_stale_version_write_conflicts()
    {
        await using var host = await StartHostAsync();
        var grain = host.App.Services.GetRequiredService<IGrainFactory>().GetGrain<IProbeGrain>("ac03");

        await grain.SaveAsync("written"); // write through the grain and the registered storage
        Assert.Equal("written", await grain.ReadAsync());

        var grainId = GrainId.Parse(await grain.IdAsync());
        // the same keyed registration the silo resolves the grain's state from
        var storage = host.App.Services.GetKeyedService<IGrainStorage>(SquidServiceHost.StorageProviderName);
        Assert.NotNull(storage);

        var current = new GrainState<ProbeState>();
        await storage.ReadStateAsync(ProbeState.StateName, grainId, current);
        Assert.Equal("written", current.State.Value);

        await grain.SaveAsync("written again"); // the row version moves on

        var stale = new GrainState<ProbeState> { State = current.State, ETag = current.ETag, RecordExists = true };
        await Assert.ThrowsAsync<InconsistentStateException>(
            () => storage.WriteStateAsync(ProbeState.StateName, grainId, stale));

        var after = new GrainState<ProbeState>();
        await storage.ReadStateAsync(ProbeState.StateName, grainId, after);
        Assert.Equal("written again", after.State.Value); // the stale write changed nothing
    }

    [Fact] // AC-04
    public async Task Dashboard_answers_locally_and_reports_the_active_silo()
    {
        await using var host = await StartHostAsync();

        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/dashboard/")).StatusCode);
        // the bare route answers with a slash redirect the client follows to 200
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/dashboard")).StatusCode);

        // The SPA polls DashboardCounters; the dashboard's collector reports the silo as Active.
        var body = string.Empty;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            body = await host.Http.GetStringAsync("/dashboard/DashboardCounters");
            if (IsReportedActive(body)) break;
            await Task.Delay(500);
        }

        Assert.True(IsReportedActive(body), $"dashboard counters never showed the active silo: {Truncate(body)}");
    }

    [Fact] // TASK-0011 AC-04: can-fail guard — fails again if the /dashboard rewrite comes back
    public async Task Bare_dashboard_redirects_to_the_slash_url()
    {
        await using var host = await StartHostAsync();

        using var direct = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(host.BaseUrl),
        };
        var response = await direct.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/dashboard/", response.Headers.Location?.ToString());
        // the client that follows the redirect lands on the canonical URL with 200 (AC-01)
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync("/dashboard")).StatusCode);
    }

    [Fact] // AC-05
    public async Task Second_host_on_a_busy_port_fails_fast_instead_of_hanging()
    {
        await using var first = await StartHostAsync();
        var second = SquidServiceHost.Build([
            "--Storage:Path=" + first.StoragePath,
            "--Logging:LogLevel:Default=Warning",
        ]);
        second.Urls.Add(first.BaseUrl);
        try
        {
            var starting = second.StartAsync();
            var finished = await Task.WhenAny(starting, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.Same(starting, finished); // it did not hang

            var error = await Assert.ThrowsAsync<IOException>(() => starting);
            Assert.Contains("address already in use", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            await second.DisposeAsync();
        }
    }

    private sealed record RunningHost(WebApplication App, HttpClient Http, string BaseUrl, string StoragePath) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            try
            {
                File.Delete(StoragePath);
            }
            catch (IOException)
            {
                // a leftover temp file is not a product failure; the OS cleans it up
            }
        }
    }

    private static async Task<RunningHost> StartHostAsync()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"squid-svc-{Guid.NewGuid():N}.db");
        var app = SquidServiceHost.Build([
            "--Storage:Path=" + storagePath,
            "--Logging:LogLevel:Default=Warning",
        ]);
        app.Urls.Add("http://127.0.0.1:0"); // dynamic port: tests never fight over a fixed one
        await app.StartAsync();

        var baseUrl = app.Urls.Single();
        return new RunningHost(app, new HttpClient { BaseAddress = new Uri(baseUrl) }, baseUrl, storagePath);
    }

    private static bool IsReportedActive(string counters) =>
        counters.Contains("\"status\":\"Active\"", StringComparison.Ordinal)
        && counters.Contains("127.0.0.1:11111", StringComparison.Ordinal);

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];
}
