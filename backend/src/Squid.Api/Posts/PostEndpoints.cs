using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Api.Discovery;
using Squid.Api.Federation;
using Squid.Api.Media;
using Squid.Domain;
using Squid.Domain.Containers;
using Squid.Domain.Objects;

namespace Squid.Api.Posts;

/// <summary>The publish body of SPEC-0009 section A: text plus the media hashes of section E (≤ 10).</summary>
public sealed record CreatePostRequest(string? Content, IReadOnlyList<string>? ContentHashes = null);

/// <summary>The reply body: the same text field, while the route already names the parent.</summary>
public sealed record CreateReplyRequest(string? Content);

/// <summary>
/// The removal body of section H: the reason a Moderator+ removal owes data-model §2.5 (R-07).
/// The contract fixes no carrier for it, so <c>DELETE /api/posts/{id}</c> also reads the same
/// name from the query; a caller that sends neither is refused with the 422 of AC-02.
/// </summary>
public sealed record RemovePostRequest(string? TakedownReason);

/// <summary>
/// SPEC-0009 sections A and H — publish, read, reply and remove over the object aggregate.
/// Session and CSRF ride the middleware <c>MapSquidAuth</c> already installs in the pipeline; every
/// posting decision goes through <see cref="IPolicyEvaluator"/> with the FULL actor context
/// (data-model §3.2 hard rule: never a role <c>if</c>) against the policy in force; every refusal
/// reaches the append-only trail before the answer leaves (FR-019, SC-011); and the 404 rule of
/// FR-018/SC-005 answers byte-identically for absent and invisible — the container's own cases
/// reuse <see cref="ContainerEndpoints.Hidden"/> itself, the post cases use the same envelope shape.
/// </summary>
public static class PostEndpoints
{
    /// <summary>
    /// The trail words of data-model §2.11 this slice writes for publishing: <c>Post</c> (the
    /// publish decision) and <c>Comment</c> (the reply decision) — both predate posts in the enum,
    /// so the shared constants of <c>IPolicyAuditStore</c> gain nothing from local aliases here.
    /// </summary>
    private const string PostDecision = "Post";

    private const string CommentDecision = "Comment";

    /// <summary>
    /// The trail word of data-model §2.11 for a removal (SPEC-0009 section H): the enum's own
    /// spelling, kept local exactly like <see cref="PostDecision"/> — the shared constants of
    /// <c>IPolicyAuditStore</c> live in the container files this task may not edit.
    /// </summary>
    private const string RemoveContentDecision = "RemoveContent";

    private const string RepliesSuffix = "/replies";

    /// <summary>The suffix that turns the POST catch-all into the denunciation of section I.</summary>
    private const string ReportSuffix = "/report";

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    /// <summary>Maps the publish routes; runs after <c>MapSquidAuth</c>, on the session pipeline.</summary>
    public static WebApplication MapSquidPosts(this WebApplication app)
    {
        app.MapPost("/api/containers/{slug}/posts", PublishAsync);
        // The id arrives as sent — the opaque tail of /o/{id} or the whole canonical URI, plain or
        // percent-encoded — because a client that pastes the `id` it just got back must hit the row.
        app.MapGet("/api/posts/{*id}", GetAsync);
        // The removal walks the very same capture as the read: one id, whatever spelling, and the
        // handler answers the shared 404 for anything it may not touch (contract §5, section H).
        app.MapDelete("/api/posts/{*id}", RemoveAsync);
        // One capture carries `{id}` and its suffix alike (`/replies`, now `/report`): an id that
        // itself contains slashes cannot sit in a single segment, so the dispatcher reads the
        // suffix off the tail and hands the path to the handler that owns it (contract §5).
        app.MapPost("/api/posts/{*target}", TargetAsync);
        return app;
    }

