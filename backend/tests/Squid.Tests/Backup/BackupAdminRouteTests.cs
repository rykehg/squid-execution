using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Admin;
using Squid.Api.Crypto;
using Squid.Api.Auth;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-03..AC-06 of TASK-0055 through the HTTP surface SPEC-0012 section C opens: the two routes
/// behind the P-03 operator gate (401 without a session, 403 outside <c>Instance:Operators</c>),
/// the section 6 status they answer, the alerts section 6 demands - a stale last success and any
/// verify failure - and a destination that is down answering a fixed 500 while the process keeps
/// serving.
/// </summary>
[Collection("ServiceHost")]
public sealed class BackupAdminRouteTests
{
    [Fact] // AC-03, AC-04, AC-05 (stale half)
    public async Task Ac03_ac04_ac05_The_routes_are_operator_only_and_the_status_answers_section_6()
    {
        var operatorHandle = PostRoutes.Handle("bkoper");
        var strangerHandle = PostRoutes.Handle("bkstran");
        var storage = NewDb();
        var volume = NewVolume();
        var alerts = new CapturingLogger<BackupAlerter>();
        try
        {
            await using var host = await StartAsync(storage, volume, operatorHandle, alerts);

            // No session at all: 401 on both routes, before anything else is looked at.
            var anonymous = host.NewClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(
                HttpMethod.Post, "/api/admin/backup/run")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(
                HttpMethod.Get, "/api/admin/backup/status")).StatusCode);

