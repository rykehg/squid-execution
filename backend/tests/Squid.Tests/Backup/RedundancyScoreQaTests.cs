using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Crypto;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// QA-0060's own scenario (TASK-0060 AC-02): the thin page must print the drill INSTANT the
/// ledger holds. The DEV facts assert <c>restore.last_drill_at</c> only by label and by the
/// shared <c>—</c> placeholder, so a page that hardcoded or mangled the VALUE would still pass
/// them. The expected string is read from <c>GET /api/admin/backup/status</c> first: the page
/// has to reproduce the JSON route's value byte-identically (SPEC-0012 section G, "leitura do
/// endpoint JSON acima").
/// </summary>
[Collection("ServiceHost")] // this fact starts a host; hosts never overlap
public sealed class RedundancyScoreQaTests
{
    [Fact]
    public async Task Ac02_the_page_prints_the_drill_values_the_json_route_answers()
    {
        var handle = PostRoutes.Handle("redqadrill");
        var storage = NewDb();
        var volume = NewVolume();
        try
        {
            await using var host = await StartAsync(storage, volume, handle);

            var client = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await client.RegisterAsync(handle, PostRoutes.Password)).StatusCode);

            // One drill recorded by the ledger of TASK-0056 — the state both surfaces read.
            var ledger = host.App.Services.GetRequiredService<SqliteRestoreDrillLedger>();
            await ledger.RecordAsync("restore-roundtrip", "passed");

            // The artifact first: what GET /api/admin/backup/status answers for restore.*.
            var status = await PostRoutes.SendAsync(
                client, HttpMethod.Get, "/api/admin/backup/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            string drillAt;
            string drillResult;
            using (var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
            {
                var restore = document.RootElement.GetProperty("restore");
                drillAt = restore.GetProperty("last_drill_at").GetString()!;
                drillResult = restore.GetProperty("last_drill_result").GetString()!;
            }

            Assert.False(string.IsNullOrEmpty(drillAt), "the ledger must answer an instant");
            Assert.Equal("restore-roundtrip: passed", drillResult);

            // The page reproduces both values verbatim — not a placeholder, not a reformat.
            var page = await PostRoutes.SendAsync(client, HttpMethod.Get, "/admin/backup");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains($"<dd>{drillAt}</dd>", html, StringComparison.Ordinal);
            Assert.Contains($"<dd>{drillResult}</dd>", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(storage, volume);
        }
    }

    private static async Task<BackupHost> StartAsync(
        string storage, string volume, string operatorHandle)
    {
        var recoveryKey = Convert.ToBase64String(RootKey.Create().AsSpan());
        return await BackupHost.StartAsync(
        [
            "--Storage:Path=" + storage,
            "--Logging:LogLevel:Default=Warning",
            "--Instance:Operators=" + operatorHandle,
            "--Backup:Local:Path=" + volume,
            "--Backup:IntervalMinutes=0", // the scheduler has no business in this fact
            "--Squid:RecoveryKey=" + recoveryKey,
        ]);
    }

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"squid-redundancy-qa-{Guid.NewGuid():N}.db");

    private static string NewVolume() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"squid-redundancy-qa-{Guid.NewGuid():N}")).FullName;

    private static void Cleanup(string storage, string volume)
    {
        ProjectionDb.TryDelete(storage);
        try
        {
            Directory.Delete(volume, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a product failure; the OS cleans it up.
        }
    }
}