    /// <summary>
    /// The single POST route under <c>/api/posts</c> (contract §5): the suffix decides the
    /// handler — <c>/report</c> is the denunciation of section I, <c>/replies</c> the answer of
    /// section A, anything else the shared 404. The suffix rides a catch-all because a post id
    /// is an opaque URI: the caller may paste the tail of <c>/o/{id}</c>, the whole canonical id
    /// plain or percent-encoded, and every spelling must reach the same row — the rule GET and
    /// DELETE already walk through <c>{*id}</c>. The body is read here, once, for the same reason.
    /// </summary>
    private static async Task<IResult> TargetAsync(
        string target, HttpContext context, IContainerStore store, IPolicyAuditStore audit,
        IPolicyEvaluator evaluator, IObjectStore objects, IActivityStore activities,
        IActorStatusStore statuses, IReportStore reports, StreamHub hub, TimeProvider time,
        IOutboundPipeline outbound)
    {
        var path = Uri.UnescapeDataString(target ?? string.Empty).Trim().TrimEnd('/');
        if (path.EndsWith(ReportSuffix, StringComparison.Ordinal))
        {
            var request = await ReadJsonAsync<ReportRequest>(context);
            return await ReportEndpoints.ReportAsync(
                path[..^ReportSuffix.Length], request, context, store, audit, objects, reports, time);
        }

        if (path.EndsWith(RepliesSuffix, StringComparison.Ordinal))
        {
            var request = await ReadJsonAsync<CreateReplyRequest>(context);
            return await ReplyAsync(
                path[..^RepliesSuffix.Length], request, context, store, audit, evaluator,
                objects, activities, statuses, hub, time, outbound);
        }

        // The contract has no other POST under /api/posts — the route answers like nothing is there.
        return HiddenPost(context);
    }

