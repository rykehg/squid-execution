using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Api.Discovery;
using Squid.Api.Posts;
using Squid.Domain;
using Squid.Domain.Containers;
using Squid.Domain.Objects;
using Squid.Grains.Actors;

namespace Squid.Api.Federation;

/// <summary>
/// The ActivityPub surface of contract activitypub §1 (SPEC-0011 section D, TASK-0043): the three
/// collections of an actor, the object, the container with its outbox and members, the nodeinfo
/// document, and the two inboxes — which, since TASK-0044, run the whole §3 order through
/// <c>IInboxPipeline</c> and answer its verdicts here: this class keeps the status codes, the
/// single refusal envelope and the media types, the pipeline keeps the verification and the
/// application. Three rules walk every read route, in this order: negotiate the <c>Accept</c> first
/// (AC-06), then the 404 ladder of FR-018/SC-005 — byte-identical for absent and invisible, and
/// every container decision recorded in the trail (FR-019) through <c>PostEndpoints.VisibleAsync</c>,
/// the rule the P-03/P-04 routes already read — then the answer. The document bytes themselves come
/// from Squid.Protocols through the seams the composition root hands in, exactly like the profile
/// document of <c>MapSquidDiscovery</c> (ADR-11: JSON-LD stays in the adapter, this assembly
/// references no protocol project).
/// </summary>
public static class FederationEndpoints
{
    /// <summary>Media type of every document here (contract §6).</summary>
    private const string ActivityJson = "application/activity+json";

    /// <summary>Page size of every collection — the default 20 of the HTTP contract §1.</summary>
    private const int PageSize = 20;

    /// <summary>The only event an outbox serves: publishes. A tombstone never reaches a collection.</summary>
    private const string Create = "Create";

