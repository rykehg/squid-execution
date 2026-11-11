using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Domain.Containers;
using Squid.ReadModels.Projections;

namespace Squid.Api.Posts;

/// <summary>
/// SPEC-0009 section C — <c>GET /api/containers/{slug}/feed?since=</c> and
/// <c>GET /api/timeline?since=</c> served from the projections of data-model §4, never from the
/// aggregate (R-18: reads stay off the write path). Both pages are chronological (newest first)
/// and a <c>since</c> cursor answers the exact delta of contract §9.3 — strictly above the
/// cursor, in seq order, no repeats, idempotent when repeated (FR-023/FR-025). The cursor speaks
/// the global Activity.Seq (GAP-23) both projection tables are indexed by, so feed and timeline
/// and the future stream share one sequence space. Every read applies CURRENT state (FR-026):
/// the feed walks the very visibility ladder of <see cref="PostEndpoints"/> (its byte-identical
/// 404, with the trail row the refusal owes), and a timeline row survives only while the reader's
/// membership in its container is Active right now and that container still passes the same
/// ladder. <c>Unlisted</c> here keeps the P-03 reading: non-public needs a membership (task gap
/// row), so its items can never reach a third party's timeline.
/// </summary>
public static class FeedEndpoints
{
    private const int DefaultLimit = 20;

    private const int MaxLimit = 100;

    /// <summary>Maps the two read routes; after auth, so the session middleware already guards the timeline.</summary>
    public static WebApplication MapSquidFeed(this WebApplication app)
    {
        app.MapGet("/api/containers/{slug}/feed", FeedAsync);
        app.MapGet("/api/timeline", TimelineAsync);
        return app;
    }

    /// <summary>
    /// <c>GET /api/containers/{slug}/feed?since=</c> (contract §5/§9.3): payload first — a cursor
    /// outside the range answers an empty page, a malformed one the §8 400 — then the visibility
    /// ladder (404 byte-identical for an absent slug and a hidden one, AC-02/AC-04/AC-05), and
    /// only then the window over <c>timeline_item</c>.
    /// </summary>
    private static async Task<IResult> FeedAsync(
        string slug, HttpContext context, IContainerStore store, IPolicyAuditStore audit,
        SqliteFeedTimeline projections)
    {
        var refusal = Cursor(context, out var since, out var limit);
        if (refusal is not null)
        {
            return refusal;
        }

        var state = await store.FindAsync(slug);
        var container = await PostEndpoints.VisibleAsync(
            state?.Container, $"{PostEndpoints.Origin(context)}/c/{slug}",
            context, store, audit, forRead: true);
        if (container is null)
        {
            return ContainerEndpoints.Hidden(context);
        }

        return Answer(await projections.ReadContainerAsync(container.Id, since, limit), since, limit);
    }

    /// <summary>
    /// <c>GET /api/timeline?since=</c> (contract §5, AC-03): the reader's own
    /// <c>home_timeline</c>, same page and delta rule as the feed, gated on membership and
    /// visibility as they stand at this very request. The window is walked until
    /// <c>limit</c> rows SURVIVE that gate — a page whose raw rows all belong to
    /// containers the reader has just left must still make progress instead of handing the
    /// client an empty answer under a cursor it would repeat forever.
    /// </summary>
    private static async Task<IResult> TimelineAsync(
        HttpContext context, IContainerStore store, SqliteFeedTimeline projections)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var refusal = Cursor(context, out var since, out var limit);
        if (refusal is not null)
        {
            return refusal;
        }

        var actorId = ContainerEndpoints.ActorId(context, current.Handle);
        var kept = new List<FeedTimelineRow>();
        var reachable = new Dictionary<string, bool>(StringComparer.Ordinal);
        long? afterSeq = since, beforeSeq = null;
        while (kept.Count < limit)
        {
            var batch = await projections.ReadActorAsync(actorId, afterSeq, beforeSeq, limit);
            if (batch.Count == 0) break;
            foreach (var row in batch)
            {
                if (kept.Count == limit) break;
                if (await ReachableAsync(current.Handle, row, context, store, reachable))
                {
                    kept.Add(row);
                }
            }

            if (batch.Count < limit) break; // the raw window itself is exhausted
            // Strictly one direction per walk: the delta climbs above the cursor, the first page
            // walks down from the newest row, and both always move — every row of a full batch
            // sits on one side of the cursor it replaces.
            (afterSeq, beforeSeq) = since is not null
                ? ((long?)batch[^1].Seq, (long?)null)
                : ((long?)null, (long?)batch[0].Seq);
        }

