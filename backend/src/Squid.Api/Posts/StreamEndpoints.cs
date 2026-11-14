using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.ReadModels.Projections;

namespace Squid.Api.Posts;

/// <summary>
/// SPEC-0009 section D — <c>GET /api/stream</c>, the live channel of FR-024 and R-16: session
/// first (the 401 of contract §8), then <c>text/event-stream</c> frames rendered by
/// <see cref="StreamSession"/> from the hub's wakes. The cursor comes from the <c>Last-Event-ID</c>
/// header the client echoes back (contract §9.4), so a reconnect delivers only what the client
/// has not seen — absent or malformed, from the start. Each frame is gated by CURRENT visibility
/// (FR-026) through the very read rule the feed routes read with: membership and policy are
/// evaluated when the frame is delivered, never frozen at publish time.
/// </summary>
public static class StreamEndpoints
{
    /// <summary>The contract §5 heartbeat cadence when <c>Stream:PingSeconds</c> says nothing.</summary>
    public const double DefaultPingSeconds = 15;

    private const string PingSecondsKey = "Stream:PingSeconds";

    /// <summary>Maps the SSE route; after auth, so the session middleware already guards it.</summary>
    public static WebApplication MapSquidStream(this WebApplication app)
    {
        app.MapGet("/api/stream", StreamAsync);
        return app;
    }

    /// <summary>
    /// <c>GET /api/stream</c>: 401 without a session, otherwise the stream itself — headers with
    /// buffering off so a frame leaves the moment it is written (the &lt; 1 s of the DoD), then
    /// frames until the client hangs up. A hang-up is this route's NORMAL exit, never a fault:
    /// the finally unregisters the subscriber so a dead connection stops receiving wakes.
    /// </summary>
    private static async Task StreamAsync(
        HttpContext context, StreamHub hub, IActivityStore activities, IContainerStore store,
        IConfiguration configuration, TimeProvider time)
    {
        if (AuthSessions.Current(context) is null)
        {
            await AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.").ExecuteAsync(context);
            return;
        }

        var subscription = hub.Subscribe();
        try
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            // Headers out now: a subscriber's handshake must not wait for the first frame (or the
            // first heartbeat) to learn the stream opened.
            await context.Response.Body.FlushAsync(context.RequestAborted);

            var session = new StreamSession(activities, subscription,
                activity => DeliverableAsync(activity, context, store),
                PingInterval(configuration), time);

            await foreach (var frame in session.ReadAsync(LastEventId(context), context.RequestAborted))
            {
                var bytes = Encoding.UTF8.GetBytes(frame);
                await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (Exception error) when (context.RequestAborted.IsCancellationRequested
            || error is OperationCanceledException or IOException)
        {
            // The client hung up mid-wait or mid-frame: an interrupted stream is a disconnect,
            // not a defect — anything else (the log failing while the client is still there) escapes.
        }
        finally
        {
            subscription.Dispose();
        }
    }

    /// <summary>
    /// The delivery-time gate of AC-04 (FR-026): the event's container — resolved from the
    /// summary payload's canonical id — must be alive and pass
    /// <see cref="PostEndpoints.CanSeeAsync"/>, the rule the P-03 routes read with, in its read
    /// half: current policy, current membership, <c>Banned</c> excluded. The gate reads CURRENT
    /// state and never the row's frozen <c>visibilityAtPublish</c> — QA-0026's AC-04b locks that
    /// a post which became visible while the subscriber was away IS delivered on replay. An event
    /// whose container cannot be resolved is never delivered: what visibility cannot confirm, the
    /// stream does not send. No trail row per frame — the trail records route decisions, not
    /// stream fan-out; the policy itself is re-read here on every delivery.
    /// </summary>
    private static async Task<bool> DeliverableAsync(
        ActivityEvent activity, HttpContext context, IContainerStore store)
    {
        using var summary = JsonDocument.Parse(activity.Payload);
        if (!summary.RootElement.TryGetProperty("containerId", out var property))
        {
            return false;
        }

        var containerId = property.GetString();
        if (string.IsNullOrEmpty(containerId) || !containerId.Contains('/'))
        {
            return false;
        }

        var container = (await store.FindAsync(SqliteFeedTimeline.SlugOf(containerId)))?.Container;
        return container is not null
            && container.DeletedAt is null
            && await PostEndpoints.CanSeeAsync(container, context, store, forRead: true);
    }

    /// <summary>
    /// The reconnect cursor of contract §9.4: the <c>Last-Event-ID</c> the client echoes back.
    /// Absent, malformed or negative answers 0 — a cursor that cannot be trusted delivers from the
    /// start instead of inventing a position (§9.4 only constrains a real id).
    /// </summary>
    public static long LastEventId(HttpContext context)
    {
        var header = context.Request.Headers["Last-Event-ID"].ToString();
        return long.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq)
            && seq >= 0
            ? seq
            : 0;
    }

    /// <summary>
    /// <c>Stream:PingSeconds</c>, default <see cref="DefaultPingSeconds"/> (SPEC-0009 section D
    /// and its gap row): a fractional value is accepted so a test can watch the heartbeat without
    /// sleeping 15 seconds; anything unusable falls back to the contract cadence.
    /// </summary>
    public static TimeSpan PingInterval(IConfiguration configuration) =>
        double.TryParse(configuration[PingSecondsKey], NumberStyles.Float, CultureInfo.InvariantCulture,
            out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultPingSeconds);
}
