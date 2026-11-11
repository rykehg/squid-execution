using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.ReadModels.Projections;
using Squid.Tests.Auth;

namespace Squid.Tests.Feed;

/// <summary>
/// House helpers of the TASK-0025 suites: the projection step the Worker pump would do — the test
/// host runs no pump (R-18 keeps projection off the write path), so a scenario that needs feed
/// rows runs the very projector the composition registers — and the JSON readers every route
/// assertion needs.
/// </summary>
internal static class FeedHarness
{
    /// <summary>Projects every event past the projections' high-water: <c>ActivityProjectionPump</c>'s tick, by hand.</summary>
    public static async Task ProjectPendingAsync(AuthHostFixture host)
    {
        var projector = host.App.Services.GetRequiredService<IActivityProjector>();
        var log = host.App.Services.GetRequiredService<SqliteActivityLog>();
        var pending = await log.ReadAsync(await projector.LastProjectedSeqAsync());
        foreach (var entry in pending)
        {
            await projector.ProjectAsync(entry);
        }
    }

    /// <summary>The parsed body, cloned out of its document so the caller can hold it.</summary>
    public static async Task<JsonElement> JsonOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>One property of a view as text; a missing property fails the assertion, never defaults.</summary>
    public static string? Text(JsonElement element, string name) => element.GetProperty(name).GetString();

    /// <summary>The <c>items</c> array of a page body.</summary>
    public static JsonElement Items(JsonElement page) => page.GetProperty("items");

    /// <summary>The content of every item, in the order the page answered them.</summary>
    public static List<string> Contents(JsonElement items) =>
        items.EnumerateArray().Select(item => item.GetProperty("content").GetString()!).ToList();

    /// <summary>The <c>seq</c> of every item — the global Activity.Seq the cursor speaks (GAP-23).</summary>
    public static List<long> Seqs(JsonElement items) =>
        items.EnumerateArray().Select(item => item.GetProperty("seq").GetInt64()).ToList();

    /// <summary>The container each item belongs to, in page order.</summary>
    public static List<string> ContainerIds(JsonElement items) =>
        items.EnumerateArray().Select(item => item.GetProperty("containerId").GetString()!).ToList();

    /// <summary>One page answer as raw bytes — for the byte-identical 404 of SC-005.</summary>
    public static Task<byte[]> BytesOf(HttpResponseMessage response) => response.Content.ReadAsByteArrayAsync();
}
