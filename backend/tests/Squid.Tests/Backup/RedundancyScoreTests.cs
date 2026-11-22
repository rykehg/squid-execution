using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Admin;
using Squid.Api.Crypto;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Squid.Tests.Projections;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-01..AC-05 of TASK-0060 (SPEC-0012 section G, opção A): the redundancy score behind its own
/// port — computed from the registry rows, never from a destination header (S-47) — in the exact
/// doc 05 §8 format; the thin HTML page <c>/admin/backup</c> renders over the same state with no
/// asset and no package; the operator gate both routes share; and the score moving 1/2 → 2/2 the
/// moment the off-site destination really holds the newest verified envelope. Plus S-52: a
/// <c>Backup:S3:*</c> key that is set but unusable is said out loud at boot.
/// </summary>
[Collection("ServiceHost")] // every fact here builds a host; hosts never overlap
public sealed class RedundancyScoreTests
{
    private const string AccessKey = "AKIA0TASK0060S3B";

    private const string SecretKey = "task-0060-redundancy-secret";

    [Fact] // AC-01 (local-only spelling), AC-02, AC-03, AC-04
    public async Task Ac01_ac02_ac03_ac04_The_status_and_the_thin_page_publish_the_state_behind_the_operator_gate()
    {
        var operatorHandle = PostRoutes.Handle("redoper");
        var strangerHandle = PostRoutes.Handle("redstran");
        var storage = NewDb();
        var volume = NewVolume();
        try
        {
            await using var host = await StartAsync(storage, volume, operatorHandle);

            // AC-03: both routes, both refusals — no session 401, a session outside
            // Instance:Operators 403 policy_denied (the operator rule since P-03, verbatim).
            var anonymous = host.NewClient();
            var anonymousStatus = await anonymous.SendAsync(HttpMethod.Get, "/api/admin/backup/status");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatus.StatusCode);
            Assert.Contains("not_authenticated", await anonymousStatus.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            var anonymousPage = await anonymous.SendAsync(HttpMethod.Get, "/admin/backup");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousPage.StatusCode);
            Assert.Contains("not_authenticated", await anonymousPage.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            var stranger = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await stranger.RegisterAsync(strangerHandle, PostRoutes.Password)).StatusCode);
            var refusedPage = await PostRoutes.SendAsync(stranger, HttpMethod.Get, "/admin/backup");
            Assert.Equal(HttpStatusCode.Forbidden, refusedPage.StatusCode);
            Assert.Contains("policy_denied", await refusedPage.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Forbidden, (await PostRoutes.SendAsync(
                stranger, HttpMethod.Get, "/api/admin/backup/status")).StatusCode);