    /// <summary>
    /// The JSON body this one dispatcher reads by hand now that a single POST route serves two
    /// suffixes: no JSON content type, an empty body or a document that will not parse answers
    /// null, and the ladder's own refusal speaks for it — the 400 of <c>ContentRefusal</c>, the
    /// 422 of the denunciation — never a framework error with a different envelope.
    /// </summary>
    private static async Task<T?> ReadJsonAsync<T>(HttpContext context) where T : class
    {
        if (!context.Request.HasJsonContentType()) return null;

        try
        {
            return await context.Request.ReadFromJsonAsync<T>(context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>POST /api/containers/{slug}/posts</c> (contract §5, SPEC-0009 A): session (401), the
    /// visibility ladder (404 byte-identical to an absent slug — SC-005), the §3.1 refusal of a
    /// suspended actor (403, audited), the policy verdict for the full actor (403 with its Deny
    /// row — AC-02), the payload (400), and only then the write: the container's next
    /// <c>Object.Seq</c> taken under the ETag, the row inserted, the <c>Activity: Create</c>
    /// appended with its global Seq and raw JSON payload, and the 201 with the four fields AC-01
    /// pins — <c>id</c>, <c>seq</c>, <c>policyVersion</c> and the frozen <c>visibilityAtPublish</c>.
    /// </summary>
    private static async Task<IResult> PublishAsync(
        string slug, CreatePostRequest? request, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, IPolicyEvaluator evaluator, IObjectStore objects,
        IActivityStore activities, IActorStatusStore statuses, IMediaStore media,
        StreamHub hub, TimeProvider time, LinkUnfurler unfurler, IOutboundPipeline outbound)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var state = await store.FindAsync(slug);
        var container = await VisibleAsync(state?.Container, $"{Origin(context)}/c/{slug}",
            context, store, audit, forRead: false);
        if (container is null)
        {
            return ContainerEndpoints.Hidden(context);
        }

        var actorId = ContainerEndpoints.ActorId(context, current.Handle);

        // data-model §3.1: a suspended actor does not publish — the refusal is a decision like
        // any other and reaches the trail before the 403 (AC-06).
        if (await statuses.GetAsync(current.Handle) == ActorStatus.Suspended)
        {
            await RecordAsync(audit, context, PostDecision, container.Id, PolicyAudit.Deny,
                container.Policy.Version, "ator suspenso não publica (data-model §3.1)");
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "o ator está suspenso e não pode publicar (data-model §3.1).");
        }

        var membership = await store.FindMembershipAsync(slug, current.Handle);
        var decision = evaluator.Evaluate(
            PolicyAction.Post, ActorContext(membership, container.Policy), container.Policy);
        if (!decision.Permitted)
        {
            await RecordAsync(audit, context, PostDecision, container.Id, PolicyAudit.Deny,
                decision.PolicyVersion, decision.Reason);
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "a política do contêiner nega esta publicação para o seu papel.");
        }

        var refusal = ContentRefusal(context, request?.Content, out var content);
        if (refusal is not null)
        {
            return refusal;
        }

        // The media half of the publish body (SPEC-0009 section E, AC-03): every hash must be an
        // upload that exists, at most 10, and the §2.6 alt rule guards the ATTACH path too — a
        // publication carrying an image/video/gif without alt is refused right here (400), never
        // only in the UI.
        var (attachmentRefusal, attachments) = await AttachmentsAsync(context, request?.ContentHashes, media);
        if (attachmentRefusal is not null)
        {
            return attachmentRefusal;
        }

        // SPEC-0009 section G (GAP-26): a content that IS a single URL unfurls synchronously into
        // a Link attachment — fail-open by contract, so any refusal, timeout or oversized body
        // leaves the post with just its URL (201, never blocked). The RAW content is what gets
        // fetched: the stored form escapes `&` to `&amp;` and is no longer the URL the author
        // pasted. The §2.5 cap of 10 wins over a preview that would push the list past it.
        var unfurled = await unfurler.TryUnfurlAsync(request?.Content, context.RequestAborted);
        if (unfurled is not null && attachments.Count < Attachment.MaxPerPost)
        {
            attachments.Add(unfurled);
        }

        var seq = await objects.NextSeqAsync(slug);
        var id = $"{Origin(context)}/o/{NewToken()}";
        var post = new PostObject
        {
            Id = id,
            AuthorActorId = actorId,
            ContainerId = container.Id,
            ContainerSlug = container.Slug,
            Kind = ObjectKind.Post,
            Content = content,
            Attachments = attachments,
            VisibilityAtPublish = container.Policy.Visibility,
            PolicyVersion = decision.PolicyVersion,
            Seq = seq,
            ReplyToId = null,
            Depth = 0,
            CreatedAt = time.GetUtcNow(),
        };

        if (!await objects.CreateAsync(post))
        {
            throw new IOException($"the object id '{id}' already exists (insert only, never a rewrite)");
        }

        var view = PostView.Of(post);
        await activities.AppendAsync(new ActivityEvent(
            $"urn:squid:activity:{NewToken()}", "Create", actorId, id, 0,
            JsonSerializer.Serialize(view, PayloadJson), post.CreatedAt));

        // The row and the event are durable before any subscriber wakes (SPEC-0009 section D): the
        // stream only ever shows a seq a reconnecting read finds, and the wake itself never blocks.
        hub.Publish();

        // Fan-out da saída (SPEC-0011 section F, TASK-0045): as entregas federadas ficam
        // registradas como Pending ANTES desta resposta sair (AC-05) — escrita local rápida —
        // e nenhum envio acontece aqui: a rede nunca bloqueia a publicação (FR-036) e o motor
        // que drena a fila é da TASK-0046. private não cria fila nenhuma (AC-02, §7.3).
        await outbound.EnqueueCreateAsync(Origin(context), post, container);

        // The grant is a decision too (FR-019): recorded before the answer leaves.
        await RecordAsync(audit, context, PostDecision, container.Id, PolicyAudit.Allow,
            decision.PolicyVersion, decision.Reason);

        return Results.Json(view, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// <c>GET /api/posts/{id}</c> (contract §5, FR-026): the post the reader may see, or the same
    /// 404 body for a post that does not exist and a post outside their sight — no traceId, so
    /// existence never leaks (SC-005, AC-04). Reading applies the CURRENT policy and membership
    /// (§4): the frozen <c>VisibilityAtPublish</c> may only be more restrictive, never less.
    /// </summary>
    private static async Task<IResult> GetAsync(
        string id, HttpContext context, IContainerStore store, IObjectStore objects,
        IPolicyAuditStore audit)
    {
        var post = await objects.FindAsync(id);
        if (post is null)
        {
            return await MissingAsync(context, audit, id);
        }

        var container = await VisibleAsync(
            (await store.FindAsync(post.ContainerSlug))?.Container, post.ContainerId,
            context, store, audit, forRead: true);
        if (container is null)
        {
            return HiddenPost(context);
        }

        return Results.Json(PostView.Of(post));
    }

    /// <summary>
    /// <c>DELETE /api/posts/{id}</c> (contract §5, SPEC-0009 section H, FR-027): the author's or a
    /// Moderator+'s removal behind the ladder every post route walks — session (401), the
    /// byte-identical 404 for an absent or still-unseen post (SC-005, AC-04), the refusal of
    /// anyone who is neither author nor Moderator+ (403 with its <c>Deny</c> row — AC-02), and for
    /// a third party's removal the non-empty <c>takedownReason</c> data-model §2.5 owes (R-07; 422
    /// as pure payload validation and so without a trail row, SPEC-0008 C). Only then the write:
    /// the tombstone (<c>DeletedAt</c> + reason), the <c>Activity: Delete</c> event on the global
    /// sequence, and the <c>RemoveContent</c> row of §2.11 with author, instant and
    /// <c>PolicyVersion</c> (AC-01). The 200 reports the state of what was removed; the row, the
    /// event and the trail are never deleted (data-model §2.5) while the post leaves every read.
    /// </summary>
    private static async Task<IResult> RemoveAsync(
        string id, HttpContext context, IContainerStore store, IPolicyAuditStore audit,
        IPolicyEvaluator evaluator, IObjectStore objects, IActivityStore activities, TimeProvider time,
        IOutboundPipeline outbound)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var post = await objects.FindAsync(id);
        if (post is null)
        {
            return await MissingAsync(context, audit, id);
        }

        // The reader must see the post before they may act on it — the same read half of §3.3 the
        // GET walks, so a Banned member and a stranger behind a closed container both meet the
        // 404 instead of a hint that the id exists (SC-005).
        var container = await VisibleAsync(
            (await store.FindAsync(post.ContainerSlug))?.Container, post.ContainerId,
            context, store, audit, forRead: true);
        if (container is null)
        {
            return HiddenPost(context);
        }

        var actorId = ContainerEndpoints.ActorId(context, current.Handle);
        var reason = await ReasonAsync(context);
        int policyVersion;
        if (string.Equals(post.AuthorActorId, actorId, StringComparison.Ordinal))
        {
            // Your own post: no reason owed (the task's decided semantics) and no role to hold —
            // the identity IS the authority data-model §2.5 gives the author.
            policyVersion = container.Policy.Version;
        }
        else
        {
            // data-model §3.2's single path for a role check: the FULL actor context through the
            // evaluator, so Moderator+ means the capability AND the one active state §3.3 grants
            // a write (AC-02) — never a role `if` in this file.
            var membership = await store.FindMembershipAsync(post.ContainerSlug, current.Handle);
            var decision = evaluator.Evaluate(
                PolicyAction.Moderate, ActorContext(membership, container.Policy), container.Policy);
            if (!decision.Permitted)
            {
                await RecordAsync(audit, context, RemoveContentDecision, post.Id, PolicyAudit.Deny,
                    decision.PolicyVersion, decision.Reason);
                return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                    "policy_denied", "remover o post de terceiro exige o papel de Moderador ou acima (data-model §3.2).");
            }

            policyVersion = decision.PolicyVersion;
            if (string.IsNullOrWhiteSpace(reason))
            {
                return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                    "validation_failed", "takedownReason obrigatório e não vazio em remoção de terceiros (R-07).");
            }
        }