            // A session outside Instance:Operators: 403 policy_denied on both (SPEC-0008 D).
            var stranger = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await stranger.RegisterAsync(strangerHandle, PostRoutes.Password)).StatusCode);
            var refused = await PostRoutes.SendAsync(
                stranger, HttpMethod.Post, "/api/admin/backup/run", null, stranger.Csrf);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("policy_denied", await refused.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Forbidden, (await PostRoutes.SendAsync(
                stranger, HttpMethod.Get, "/api/admin/backup/status")).StatusCode);

            // The operator triggers an immediate backup and gets the registered envelope back.
            var operatorClient = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await operatorClient.RegisterAsync(operatorHandle, PostRoutes.Password)).StatusCode);
            var run = await PostRoutes.SendAsync(
                operatorClient, HttpMethod.Post, "/api/admin/backup/run", null, operatorClient.Csrf);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
            using (var envelope = JsonDocument.Parse(await run.Content.ReadAsStringAsync()))
            {
                Assert.Equal("Verified", envelope.RootElement.GetProperty("status").GetString());
                Assert.False(string.IsNullOrEmpty(envelope.RootElement.GetProperty("id").GetString()));
                Assert.StartsWith("sha256:", envelope.RootElement.GetProperty("contentHash").GetString(),
                    StringComparison.Ordinal);
            }

            // The envelope really is in the registry the status reads.
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(storage,
                "SELECT COUNT(*) FROM backup_envelope"));

            // AC-04: every field contract section 6 fixes, under its own name.
            var status = await PostRoutes.SendAsync(operatorClient, HttpMethod.Get, "/api/admin/backup/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            using (var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
            {
                var backup = document.RootElement.GetProperty("backup");
                Assert.False(string.IsNullOrEmpty(backup.GetProperty("last_success_at").GetString()));
                Assert.Equal(1, backup.GetProperty("envelopes_count").GetInt64());
                Assert.True(backup.GetProperty("bytes_total").GetInt64() > 0);
                Assert.Equal(0, backup.GetProperty("verify_failures").GetInt64());

                var restore = document.RootElement.GetProperty("restore");
                Assert.Equal(JsonValueKind.Null, restore.GetProperty("last_drill_at").ValueKind);
                Assert.Equal(JsonValueKind.Null, restore.GetProperty("last_drill_result").ValueKind);
            }

            // A fresh last success raises nothing (the positive control of the stale alert).
            Assert.DoesNotContain(alerts.Lines,
                line => line.Contains(BackupAlerter.StaleAlert, StringComparison.Ordinal));

            // AC-05: push the verified row past 24 h and the status says it out loud.
            var aged = DateTimeOffset.UtcNow.AddHours(-25).ToString("O");
            await ExecAsync(storage,
                "UPDATE backup_envelope SET verified_at = @Aged", new { Aged = aged });
            var stale = await PostRoutes.SendAsync(operatorClient, HttpMethod.Get, "/api/admin/backup/status");
            Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
            using (var document = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
            {
                Assert.Equal(aged, document.RootElement.GetProperty("backup")
                    .GetProperty("last_success_at").GetString());
            }

            Assert.Contains(alerts.Lines,
                line => line.Contains(BackupAlerter.StaleAlert, StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(storage, volume);
        }
    }

    [Fact] // AC-05 (verify half)
    public async Task Ac05_An_unverified_envelope_counts_and_alerts_immediately()
    {
        var operatorHandle = PostRoutes.Handle("bkveri");
        var storage = NewDb();
        var volume = NewVolume();
        var alerts = new CapturingLogger<BackupAlerter>();
        try
        {
            // The re-read comes back tampered: the run registers a Corrupt envelope with a NULL
            // verified_at, which is exactly what section 6 counts as a verify failure.
            await using var host = await StartAsync(
                storage, volume, operatorHandle, alerts,
                builder => builder.Services.AddSingleton<IBackupTarget>(
                    _ => new FlippingTarget(new LocalDiskTarget(volume))));

            var operatorClient = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await operatorClient.RegisterAsync(operatorHandle, PostRoutes.Password)).StatusCode);
            var run = await PostRoutes.SendAsync(
                operatorClient, HttpMethod.Post, "/api/admin/backup/run", null, operatorClient.Csrf);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
            using (var envelope = JsonDocument.Parse(await run.Content.ReadAsStringAsync()))
            {
                Assert.Equal("Corrupt", envelope.RootElement.GetProperty("status").GetString());
                Assert.Equal(JsonValueKind.Null,
                    envelope.RootElement.GetProperty("verifiedAt").ValueKind);
            }

            var status = await PostRoutes.SendAsync(operatorClient, HttpMethod.Get, "/api/admin/backup/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            using (var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
            {
                Assert.Equal(1, document.RootElement.GetProperty("backup")
                    .GetProperty("verify_failures").GetInt64());
            }

            Assert.Contains(alerts.Lines,
                line => line.Contains(BackupAlerter.VerifyAlert, StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(storage, volume);
        }
    }

    [Fact] // AC-06
    public async Task Ac06_A_down_destination_answers_a_fixed_error_and_the_service_keeps_serving()
    {
        var operatorHandle = PostRoutes.Handle("bkdown");
        var storage = NewDb();
        var volume = NewVolume();
        try
        {
            await using var host = await StartAsync(
                storage, volume, operatorHandle, null,
                builder => builder.Services.AddSingleton<IBackupTarget>(_ => new BrokenTarget()));

            var operatorClient = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await operatorClient.RegisterAsync(operatorHandle, PostRoutes.Password)).StatusCode);

            var run = await PostRoutes.SendAsync(
                operatorClient, HttpMethod.Post, "/api/admin/backup/run", null, operatorClient.Csrf);
            Assert.Equal(HttpStatusCode.InternalServerError, run.StatusCode);
            var body = await run.Content.ReadAsStringAsync();
            Assert.Contains("backup_failed", body, StringComparison.Ordinal);

            // FR-053: the failure never leaks the internal path the target complained about.
            Assert.DoesNotContain(volume, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);

            // The process is still serving, and nothing was registered by the failed run.
            var health = await operatorClient.SendAsync(HttpMethod.Get, "/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            var ready = await operatorClient.SendAsync(HttpMethod.Get, "/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(storage,
                "SELECT COUNT(*) FROM backup_envelope"));
        }
        finally
        {
            Cleanup(storage, volume);
        }
    }

    private static async Task<BackupHost> StartAsync(
        string storage, string volume, string operatorHandle,
        CapturingLogger<BackupAlerter>? alerts, Action<Microsoft.AspNetCore.Builder.WebApplicationBuilder>? configure = null)
    {
        var recoveryKey = Convert.ToBase64String(RootKey.Create().AsSpan());
        return await BackupHost.StartAsync(
            [
                "--Storage:Path=" + storage,
                "--Logging:LogLevel:Default=Warning",
                "--Instance:Operators=" + operatorHandle,
                "--Backup:Local:Path=" + volume,
                "--Backup:IntervalMinutes=0", // the scheduler is exercised by its own suite
                "--Squid:RecoveryKey=" + recoveryKey,
            ],
            builder =>
            {
                configure?.Invoke(builder);
                if (alerts is not null)
                {
                    builder.Services.AddSingleton<ILogger<BackupAlerter>>(alerts);
                }
            });
    }

    private static async Task ExecAsync(string storage, string sql, object param)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            ProjectionDb.ConnectionOf(storage));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var property in param.GetType().GetProperties())
        {
            command.Parameters.AddWithValue("@" + property.Name, property.GetValue(param)!);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"squid-backup-route-{Guid.NewGuid():N}.db");

    private static string NewVolume() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"squid-backup-route-{Guid.NewGuid():N}")).FullName;

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