            var client = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await client.RegisterAsync(operatorHandle, PostRoutes.Password)).StatusCode);
            var run = await PostRoutes.SendAsync(
                client, HttpMethod.Post, "/api/admin/backup/run", null, client.Csrf);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);

            // AC-01: the score of doc 05 §8 on a local-only host — one configured destination,
            // holding the newest verified envelope, and no off-site one to recover a disaster with.
            var status = await PostRoutes.SendAsync(client, HttpMethod.Get, "/api/admin/backup/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            string score;
            string lastSuccessAt;
            long bytesTotal;
            using (var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
            {
                var backup = document.RootElement.GetProperty("backup");
                score = document.RootElement.GetProperty("redundancy").GetProperty("score").GetString()!;
                Assert.Equal("Redundância: 1/1 — sem recuperação de desastre", score);
                lastSuccessAt = backup.GetProperty("last_success_at").GetString()!;
                bytesTotal = backup.GetProperty("bytes_total").GetInt64();
                Assert.Equal(1, backup.GetProperty("envelopes_count").GetInt64());
                Assert.Equal(0, backup.GetProperty("verify_failures").GetInt64());
            }

            // AC-04: the page is this backend's own answer — one inline document, text/html, no
            // asset tag and no URL to fetch (the "no front build, no package" half is the diff:
            // no .csproj, no wwwroot, nothing but the touched sources).
            var page = await PostRoutes.SendAsync(client, HttpMethod.Get, "/admin/backup");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
            var html = await page.Content.ReadAsStringAsync();
            Assert.StartsWith("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", html, StringComparison.OrdinalIgnoreCase);

            // AC-02: every field contract section 6 publishes, with the values of THIS state —
            // the instant byte-identical to the JSON route's, so the two surfaces cannot drift.
            Assert.Contains(lastSuccessAt, html, StringComparison.Ordinal);
            Assert.Contains("backup.envelopes_count", html, StringComparison.Ordinal);
            Assert.Contains("<dd>1</dd>", html, StringComparison.Ordinal);
            Assert.Contains("backup.bytes_total", html, StringComparison.Ordinal);
            Assert.Contains($"<dd>{bytesTotal}</dd>", html, StringComparison.Ordinal);
            Assert.Contains("backup.verify_failures", html, StringComparison.Ordinal);
            Assert.Contains("<dd>0</dd>", html, StringComparison.Ordinal);
            Assert.Contains("redundancy.score", html, StringComparison.Ordinal);
            Assert.Contains(score, html, StringComparison.Ordinal);

            // The drill fields are read, never hardcoded: null answers the placeholder the
            // section 6 ledger has not filled yet.
            Assert.Contains("restore.last_drill_at", html, StringComparison.Ordinal);
            Assert.Contains("restore.last_drill_result", html, StringComparison.Ordinal);
            Assert.Contains("<dd>—</dd>", html, StringComparison.Ordinal);

            // Recording a drill changes the page — the display follows the ledger of TASK-0056.
            var ledger = host.App.Services.GetRequiredService<SqliteRestoreDrillLedger>();
            await ledger.RecordAsync("restore-roundtrip", "passed");
            var refreshed = await PostRoutes.SendAsync(client, HttpMethod.Get, "/admin/backup");
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            var afterDrill = await refreshed.Content.ReadAsStringAsync();
            Assert.Contains("restore-roundtrip: passed", afterDrill, StringComparison.Ordinal);
            Assert.DoesNotContain("<dd>—</dd>", afterDrill, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(storage, volume);
        }
    }

    [Fact] // AC-05 (and AC-01's 1/2 spelling) end to end: the off-site destination comes back
    public async Task Ac05_The_score_moves_from_1_2_to_2_2_when_the_off_site_holds_the_newest_verified_envelope()
    {
        var handle = PostRoutes.Handle("redoff");
        var storage = NewDb();
        var volume = NewVolume();
        await using var s3 = await FakeS3.StartAsync("squid-backup", AccessKey, SecretKey);
        s3.FailNext(StatusCodes.Status500InternalServerError, times: int.MaxValue);
        try
        {
            var recoveryKey = Convert.ToBase64String(RootKey.Create().AsSpan());
            await using var host = await BackupHost.StartAsync(
            [
                "--Storage:Path=" + storage,
                "--Logging:LogLevel:Default=Warning",
                "--Instance:Operators=" + handle,
                "--Backup:Local:Path=" + volume,
                "--Backup:IntervalMinutes=0", // the scheduler has no business in this fact
                "--Squid:RecoveryKey=" + recoveryKey,
                "--Backup:S3:Endpoint=" + s3.Endpoint,
                "--Backup:S3:Region=" + s3.Options.Region,
                "--Backup:S3:Bucket=" + s3.Bucket,
                "--Backup:S3:AccessKey=" + AccessKey,
                "--Backup:S3:SecretKey=" + SecretKey,
            ]);

            var client = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await client.RegisterAsync(handle, PostRoutes.Password)).StatusCode);

            // The off-site destination is configured but answering 500: the run degrades
            // (TASK-0057) and the score counts ONE of TWO — with the disaster-recovery suffix,
            // exactly the spelling AC-01 fixes.
            var first = await PostRoutes.SendAsync(
                client, HttpMethod.Post, "/api/admin/backup/run", null, client.Csrf);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(
                "Redundância: 1/2 — sem recuperação de desastre", await ScoreAsync(client));

            // The destination heals and the NEXT run is verified there: 2/2, suffix gone — the
            // score moved because the registry gained a verified row, not because a header said so.
            s3.Heal();
            var second = await PostRoutes.SendAsync(
                client, HttpMethod.Post, "/api/admin/backup/run", null, client.Csrf);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal("Redundância: 2/2", await ScoreAsync(client));

            // The page shows the same score the endpoint answers (one formatter, two surfaces).
            var page = await PostRoutes.SendAsync(client, HttpMethod.Get, "/admin/backup");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("Redundância: 2/2", html, StringComparison.Ordinal);
            Assert.DoesNotContain("sem recuperação de desastre", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(storage, volume);
        }
    }

    [Fact] // AC-01: the format itself, doc 05 §8 / contract §6
    public void Ac01_The_score_format_is_the_one_doc_05_section_8_fixes()
    {
        Assert.Equal(
            "Redundância: 1/2 — sem recuperação de desastre",
            new BackupRedundancy(1, 2, false).Score);
        Assert.Equal("Redundância: 2/2", new BackupRedundancy(2, 2, true).Score);
        // Local-only: no off-site destination exists, so nothing recovers a disaster — the
        // suffix says so (SPEC-0012 section G: it hangs on the off-site half, never on "all").
        Assert.Equal(
            "Redundância: 1/1 — sem recuperação de desastre",
            new BackupRedundancy(1, 1, false).Score);
        Assert.Equal(
            "Redundância: 0/2 — sem recuperação de desastre",
            new BackupRedundancy(0, 2, false).Score);
    }

    [Fact] // AC-01 + AC-05 at the source: the score follows the registry rows, window by window
    public async Task The_score_counts_the_destinations_holding_the_newest_verified_envelope()
    {
        var db = NewDb();
        try
        {
            await MigrateAsync(db);
            var source = new SqliteBackupRedundancySource(
                ProjectionDb.ConnectionOf(db), "local-disk", "s3-offsite");
            var at = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

            async Task<string> CurrentScoreAsync() =>
                (await source.ReadAsync(CancellationToken.None)).Score;

            // Nothing verified anywhere: zero of two — the honest number, never a guessed 1/1.
            Assert.Equal("Redundância: 0/2 — sem recuperação de desastre", await CurrentScoreAsync());

            // Run A: both destinations hold the envelope verified → 2/2, no suffix.
            await InsertAsync(db, "bk-a-local", 0, 0, "local-disk", true, at.AddSeconds(1));
            await InsertAsync(db, "bk-a-s3", 0, 0, "s3-offsite", true, at.AddSeconds(2));
            Assert.Equal("Redundância: 2/2", await CurrentScoreAsync());

            // Run B: the window moved (seq 1..0) and both verified again.
            await InsertAsync(db, "bk-b-local", 1, 0, "local-disk", true, at.AddSeconds(3));
            await InsertAsync(db, "bk-b-s3", 1, 0, "s3-offsite", true, at.AddSeconds(4));
            Assert.Equal("Redundância: 2/2", await CurrentScoreAsync());

            // Run C collided with run B's window — exactly what happens when seqTo does not
            // advance (no new activity) — and its off-site attempt failed. The stale verified
            // row of run B must NOT claim the newest envelope: a naive "any verified row in the
            // window" would answer 2/2 here and hide the very gap the suffix exists for.
            await InsertAsync(db, "bk-c-local", 1, 0, "local-disk", true, at.AddSeconds(5));
            await InsertAsync(db, "bk-c-s3", 1, 0, "s3-offsite", false, at.AddSeconds(6));
            Assert.Equal("Redundância: 1/2 — sem recuperação de desastre", await CurrentScoreAsync());

            // Run D delivers to both again: 2/2 and the suffix is gone (AC-05's move).
            await InsertAsync(db, "bk-d-local", 1, 0, "local-disk", true, at.AddSeconds(7));
            await InsertAsync(db, "bk-d-s3", 1, 0, "s3-offsite", true, at.AddSeconds(8));
            Assert.Equal("Redundância: 2/2", await CurrentScoreAsync());

            // The denominator is the CONFIGURED destination pair, not the rows: the same registry
            // read by a local-only host is 1/1 — the off-site row exists but is not configured.
            var localOnly = new SqliteBackupRedundancySource(
                ProjectionDb.ConnectionOf(db), "local-disk", offsiteTargetId: null);
            Assert.Equal(
                "Redundância: 1/1 — sem recuperação de desastre",
                (await localOnly.ReadAsync(CancellationToken.None)).Score);
        }
        finally
        {
            ProjectionDb.TryDelete(db);
        }
    }

    [Fact] // S-52: a Backup:S3:* key set but unusable is said out loud instead of silently off
    public async Task S52_A_partial_off_site_configuration_warns_at_boot_and_the_default_stays_silent()
    {
        var partial = NewDb();
        var quiet = NewDb();
        var console = Console.Out;
        try
        {
            var output = new StringWriter();
            Console.SetOut(output); // the Serilog console sink writes to the current Console.Out
            try
            {
                await using (SquidServiceHost.Build(
                [
                    "--Storage:Path=" + partial,
                    "--Logging:LogLevel:Default=Warning",
                    "--Backup:S3:Endpoint=http://127.0.0.1:1",
                    "--Backup:S3:Bucket=squid-backup",
                    // no credential: configured but unusable — the S-52 typo/partial case
                ]))
                {
                }
            }
            finally
            {
                Console.SetOut(console);
            }

            var warned = output.ToString();
            Assert.Contains("off-site backup disabled", warned, StringComparison.Ordinal);
            Assert.Contains("Backup:S3", warned, StringComparison.Ordinal);

            // The plain local-only default never names S3: no warning, byte-identical boot log.
            var silent = new StringWriter();
            Console.SetOut(silent);
            try
            {
                await using (SquidServiceHost.Build(
                [
                    "--Storage:Path=" + quiet,
                    "--Logging:LogLevel:Default=Warning",
                ]))
                {
                }
            }
            finally
            {
                Console.SetOut(console);
            }

            Assert.DoesNotContain(
                "off-site backup disabled", silent.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(console);
            ProjectionDb.TryDelete(partial);
            ProjectionDb.TryDelete(quiet);
        }
    }

    private static async Task<string> ScoreAsync(AuthClient client)
    {
        var status = await PostRoutes.SendAsync(client, HttpMethod.Get, "/api/admin/backup/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("redundancy").GetProperty("score").GetString()!;
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
            "--Backup:IntervalMinutes=0", // the scheduler has no business in these facts
            "--Squid:RecoveryKey=" + recoveryKey,
        ]);
    }

    private static async Task MigrateAsync(string db) =>
        await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(db), SchemaMigrations.Load());

    /// <summary>One registry row, exactly as the pipeline registers it (data-model §2.10).</summary>
    private static async Task InsertAsync(
        string db, string id, long seqFrom, long seqTo, string target, bool verified,
        DateTimeOffset at)
    {
        await using var connection = new SqliteConnection(ProjectionDb.ConnectionOf(db));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO backup_envelope (id, key_epoch, seq_from, seq_to, content_hash, "
            + "size_bytes, target_id, created_at, verified_at) "
            + "VALUES (@Id, 1, @From, @To, 'sha256:test', 42, @Target, @CreatedAt, @VerifiedAt)";
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@From", seqFrom);
        command.Parameters.AddWithValue("@To", seqTo);
        command.Parameters.AddWithValue("@Target", target);
        command.Parameters.AddWithValue("@CreatedAt", at.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "@VerifiedAt",
            verified ? (object)at.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"squid-redundancy-{Guid.NewGuid():N}.db");

    private static string NewVolume() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"squid-redundancy-{Guid.NewGuid():N}")).FullName;

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