        var now = time.GetUtcNow();
        if (!await RemoveContentAsync(post, actorId, reason, policyVersion, now, objects, activities, audit))
        {
            // A concurrent removal took the tombstone first: the post is gone for everyone by now,
            // so this answer is the same 404 an absent id gets.
            return await MissingAsync(context, audit, id);
        }

        // R-08 / AC-06: a remoção propaga um Delete assinado a cada destinatário que recebeu (ou
        // ia receber) o Create — fila local como a publicação; o envio segue fora da request.
        await outbound.EnqueueDeleteAsync(Origin(context), post, container);

        return Results.Json(new { id = post.Id, deletedAt = now, takedownReason = reason });
    }

    /// <summary>
    /// The write half of a removal (SPEC-0009 section H): the tombstone (<c>DeletedAt</c> +
    /// reason), the <c>Activity: Delete</c> event on the global sequence that carries it to the
    /// projections, and the <c>RemoveContent</c> row of data-model §2.11 with author, instant and
    /// policy version. One path shared by the author's <c>DELETE /api/posts/{id}</c> and the
    /// operator's <c>resolve action=remove</c> (TASK-0031), so the tombstone logic is never
    /// forked. False when the post is already tombstoned: the caller answers the shared 404, or
    /// closes the report — the content is gone either way.
    /// </summary>
    internal static async Task<bool> RemoveContentAsync(
        PostObject post, string actorId, string? reason, int policyVersion, DateTimeOffset now,
        IObjectStore objects, IActivityStore activities, IPolicyAuditStore audit)
    {
        if (!await objects.DeleteAsync(post.Id, now, reason))
        {
            return false;
        }

        var tombstone = JsonSerializer.Serialize(
            new { deletedAt = now, takedownReason = reason }, PayloadJson);

        // The event carries the removal to the projections (section H: feed, timeline and ?since
        // drop the post when the projector reads it) and keeps the global sequence running.
        await activities.AppendAsync(new ActivityEvent(
            $"urn:squid:activity:{NewToken()}", "Delete", actorId, post.Id, 0,
            JsonSerializer.Serialize(new { id = post.Id, deletedAt = now, takedownReason = reason }, PayloadJson),
            now));

        // The trail row this task owes AC-01: author, instant and the policy version the decision
        // was taken under — appended after the write, before the answer leaves, never rewritten.
        await audit.AppendAsync(new PolicyAuditEntry(
            now, actorId, RemoveContentDecision, post.Id, PolicyAudit.Allow, policyVersion, tombstone));
        return true;
    }

    /// <summary>
    /// The <c>takedownReason</c> of a removal: the contract fixes neither the body nor the query
    /// as its carrier, so both spellings answer — the first non-empty value found, null when the
    /// caller sent none (the 422 of R-07 belongs to the ladder, never to this reader).
    /// </summary>
    private static async Task<string?> ReasonAsync(HttpContext context)
    {
        var reason = context.Request.Query["takedownReason"].ToString();
        if (!string.IsNullOrWhiteSpace(reason)) return reason;
        if (!context.Request.HasJsonContentType()) return null;

        try
        {
            return (await context.Request.ReadFromJsonAsync<RemovePostRequest>(context.RequestAborted))
                ?.TakedownReason;
        }
        catch (JsonException)
        {
            return null; // a body that will not parse carries no reason; the 422 answers it
        }
    }

    /// <summary>
    /// <c>POST /api/posts/{id}/replies</c> (contract §5, SPEC-0009 A): an answer inside the same
    /// container as its parent, chained by <c>ReplyToId</c> with the depth rule of §2.5 — depth 3
    /// publishes, the level above it is <c>422 invalid_reply_target</c> (AC-05). The ladder is
    /// the publish ladder with the target's own checks in front: parent absent or outside the
    /// reader's sight is the byte-identical 404 (never a hint of what exists), then the depth,
    /// then §3.1 and the policy verdict for <c>Comment</c>, then the payload. The id arrives
    /// with the <c>/replies</c> suffix already read off the tail by <see cref="TargetAsync"/>, and
    /// the body rides <paramref name="request"/> because that one dispatcher read it once.
    /// </summary>
    private static async Task<IResult> ReplyAsync(
        string parentId, CreateReplyRequest? request, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, IPolicyEvaluator evaluator, IObjectStore objects,
        IActivityStore activities, IActorStatusStore statuses, StreamHub hub, TimeProvider time,
        IOutboundPipeline outbound)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var parent = await objects.FindAsync(parentId);
        if (parent is null)
        {
            return await MissingAsync(context, audit, parentId);
        }

        var container = await VisibleAsync(
            (await store.FindAsync(parent.ContainerSlug))?.Container, parent.ContainerId,
            context, store, audit, forRead: false);
        if (container is null)
        {
            return HiddenPost(context);
        }

        // The target itself is invalid above the MVP's chain — §9.9's word for a reply that can
        // never belong where it was sent (AC-05, gap row of the task).
        if (!ReplyDepth.AllowedFor(parent.Depth))
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "invalid_reply_target",
                $"profundidade excedida: a resposta ficaria no nível {ReplyDepth.DepthOf(parent.Depth)}, acima do máximo {ReplyDepth.MaxDepth}.");
        }

        var actorId = ContainerEndpoints.ActorId(context, current.Handle);

        if (await statuses.GetAsync(current.Handle) == ActorStatus.Suspended)
        {
            await RecordAsync(audit, context, CommentDecision, parent.Id, PolicyAudit.Deny,
                container.Policy.Version, "ator suspenso não publica nem comenta (data-model §3.1)");
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "o ator está suspenso e não pode publicar (data-model §3.1).");
        }

        var membership = await store.FindMembershipAsync(parent.ContainerSlug, current.Handle);
        var decision = evaluator.Evaluate(
            PolicyAction.Comment, ActorContext(membership, container.Policy), container.Policy);
        if (!decision.Permitted)
        {
            await RecordAsync(audit, context, CommentDecision, parent.Id, PolicyAudit.Deny,
                decision.PolicyVersion, decision.Reason);
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "a política do contêiner nega esta resposta para o seu papel.");
        }

        var refusal = ContentRefusal(context, request?.Content, out var content);
        if (refusal is not null)
        {
            return refusal;
        }

        var seq = await objects.NextSeqAsync(parent.ContainerSlug);
        var id = $"{Origin(context)}/o/{NewToken()}";
        var post = new PostObject
        {
            Id = id,
            AuthorActorId = actorId,
            ContainerId = parent.ContainerId,
            ContainerSlug = parent.ContainerSlug,
            Kind = ObjectKind.Post,
            Content = content,
            VisibilityAtPublish = container.Policy.Visibility,
            PolicyVersion = decision.PolicyVersion,
            Seq = seq,
            ReplyToId = parent.Id,
            Depth = ReplyDepth.DepthOf(parent.Depth),
            CreatedAt = time.GetUtcNow(),
        };

        if (!await objects.CreateAsync(post))
        {
            throw new IOException($"the object id '{id}' already exists (insert only, never a rewrite)");
        }

        var view = PostView.Of(post);
        await activities.AppendAsync(new ActivityEvent(
            $"urn:squid:activity:{NewToken()}", "Create", actorId, id, 0,
            JsonSerializer.Serialize(view, PayloadJson), post.CreatedAt));

        // A reply is a post and its event is durable here too, so the live channel wakes the same
        // way (SPEC-0009 section D): one hub, one `post` event name, no per-route emission.
        hub.Publish();

        // Uma resposta É uma publicação: o mesmo fan-out da saída — o Create federado carrega o
        // inReplyTo (contrato §7.15) — enfileirado antes da resposta, sem nenhum envio aqui.
        await outbound.EnqueueCreateAsync(Origin(context), post, container);

        await RecordAsync(audit, context, CommentDecision, parent.Id, PolicyAudit.Allow,
            decision.PolicyVersion, decision.Reason);

        return Results.Json(view, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// The visibility decision of a post, appended to the trail before the answer leaves, exactly
    /// as <c>ContainerEndpoints.DecideVisibilityAsync</c> does it for a container: <c>Allow</c>
    /// when the reader may see it, <c>NotFound</c> when the container is absent, soft-deleted or
    /// out of their sight — the two are byte-identical at the wire and the trail records which
    /// one happened (FR-018, SC-005). Returns the container when visible and null otherwise, so
    /// the caller has one refusal to answer with. <paramref name="forRead"/> keeps §3.3's halves
    /// apart: a read also needs the reader not to be <c>Banned</c>, a write keeps the pure
    /// existence rule of the container's own ladder. Internal: the feed route answers its 404
    /// through this very ladder (TASK-0025), so a container and its feed can never disagree.
    /// </summary>
    internal static async Task<Container?> VisibleAsync(
        Container? container, string resourceId, HttpContext context, IContainerStore store,
        IPolicyAuditStore audit, bool forRead)
    {
        var visible = container is not null
            && container.DeletedAt is null // soft delete: gone for every reader and every writer (SPEC-0008 G)
            && await CanSeeAsync(container, context, store, forRead);

        await RecordAsync(audit, context, PolicyAudit.View,
            container?.Id ?? resourceId,
            visible ? PolicyAudit.Allow : PolicyAudit.NotFound,
            container?.Policy.Version ?? 0,
            visible
                ? "the visibility policy or an active membership admits the reader"
                : container is null
                    ? "the container of the post does not exist (FR-018)"
                    : container.DeletedAt is not null
                        ? "the container of the post was soft-deleted and stays hidden (SPEC-0008 G)"
                        : "the post is not visible to this reader (FR-018, SC-005)");

        return visible ? container : null;
    }

    /// <summary>
    /// A post that is not there at all: the <c>NotFound</c> row of the trail (the decision still
    /// happened) and the very same 404 an invisible post answers with.
    /// </summary>
    internal static async Task<IResult> MissingAsync(HttpContext context, IPolicyAuditStore audit, string id)
    {
        await RecordAsync(audit, context, PolicyAudit.View, AbsolutePostId(context, id),
            PolicyAudit.NotFound, 0, "the post does not exist (FR-018)");
        return HiddenPost(context);
    }

    /// <summary>
    /// The rules of <c>ContainerEndpoints.CanSeeAsync</c> (SPEC-0008 section C) verbatim, mirrored
    /// instead of extracted: the container files are TASK-0017..0022's and stay untouched, while
    /// the rule itself must be the same one — only the public preset is readable without a
    /// membership, every other visibility answers the byte-identical 404, and a read carries §3.3
    /// (<c>Banned</c> reads nothing at all) while a write keeps the pure existence rule. Internal
    /// for the timeline's per-container gate (TASK-0025), which reuses the rule instead of
    /// re-deriving it.
    /// </summary>
    internal static async Task<bool> CanSeeAsync(
        Container container, HttpContext context, IContainerStore store, bool forRead)
    {
        var isPublic = container.Policy.Visibility == VisibilityPolicy.Public;
        var current = AuthSessions.Current(context);
        if (isPublic && !forRead) return true;

        var membership = current is null
            ? null
            : await store.FindMembershipAsync(container.Slug, current.Handle);

        if (forRead && membership?.State == MembershipState.Banned) return false;
        if (isPublic) return true;

        return membership?.State is MembershipState.Active or MembershipState.Silenced;
    }

    /// <summary>The reader's role on this container (data-model §3.2) — mirror of ContainerEndpoints.RoleOf.</summary>
    private static ContainerRole RoleOf(ContainerMembership? membership) =>
        membership is not { State: MembershipState.Active }
            ? ContainerRole.Guest
            : membership.Role switch
            {
                MembershipRole.Owner => ContainerRole.Owner,
                MembershipRole.Admin => ContainerRole.Admin,
                MembershipRole.Moderator => ContainerRole.Moderator,
                MembershipRole.Member => ContainerRole.Member,
                _ => ContainerRole.Guest,
            };

    /// <summary>
    /// The full actor context GAP-11 hands the evaluator (SPEC-0008 section F) — mirror of
    /// <c>ContainerEndpoints.ActorContext</c>: role and state exactly as the row holds them, plus
    /// the publisher seat only while the membership is active. The three together, never the role
    /// alone (the dispatch of this task repeats the rule for posting decisions).
    /// </summary>
    private static PolicyActor ActorContext(ContainerMembership? membership, ContainerPolicy policy) =>
        new(
            RoleOf(membership),
            membership?.State,
            membership is { State: MembershipState.Active }
                && policy.PublisherIds.Contains(membership.ActorId, StringComparer.Ordinal));

    /// <summary>The actor behind this request: its canonical id, or the anonymous marker.</summary>
    private static string Reader(HttpContext context)
    {
        var current = AuthSessions.Current(context);
        return current is null ? PolicyAudit.AnonymousActor : ContainerEndpoints.ActorId(context, current.Handle);
    }

    /// <summary>
    /// The identity origin of P-02/P-03 (SPEC-0011 section A): the one canonical helper —
    /// <c>Instance:Domain</c> over https when the operator published one, else scheme + Host of
    /// the very request that asked.
    /// </summary>
    internal static string Origin(HttpContext context) => CanonicalOrigin.Of(context);

    /// <summary>The trail row itself — one call site, so every decision of this slice is shaped alike.</summary>
    private static Task RecordAsync(
        IPolicyAuditStore audit, HttpContext context, string action, string resourceId,
        string result, int policyVersion, string reason) =>
        audit.AppendAsync(new PolicyAuditEntry(
            DateTimeOffset.UtcNow, Reader(context), action, resourceId, result, policyVersion, reason));

    /// <summary>
    /// The 400 of §8 for a payload this route cannot take; null when the content stands. The cap
    /// is held against BOTH forms of the text (AC-08, QA-0022/F1): the raw body refuses early and
    /// the SANITIZED text must fit as well, because escaping multiplies it (<c>&amp;</c> becomes
    /// <c>&amp;amp;</c>, five characters) while data-model §2.5 caps the PERSISTED Content at
    /// 5000. The sanitized text leaves through <paramref name="sanitized"/> so the caller stores
    /// exactly what was checked — running the sanitizer a second time would double-escape the
    /// entities this pass just wrote.
    /// </summary>
    private static IResult? ContentRefusal(HttpContext context, string? content, out string sanitized)
    {
        sanitized = string.Empty;

        if (string.IsNullOrEmpty(content))
        {
            return Invalid(context, "content obrigatório: informe o texto do post");
        }

        if (content.Length > PostObject.MaxContentLength)
        {
            return Invalid(context, $"content inválido: máximo de {PostObject.MaxContentLength} caracteres");
        }

        sanitized = PostSanitizer.Sanitize(content);
        return sanitized.Length > PostObject.MaxContentLength
            ? Invalid(context,
                $"content inválido: máximo de {PostObject.MaxContentLength} caracteres após a sanitização")
            : null;
    }

    /// <summary>The opaque id of a post/event: 128 random bits, hex, never guessable from content.</summary>
    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// The <c>contentHashes</c> list of the publish body, turned into the <c>Attachment</c> rows
    /// of data-model §2.6 (SPEC-0009 section E, AC-03): at most <see cref="Attachment.MaxPerPost"/>
    /// per post, every hash an upload that exists (400 otherwise), kind derived from the sniffed
    /// media type of that upload, alt and blurHash inherited from its record — and the publish-side
    /// guard of §2.6: an image, video or gif whose alt is empty refuses the publication itself.
    /// The refusal is null when the list stands; nothing has been written when it does not.
    /// </summary>
    private static async Task<(IResult? Refusal, List<Attachment> Attachments)> AttachmentsAsync(
        HttpContext context, IReadOnlyList<string>? contentHashes, IMediaStore media)
    {
        var attachments = new List<Attachment>();
        if (contentHashes is null || contentHashes.Count == 0)
        {
            return (null, attachments);
        }

        if (contentHashes.Count > Attachment.MaxPerPost)
        {
            return (Invalid(context, $"máximo de {Attachment.MaxPerPost} anexos por post"), attachments);
        }

        foreach (var sent in contentHashes)
        {
            var record = await media.FindAsync(sent ?? string.Empty);
            if (record is null)
            {
                return (Invalid(context, "contentHash inexistente: faça o upload da mídia antes de anexá-la"), attachments);
            }

            var kind = Attachment.KindOf(record.MimeType);
            if (Attachment.NeedsAlt(kind) && string.IsNullOrWhiteSpace(record.Alt))
            {
                return (Invalid(context, "alt obrigatório e não vazio para imagem, vídeo e gif (FR-022)"), attachments);
            }

            attachments.Add(new Attachment
            {
                Kind = kind,
                ContentHash = record.ContentHash,
                MimeType = record.MimeType,
                Url = $"{Origin(context)}/api/media/{record.ContentHash}",
                BlurHash = record.BlurHash,
                Alt = record.Alt,
                Width = record.Width,
                Height = record.Height,
                DurationSeconds = record.DurationSeconds,
                SizeBytes = record.SizeBytes,
            });
        }

        return (null, attachments);
    }

    /// <summary>A post id as the trail stores it: the URI when the caller sent one, /o/{id} otherwise.</summary>
    private static string AbsolutePostId(HttpContext context, string id)
    {
        var value = Uri.UnescapeDataString(id ?? string.Empty).Trim();
        return value.Contains("://", StringComparison.Ordinal) ? value : $"{Origin(context)}/o/{value}";
    }

    /// <summary>FR-018/SC-005: an absent post and an invisible one share one body, so existence never leaks.</summary>
    internal static IResult HiddenPost(HttpContext context) =>
        AuthErrors.Write(context, StatusCodes.Status404NotFound,
            "not_found", "post não encontrado.", withTraceId: false);

    private static IResult Invalid(HttpContext context, string message) =>
        AuthErrors.Write(context, StatusCodes.Status400BadRequest, "validation_failed", message);
}