    /// <summary>Maps the routes of contract activitypub §1; runs after <c>MapSquidDiscovery</c>.</summary>
    public static WebApplication MapSquidFederation(this WebApplication app, FederationSeams seams)
    {
        app.MapGet("/users/{name}/outbox", OutboxAsync);
        app.MapGet("/users/{name}/followers", FollowersAsync);
        app.MapGet("/users/{name}/following", FollowingAsync);
        // Both inboxes share one verdict mapper behind two routes: the target is what the ROUTE
        // said, never what a query parameter could stand in for (SPEC-0011 section E). The
        // signature gate runs BEFORE the recipient is even looked at, so an unknown actor or
        // container cannot be told apart from a signed one.
        app.MapPost("/users/{name}/inbox", (string name, HttpContext context, IInboxPipeline pipeline) =>
            InboxAsync(context, pipeline, InboxTarget.ForActor(name)));
        app.MapPost("/c/{slug}/inbox", (string slug, HttpContext context, IInboxPipeline pipeline) =>
            InboxAsync(context, pipeline, InboxTarget.ForContainer(slug)));
        // The object id arrives as sent — the tail of /o/{id} or the whole canonical URI, plain or
        // percent-encoded — the same capture rule /api/posts/{*id} walks (contract §1).
        app.MapGet("/objects/{*id}", ObjectAsync);
        app.MapGet("/c/{slug}", ContainerAsync);
        app.MapGet("/c/{slug}/outbox", ContainerOutboxAsync);
        app.MapGet("/c/{slug}/members", MembersAsync);
        app.MapGet("/.well-known/nodeinfo", NodeInfoAsync);
        return app;

        // ---- actor collections (contract §1) -----------------------------------------

        /// <summary>
        /// <c>GET /users/{name}/outbox</c> (AC-02): the actor's own publications, newest first,
        /// behind first/next. Only a <c>public</c> publication enters — contract §4 keeps
        /// unlisted, members-only and private out of every public collection.
        /// </summary>
        async Task<IResult> OutboxAsync(
            string name, HttpContext context, ActorGrain actors, IActivityStore activities,
            IObjectStore objects, IContainerStore store)
        {
            var refused = Negotiate(context);
            if (refused is not null) return refused;
            if (await KnownAsync(name, actors) is null) return Results.NotFound();

            var actorId = ActorId(context, name);
            return await AnswerAsync(
                context, seams, null, $"{ActorId(context, name)}/outbox",
                () => PublicPostsAsync(activities, objects, store, (activity, _) => activity.ActorId == actorId),
                Note(seams, context));
        }

        /// <summary>
        /// <c>GET /users/{name}/followers</c> (AC-02): the actors whose Follow this instance
        /// answered with an Accept — the rows TASK-0044 writes through <c>IFollowStore</c>, paged
        /// by the same collection ladder as every other relation page.
        /// </summary>
        Task<IResult> FollowersAsync(string name, HttpContext context, ActorGrain actors, IFollowStore follows) =>
            ActorRelationAsync(name, "followers", context, actors, follows, seams);

        /// <summary>
        /// <c>GET /users/{name}/following</c> — the remote actors this one follows once they
        /// accepted; the pending ones never federate (AC-02, TASK-0044).
        /// </summary>
        Task<IResult> FollowingAsync(string name, HttpContext context, ActorGrain actors, IFollowStore follows) =>
            ActorRelationAsync(name, "following", context, actors, follows, seams);

        // ---- inboxes (contract §3, SPEC-0011 section E) ------------------------------

        /// <summary>
        /// One inbox delivery, both routes: contract §3 runs inside <see cref="IInboxPipeline"/>
        /// and this mapper is only its wire half (AC-03..AC-07). Every refusal carries the same
        /// shape and no traceId — a caller learns nothing from the difference between the causes —
        /// while the two distinct steps the contract spells out keep their own error word: the
        /// Digest that does not confer, and the Date outside the ±5 min window. An
        /// <see cref="InboxOutcome.Accepted"/> is always 202: applied, or deliberately left
        /// unapplied with the state intact (unknown type, no destination, <c>private</c>) — and a
        /// Follow answers with the Accept it owes in its body (AC-05).
        /// </summary>
        static async Task<IResult> InboxAsync(
            HttpContext context, IInboxPipeline pipeline, InboxTarget target)
        {
            var verdict = await pipeline.ReceiveAsync(context, target, context.RequestAborted);
            return verdict.Outcome switch
            {
                InboxOutcome.Accepted => verdict.ActivityJson is null
                    ? Results.Accepted()
                    : Results.Text(verdict.ActivityJson, ActivityJson,
                        statusCode: StatusCodes.Status202Accepted),
                InboxOutcome.PayloadTooLarge => AuthErrors.Write(context,
                    StatusCodes.Status413PayloadTooLarge, "payload_too_large",
                    "corpo acima de 1 MB: o corpo federado é recusado no passo 4 do contrato §3."),
                InboxOutcome.PayloadTooDeep => AuthErrors.Write(context,
                    StatusCodes.Status400BadRequest, "payload_too_deep",
                    "aninhamento acima de 32 níveis: o corpo é recusado no passo 4 do contrato §3."),
                InboxOutcome.InvalidActivity => AuthErrors.Write(context,
                    StatusCodes.Status400BadRequest, "invalid_activity",
                    "atividade malformada: id ou ator ausente."),
                InboxOutcome.RateLimited => TooManyRequests(context, verdict.RetryAfterSeconds),
                InboxOutcome.DigestMismatch => AuthErrors.Write(context,
                    StatusCodes.Status401Unauthorized, "digest_invalid",
                    "digest do corpo não confere.", withTraceId: false),
                InboxOutcome.StaleDate => AuthErrors.Write(context,
                    StatusCodes.Status401Unauthorized, "date_invalid",
                    "data fora da janela de ±5 min.", withTraceId: false),
                InboxOutcome.SenderUnresolvable => AuthErrors.Write(context,
                    StatusCodes.Status401Unauthorized, "sender_unresolvable",
                    "remetente não resolvível.", withTraceId: false),
                _ => AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                    "signature_invalid", "assinatura ausente ou inválida.", withTraceId: false),
            };
        }

