using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Xunit.Abstractions;

namespace Squid.Tests.Load;

/// <summary>
/// AC-02 of TASK-0032 — SPEC-0009 SC-004: 10k items seeded straight into <c>timeline_item</c>
/// (and their pointers into <c>home_timeline</c>) as test setup — no publish loop, no network —
/// then BOTH first pages measured in-process through the real routes of section C: P95 under a
/// second on the dev machine (the gap row: big regressions go red, fine noise does not), with
/// P50/P95/P99 printed to the test log the way the QA PROBE lines are.
/// </summary>
[Collection("ServiceHost")]
public sealed class FeedLoadTests : IClassFixture<AuthHostFixture>
{
    private const int Items = 10_000;

    private const int Samples = 100;

    private readonly AuthHostFixture _host;

    private readonly ITestOutputHelper _output;

    public FeedLoadTests(AuthHostFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    [Fact] // AC-02 — SC-004
    public async Task Ac02_ten_thousand_items_first_pages_answer_p95_under_a_second_with_p50_p99_printed()
    {
        var (owner, handle) = await PostRoutes.SignedInAsync(_host, "carga");
        var slug = PostRoutes.Slug("carga");
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = slug, slug }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var origin = $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}";
        await SeedAsync($"{origin}/c/{slug}", $"{origin}/users/{handle}", handle);

        var feed = await MeasureAsync(
            () => PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}/feed"), "feed");
        var timeline = await MeasureAsync(
            () => PostRoutes.SendAsync(owner, HttpMethod.Get, "/api/timeline"), "timeline");

        Assert.True(feed.P95 < 1000,
            $"SC-004: feed first page P95 {feed.P95:F0} ms must stay under 1000 ms "
            + $"(n={Samples}, P50 {feed.P50:F0} ms, P99 {feed.P99:F0} ms)");
        Assert.True(timeline.P95 < 1000,
            $"SC-004: timeline first page P95 {timeline.P95:F0} ms must stay under 1000 ms "
            + $"(n={Samples}, P50 {timeline.P50:F0} ms, P99 {timeline.P99:F0} ms)");
    }

    /// <summary>
    /// One endpoint's first page, <see cref="Samples"/> times: every answer must be a full
    /// 20-item page (so the sample measures real work), and the nearest-rank percentiles of the
    /// round-trip are printed to the test log and asserted on P95 (SC-004).
    /// </summary>
    private async Task<Stats> MeasureAsync(Func<Task<HttpResponseMessage>> call, string name)
    {
        var samples = new List<double>(Samples);
        for (var round = 0; round < Samples; round++)
        {
            var watch = Stopwatch.StartNew();
            var response = await call();
            watch.Stop(); // the body is already buffered by SendAsync; the parse stays out of the sample

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(20, page.RootElement.GetProperty("items").GetArrayLength());

            samples.Add(watch.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var stats = new Stats(
            Percentile(samples, 50), Percentile(samples, 95), Percentile(samples, 99), samples[^1]);
        var line =
            $"SC-004 {name}: itens={Items} n={Samples} P50={stats.P50:F1} ms "
            + $"P95={stats.P95:F1} ms P99={stats.P99:F1} ms max={stats.Max:F1} ms";
        _output.WriteLine(line); // o P50/P99 do AC-02: sai no log detalhado do teste e nas mensagens de falha
        return stats;
    }

    /// <summary>
    /// The seed of the load test (AC-02): the two projection tables of data-model §4, written
    /// directly in one transaction — the setup a 10k publish loop would produce, without the
    /// loop, the pump or a single network hop.
    /// </summary>
    private async Task SeedAsync(string containerId, string actorId, string author)
    {
        var createdAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = new SqliteConnection($"Data Source={_host.StoragePath};Pooling=False");
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        await using (var timeline = connection.CreateCommand())
        {
            timeline.Transaction = transaction;
            timeline.CommandText = """
                INSERT INTO timeline_item
                    (container_id, seq, object_seq, post_id, author, content, depth,
                     like_count, reply_count, share_count, visibility, policy_version, created_at)
                VALUES
                    (@ContainerId, @Seq, @Seq, @PostId, @Author, @Content, 0, 0, 0, 0, 'public', 1, @CreatedAt)
                """;
            timeline.Parameters.AddWithValue("@ContainerId", containerId);
            var seq = timeline.Parameters.Add("@Seq", SqliteType.Integer);
            var postId = timeline.Parameters.Add("@PostId", SqliteType.Text);
            timeline.Parameters.AddWithValue("@Author", author);
            var content = timeline.Parameters.Add("@Content", SqliteType.Text);
            timeline.Parameters.AddWithValue("@CreatedAt", createdAt);

            for (var index = 1; index <= Items; index++)
            {
                seq.Value = index;
                postId.Value = $"carga-{index}";
                content.Value = $"item de carga {index}";
                await timeline.ExecuteNonQueryAsync();
            }
        }

        await using (var home = connection.CreateCommand())
        {
            home.Transaction = transaction;
            home.CommandText = """
                INSERT INTO home_timeline (actor_id, seq, container_id, post_id)
                VALUES (@ActorId, @Seq, @ContainerId, @PostId)
                """;
            home.Parameters.AddWithValue("@ActorId", actorId);
            home.Parameters.AddWithValue("@ContainerId", containerId);
            var seq = home.Parameters.Add("@Seq", SqliteType.Integer);
            var postId = home.Parameters.Add("@PostId", SqliteType.Text);

            for (var index = 1; index <= Items; index++)
            {
                seq.Value = index;
                postId.Value = $"carga-{index}";
                await home.ExecuteNonQueryAsync();
            }
        }

        await transaction.CommitAsync();

        // O "10k" do AC-02 é observável na origem: uma página cheia de 20 só prova que há pelo
        // menos 20 linhas — a contagem confere a carga que a medição supõe existir.
        Assert.Equal(Items, await CountAsync(connection, "timeline_item"));
        Assert.Equal(Items, await CountAsync(connection, "home_timeline"));
    }

    /// <summary>Row count of one seeded projection table — the 10k the AC demands, checked where it was written.</summary>
    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}"; // the table name comes from this file only
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Nearest-rank percentile over an ascending sample (SC-004's P50/P95/P99).</summary>
    private static double Percentile(List<double> sorted, double percentile) =>
        sorted[Math.Clamp((int)Math.Ceiling(percentile / 100 * sorted.Count) - 1, 0, sorted.Count - 1)];

    private sealed record Stats(double P50, double P95, double P99, double Max);
}