        return Answer(kept, since, limit);
    }

    /// <summary>
    /// The current-state gate of one timeline row (AC-03, FR-026): the reader's membership in the
    /// row's container must be Active RIGHT NOW — stricter than visibility alone, which a public
    /// container passes with no membership at all — and the container must be alive and pass
    /// <see cref="PostEndpoints.CanSeeAsync"/>, the rule the P-03 routes already read with (the
    /// task's Gaps row: reuse it, never re-derive it). One lookup per container per request.
    /// </summary>
    private static async Task<bool> ReachableAsync(
        string handle, FeedTimelineRow row, HttpContext context, IContainerStore store,
        Dictionary<string, bool> cache)
    {
        var slug = SqliteFeedTimeline.SlugOf(row.ContainerId);
        if (cache.TryGetValue(slug, out var reachable))
        {
            return reachable;
        }

        var container = (await store.FindAsync(slug))?.Container;
        var membership = container is null
            ? null
            : await store.FindMembershipAsync(slug, handle);

        reachable = container is not null
            && container.DeletedAt is null
            && membership?.State == MembershipState.Active
            && await PostEndpoints.CanSeeAsync(container, context, store, forRead: true);
        cache[slug] = reachable;
        return reachable;
    }

    /// <summary>
    /// Contract §1 pagination: <c>since</c> is a non-negative seq (outside the data it answers an
    /// empty page, never an error), <c>limit</c> is 1..100 with the default 20 of the same §1.
    /// </summary>
    private static IResult? Cursor(HttpContext context, out long? since, out int limit)
    {
        since = null;
        limit = DefaultLimit;

        var after = context.Request.Query["since"].ToString();
        if (after.Length > 0)
        {
            if (!long.TryParse(after, out var value) || value < 0)
            {
                return Invalid(context, "since inválido: informe um seq inteiro não negativo");
            }

            since = value;
        }

        var size = context.Request.Query["limit"].ToString();
        if (size.Length > 0)
        {
            if (!int.TryParse(size, out var value) || value < 1)
            {
                return Invalid(context, "limit inválido: informe um inteiro entre 1 e 100");
            }

            limit = Math.Min(value, MaxLimit);
        }

        return null;
    }

    /// <summary>
    /// The page body of contract §1: the items in seq order, the cursor the next delta takes
    /// (strictly above the newest row answered; an empty page keeps the caller's own cursor, or
    /// 0 when it had none), and whether a full page may still hide more rows — a trailing empty
    /// answer under the same cursor terminates the sync, never repeats a row (§9.3).
    /// </summary>
    private static IResult Answer(IReadOnlyList<FeedTimelineRow> rows, long? since, int limit) =>
        Results.Json(new
        {
            items = rows.Select(Item).ToList(),
            nextSince = rows.Count == 0 ? since ?? 0 : rows.Max(row => row.Seq),
            hasMore = rows.Count == limit,
        });

    /// <summary>One projection line as the wire sees it: both sequences, the summary, the frozen publish values.</summary>
    private static object Item(FeedTimelineRow row) => new
    {
        id = row.PostId,
        content = row.Content,
        author = row.Author,
        containerId = row.ContainerId,
        seq = row.Seq, // the global Activity.Seq the ?since= cursor speaks (GAP-23)
        objectSeq = row.ObjectSeq, // the per-container Object.Seq of data-model §2.5
        depth = row.Depth,
        replyToId = row.ReplyToId,
        likeCount = row.LikeCount,
        replyCount = row.ReplyCount,
        shareCount = row.ShareCount,
        visibilityAtPublish = row.Visibility,
        policyVersion = row.PolicyVersion,
        createdAt = row.CreatedAt,
    };

    private static IResult Invalid(HttpContext context, string message) =>
        AuthErrors.Write(context, StatusCodes.Status400BadRequest, "validation_failed", message);
}