        /// <summary>The 429 of §3 step 5: the envelope, plus the seconds the peer must wait.</summary>
        static IResult TooManyRequests(HttpContext context, string? retryAfterSeconds)
        {
            context.Response.Headers.RetryAfter = retryAfterSeconds ?? "60";
            return AuthErrors.Write(context, StatusCodes.Status429TooManyRequests,
                "rate_limited", "limite de requisições por minuto excedido (contrato §3 passo 5).");
        }

        // ---- object and container (contract §1/§1.2) ---------------------------------

        /// <summary>
        /// <c>GET /objects/{id}</c> (AC-03): the JSON-LD of the publication, behind the very same
        /// ladder <c>GET /api/posts/{id}</c> walks — absent and invisible share one 404, the
        /// trail records which one happened — plus the §4 rule this surface owes: a members-only
        /// or private publication never answers a federated read in clear.
        /// </summary>
        async Task<IResult> ObjectAsync(
            string id, HttpContext context, IContainerStore store, IObjectStore objects,
            IPolicyAuditStore audit)
        {
            var refused = Negotiate(context);
            if (refused is not null) return refused;

            var post = await objects.FindAsync(id); // the store strips query, fragment and encoding itself (KeyOf)
            if (post is null)
            {
                return await PostEndpoints.MissingAsync(context, audit, id);
            }

            var container = await PostEndpoints.VisibleAsync(
                (await store.FindAsync(post.ContainerSlug))?.Container, post.ContainerId,
                context, store, audit, forRead: true);
            if (container is null)
            {
                return PostEndpoints.HiddenPost(context);
            }

            if (post.VisibilityAtPublish is not (VisibilityPolicy.Public or VisibilityPolicy.Unlisted))
            {
                return PostEndpoints.HiddenPost(context); // §4: fetchable by URL, never in the clear below public
            }

            return Text(context, seams.Note(post, container, CanonicalOrigin.Of(context)));
        }

        /// <summary><c>GET /c/{slug}</c> (AC-03): the container as a federated <c>Group</c>.</summary>
        async Task<IResult> ContainerAsync(string slug, HttpContext context, IContainerStore store, IPolicyAuditStore audit)
        {
            var refused = Negotiate(context);
            if (refused is not null) return refused;

            var container = await VisibleAsync(slug, context, store, audit);
            return container is null
                ? ContainerEndpoints.Hidden(context)
                : Text(context, seams.Group(container, CanonicalOrigin.Of(context)));
        }

        /// <summary>
        /// <c>GET /c/{slug}/outbox</c> (AC-03): the FIRST page of the container's publications and
        /// the links that walk the rest — contract §1, "nunca o feed inteiro". Same ladder, same
        /// audience rule and same paging as the actor outbox above.
        /// </summary>
        async Task<IResult> ContainerOutboxAsync(
            string slug, HttpContext context, IContainerStore store, IObjectStore objects,
            IActivityStore activities, IPolicyAuditStore audit)
        {
            var refused = Negotiate(context);
            if (refused is not null) return refused;

            var container = await VisibleAsync(slug, context, store, audit);
            if (container is null) return ContainerEndpoints.Hidden(context);

            return await AnswerAsync(
                context, seams, container, $"{container.Id}/outbox",
                () => PublicPostsAsync(activities, objects, store, (_, post) => post.ContainerSlug == slug),
                Note(seams, context));
        }

        /// <summary>
        /// <c>GET /c/{slug}/members</c> (AC-03, "com visibilidade"): the reader must see the
        /// container (the ladder above, 404 otherwise) and the page carries only <c>Active</c>
        /// memberships — the same rule <c>GET /api/containers/{slug}/members</c> applies to a
        /// reader that is not Admin+: pending, silenced, banned and left rows never federate.
        /// </summary>
        async Task<IResult> MembersAsync(string slug, HttpContext context, IContainerStore store, IPolicyAuditStore audit)
        {
            var refused = Negotiate(context);
            if (refused is not null) return refused;

            var container = await VisibleAsync(slug, context, store, audit);
            if (container is null) return ContainerEndpoints.Hidden(context);

            return await AnswerAsync<ContainerMembership>(
                context, seams, container, $"{container.Id}/members",
                async () => (await store.ListMembershipsAsync(slug))
                    .Where(member => member.State == MembershipState.Active)
                    .ToList(),
                member => Task.FromResult(JsonSerializer.Serialize(member.ActorId)));
        }

