using System.Net;
using System.Text.Json;
using Squid.Tests.Auth;

namespace Squid.Tests.Posts;

/// <summary>
/// AC-03 of TASK-0023: the per-container <c>Object.Seq</c> stays unique and growing when two
/// publishes race the same container (the ETag compares the stored version, so one writer wins
/// and the other takes the next number), and a different container's counter is untouched —
/// every number comes from its own row.
/// </summary>
[Collection("ServiceHost")]
public sealed class PostSeqTests : IClassFixture<AuthHostFixture>
{
    private readonly AuthHostFixture _host;

    public PostSeqTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-03
    public async Task Concurrent_publishes_take_unique_growing_seq_and_other_containers_never_interfere()
    {
        var (owner, _) = await PostRoutes.SignedInAsync(_host, "ac03dono");
        var slugA = PostRoutes.Slug("concorrente-a");
        var slugB = PostRoutes.Slug("concorrente-b");
        foreach (var slug in new[] { slugA, slugB })
        {
            Assert.Equal(HttpStatusCode.Created,
                (await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/containers",
                    new { kind = "community", name = slug, slug }, owner.Csrf)).StatusCode);
        }

        // Two authors, both active members of A — entry is open on the community preset.
        var (one, _) = await PostRoutes.SignedInAsync(_host, "ac03um");
        var (two, _) = await PostRoutes.SignedInAsync(_host, "ac03dois");
        foreach (var client in new[] { one, two })
        {
            var joined = await PostRoutes.SendAsync(client, HttpMethod.Post,
                $"/api/containers/{slugA}/join", null, client.Csrf);
            Assert.True(joined.IsSuccessStatusCode, $"join failed: {joined.StatusCode}");
        }

        // The race: two publishes of the same container in flight together (AC-03).
        var first = PostRoutes.SendAsync(one, HttpMethod.Post,
            $"/api/containers/{slugA}/posts", new { content = "primeira" }, one.Csrf);
        var second = PostRoutes.SendAsync(two, HttpMethod.Post,
            $"/api/containers/{slugA}/posts", new { content = "segunda" }, two.Csrf);
        var responses = await Task.WhenAll(first, second);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

        var seqs = new List<long>();
        foreach (var response in responses)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            seqs.Add(json.RootElement.GetProperty("seq").GetInt64());
        }

        // Unique, and exactly the container's first two numbers: the race serialized them.
        Assert.Equal(new long[] { 1, 2 }, seqs.OrderBy(seq => seq).ToArray());

        // After the race the counter keeps growing from where it stopped.
        var third = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slugA}/posts", new { content = "terceira" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
        using var thirdJson = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        Assert.Equal(3, thirdJson.RootElement.GetProperty("seq").GetInt64());

        // A different container starts its own count at 1: the counters never touch (AC-03).
        var inB = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/containers/{slugB}/posts", new { content = "do outro" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, inB.StatusCode);
        using var inBJson = JsonDocument.Parse(await inB.Content.ReadAsStringAsync());
        Assert.Equal(1, inBJson.RootElement.GetProperty("seq").GetInt64());
    }
}