        /// <summary>
        /// <c>GET /.well-known/nodeinfo</c> (AC-04): who runs this software and at which version.
        /// Plain JSON, not an ActivityStreams document, so it answers <c>application/json</c> and
        /// carries no <c>@context</c> — and no Accept gate: the discovery client that asks for it
        /// does not speak ActivityPub.
        /// </summary>
        static IResult NodeInfoAsync()
        {
            var assembly = typeof(FederationEndpoints).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = informational is null
                ? assembly.GetName().Version?.ToString() ?? "0.0.0"
                : informational.Split('+')[0]; // a "+{commit}" suffix is build metadata, not the release

            return Results.Json(new
            {
                version = "2.0",
                software = new { name = "squid", version },
                protocols = new[] { "activitypub" },
                services = new { inbound = Array.Empty<string>(), outbound = Array.Empty<string>() },
                openRegistrations = false,
                metadata = new { },
            });
        }
    }

    // ---- shared rules ---------------------------------------------------------------

    /// <summary>
    /// The <c>followers</c> and <c>following</c> collections of an actor: the existence check and
    /// the negotiation of every route above, over the relation rows <c>IFollowStore</c> holds
    /// (TASK-0044) — each actor URI quoted once, here, so it embeds verbatim by the adapter, never
    /// quoted a second time as a string inside the page.
    /// </summary>
    private static async Task<IResult> ActorRelationAsync(
        string name, string relation, HttpContext context, ActorGrain actors,
        IFollowStore follows, FederationSeams seams)
    {
        var refused = Negotiate(context);
        if (refused is not null) return refused;
        if (await KnownAsync(name, actors) is null) return Results.NotFound();

        var edges = relation == "followers"
            ? await follows.FollowersAsync(name)
            : await follows.FollowingAsync(name);
        IReadOnlyList<string> items = edges
            .Select(edge => JsonSerializer.Serialize(edge.ActorUri))
            .ToList();

        return await AnswerAsync<string>(
            context, seams, null, $"{ActorId(context, name)}/{relation}",
            () => Task.FromResult(items), item => Task.FromResult(item));
    }

    /// <summary>
    /// Answers a collection: without <c>?page=</c> the collection document itself (count and the
    /// two links, NEVER the items — AC-02), with it the page behind those links. An empty or
    /// out-of-range page is a valid empty page; only a page that is not a positive integer is a
    /// 400, the same shape the feed's <c>limit</c> refusal answers with. <paramref name="container"/>
    /// is the one whose collection this is — null for an actor's, non-null so the three
    /// <c>squid:</c> fields of AC-03 ride both the collection and its pages.
    /// </summary>
    private static async Task<IResult> AnswerAsync<T>(
        HttpContext context, FederationSeams seams, Container? container, string id,
        Func<Task<IReadOnlyList<T>>> load, Func<T, Task<string>> toJson)
    {
        var (pageNumber, refusal) = PageOf(context);
        if (refusal is not null) return refusal;

        var all = await load();
        if (pageNumber is null)
        {
            // next always names page 2, even for an empty collection: AC-02 asks for first AND
            // next on the collection itself, and page 2 of an empty collection is a valid empty page.
            return Text(context, seams.Collection(container, id, all.Count, $"{id}?page=1", $"{id}?page=2"));
        }

        var skip = (long)(pageNumber.Value - 1) * PageSize;
        var slice = skip >= all.Count ? new List<T>() : all.Skip((int)skip).Take(PageSize).ToList();
        var items = new List<string>(slice.Count);
        foreach (var item in slice)
        {
            items.Add(await toJson(item));
        }

        var next = skip + slice.Count < all.Count ? $"{id}?page={pageNumber.Value + 1}" : null;
        return Text(context, seams.Page(container, $"{id}?page={pageNumber.Value}", id, items, next));
    }

    /// <summary>
    /// The public posts behind an outbox, newest first by the GLOBAL activity sequence (GAP-23 —
    /// Object.Seq restarts per container, so it orders nothing across one).
    /// shortcut: the whole activity log is read and filtered per request, paged in memory at 20 —
    /// an index over (actor, container) on the log is the upgrade when an outbox is no longer a
    /// tail of it (P-06; the log is append-only and the page is the only reader here).
    /// </summary>
    private static async Task<IReadOnlyList<OutboxItem>> PublicPostsAsync(
        IActivityStore activities, IObjectStore objects, IContainerStore store,
        Func<ActivityEvent, PostObject, bool> matches)
    {
        var found = new List<(long Seq, OutboxItem Item)>();
        var containers = new Dictionary<string, Container?>(StringComparer.Ordinal);

        foreach (var activity in await activities.ReadAsync())
        {
            if (!activity.Type.Equals(Create, StringComparison.Ordinal))
            {
                continue;
            }

            var post = await objects.FindAsync(activity.ObjectId);
            if (post is null || post.VisibilityAtPublish != VisibilityPolicy.Public)
            {
                continue; // tombstoned, or out of every public collection (contract §4)
            }

            if (!containers.TryGetValue(post.ContainerSlug, out var container))
            {
                container = (await store.FindAsync(post.ContainerSlug))?.Container;
                if (container is { IsDeleted: true }) container = null;
                containers[post.ContainerSlug] = container;
            }

            if (container is null || !matches(activity, post))
            {
                continue; // a soft-deleted container leaves every public read (SPEC-0008 G)
            }

            found.Add((activity.Seq, new OutboxItem(post, container)));
        }

        found.Sort((left, right) => right.Seq.CompareTo(left.Seq));
        return found.Select(entry => entry.Item).ToList();
    }

    /// <summary>The projector of every outbox: the Note of the pair the loader already resolved.</summary>
    private static Func<OutboxItem, Task<string>> Note(FederationSeams seams, HttpContext context)
    {
        var origin = CanonicalOrigin.Of(context);
        return item => Task.FromResult(seams.Note(item.Post, item.Container, origin));
    }

    /// <summary>The container behind a route, visible to this reader, or null (trail already written).</summary>
    private static async Task<Container?> VisibleAsync(
        string slug, HttpContext context, IContainerStore store, IPolicyAuditStore audit) =>
        await PostEndpoints.VisibleAsync(
            (await store.FindAsync(slug))?.Container,
            $"{CanonicalOrigin.Of(context)}/c/{slug}",
            context, store, audit, forRead: true);

    /// <summary>The actor of a route, or null — the handle rule first, the row second.</summary>
    private static async Task<ActorState?> KnownAsync(string name, ActorGrain actors) =>
        ActorHandle.IsValid(name) ? await actors.GetStateAsync(name) : null;

    /// <summary>The canonical actor URI of the request (B-09a: one origin, the configured one or this request's).</summary>
    private static string ActorId(HttpContext context, string name) =>
        $"{CanonicalOrigin.Of(context)}/users/{name}";

    /// <summary>AC-06: negotiate first; null means the caller may read a document.</summary>
    private static IResult? Negotiate(HttpContext context) =>
        AcceptNegotiation.RefuseUnlessAccepted(context);

    /// <summary>The <c>?page=</c> of a collection: absent (the collection), a positive integer, or its 400.</summary>
    private static (int? Page, IResult? Refusal) PageOf(HttpContext context)
    {
        var raw = context.Request.Query["page"].ToString();
        if (raw.Length == 0)
        {
            return (null, null);
        }

        return int.TryParse(raw, out var page) && page >= 1
            ? (page, null)
            : (null, AuthErrors.Write(context, StatusCodes.Status400BadRequest,
                "validation_failed", "page inválido: informe um inteiro positivo."));
    }

    /// <summary>One document on the wire, in the media type of contract §6.</summary>
    private static IResult Text(HttpContext context, string json) => Results.Text(json, ActivityJson);

    /// <summary>One outbox row: the publication and the container it lives in, resolved together.</summary>
    private sealed record OutboxItem(PostObject Post, Container Container);
}
