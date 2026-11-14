using System.Security.Cryptography;
using System.Text.Json;
using Squid.Api.Containers;
using Squid.Api.Media;
using Squid.Domain;
using Squid.Domain.Containers;
using Squid.Domain.Objects;

namespace Squid.Api.Posts;

/// <summary>A remote attachment as the pipeline decoded it — the four fields the download and the row need (GAP-44).</summary>
public sealed record RemoteAttachmentSpec(
    string? MediaType,
    string? Url,
    string? Alt,
    string? Sha256);

/// <summary>A remote <c>Create</c> decoded into this instance's own vocabulary (SPEC-0011 section E).</summary>
/// <param name="ObjectId">The remote object URI — it becomes the local row's id, one id per resource.</param>
/// <param name="AuthorActorId">The attributed author, remote URI as sent.</param>
/// <param name="Content">The raw text as the peer spelled it; sanitized once, below, never twice.</param>
/// <param name="Published">The peer's <c>published</c> instant; absent means the instant of arrival.</param>
/// <param name="Visibility">The policy word the payload named, already resolved by the pipeline.</param>
/// <param name="InReplyTo">The parent as received — preserved even when the parent is not a row we hold.</param>
/// <param name="Attachments">The peer's attachments, at most <see cref="Attachment.MaxPerPost"/> of which this write keeps.</param>
public sealed record RemoteCreate(
    string ObjectId,
    string AuthorActorId,
    string? Content,
    string? Published,
    VisibilityPolicy Visibility,
    string? InReplyTo,
    IReadOnlyList<RemoteAttachmentSpec> Attachments);

/// <summary>
/// Where an applied activity becomes state (SPEC-0011 section E): the two writes that must not be
/// forked — a remote <c>Create</c> landing as a row plus its <c>Activity: Create</c> event, and a
/// remote <c>Delete</c> walking the very tombstone path <c>POST /api/posts</c> removals take
/// (PostEndpoints.RemoveContentAsync, so the trail, the event and the projection are one code
/// path). Relations, likes and denunciations are one store call each and live in the pipeline;
/// this class exists for the writes that need this assembly's own helpers.
/// </summary>
public sealed class RemoteActivities
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly IObjectStore _objects;
    private readonly IActivityStore _activities;
    private readonly IContainerStore _containers;
    private readonly IMediaStore _media;
    private readonly IRemoteMediaClient _fetch;
    private readonly IPolicyAuditStore _audit;
    private readonly StreamHub _hub;
    private readonly TimeProvider _time;

    /// <summary>Binds the applier to the stores the local publish path writes with.</summary>
    public RemoteActivities(
        IObjectStore objects, IActivityStore activities, IContainerStore containers,
        IMediaStore media, IRemoteMediaClient fetch, IPolicyAuditStore audit,
        StreamHub hub, TimeProvider time)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// The remote <c>Create</c> as a local row: content through the very sanitizer and cap the
    /// local publish enforces (a payload that does not fit is NOT applied — never a truncation,
    /// never a refusal of the whole delivery: the 202 already stands), the peer's
    /// <c>published</c> as the row's instant, attachments fetched under the SSRF perimeter with
    /// their SHA-256 conferred (GAP-44 — a failure or a mismatch is an absent attachment, never a
    /// rejected post), and the log event carrying it to the projections. False only when the write
    /// itself could not land — a duplicate object id is insert-only and lands nowhere.
    /// </summary>
    public async Task<bool> CreateAsync(
        string origin, Container destination, RemoteCreate create, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);

        if (string.IsNullOrEmpty(create.Content)) return false;
        if (create.Content.Length > PostObject.MaxContentLength) return false;
        var content = PostSanitizer.Sanitize(create.Content);
        if (content.Length == 0 || content.Length > PostObject.MaxContentLength) return false;

        var published = DateTimeOffset.TryParse(create.Published, out var when)
            ? when.ToUniversalTime()
            : _time.GetUtcNow();

        // The parent is preserved exactly as the peer spelled it; the depth it earns is only the
        // one this instance can vouch for — an unreachable parent keeps the link, sits at depth 0
        // (shortcut: chains rooted outside this instance are not walked; the read side decides
        // what to do with a reply whose parent never federated).
        string? replyTo = null;
        var depth = 0;
        if (!string.IsNullOrWhiteSpace(create.InReplyTo))
        {
            replyTo = create.InReplyTo;
            var parent = await _objects.FindAsync(create.InReplyTo);
            if (parent is not null && ReplyDepth.AllowedFor(parent.Depth))
            {
                depth = ReplyDepth.DepthOf(parent.Depth);
            }
        }

        var attachments = await AttachmentsAsync(origin, create, published, cancellationToken);

        var seq = await _objects.NextSeqAsync(destination.Slug);
        var post = new PostObject
        {
            Id = create.ObjectId,
            AuthorActorId = create.AuthorActorId,
            ContainerId = destination.Id,
            ContainerSlug = destination.Slug,
            Kind = ObjectKind.Post,
            Content = content,
            VisibilityAtPublish = create.Visibility,
            PolicyVersion = destination.Policy.Version,
            Seq = seq,
            ReplyToId = replyTo,
            Depth = depth,
            Attachments = attachments,
            CreatedAt = published,
        };

        if (!await _objects.CreateAsync(post))
        {
            return false; // insert only: a repeat of the same object under another activity id lands nowhere
        }

        await _activities.AppendAsync(new ActivityEvent(
            $"urn:squid:activity:{NewToken()}", "Create", create.AuthorActorId, post.Id, 0,
            JsonSerializer.Serialize(PostView.Of(post), PayloadJson), post.CreatedAt));
        _hub.Publish();
        return true;
    }

    /// <summary>
    /// The remote <c>Delete</c> as the tombstone the local removal already writes: only the row's
    /// own author may trigger it (the sender is checked against <c>AuthorActorId</c>, so a foreign
    /// actor can never take a post down), and everything else — trail, event, projection — is
    /// <see cref="PostEndpoints.RemoveContentAsync"/>'s job, shared and never forked.
    /// </summary>
    public async Task<bool> DeleteAsync(string actorUri, string objectId, CancellationToken cancellationToken)
    {
        var post = await _objects.FindAsync(objectId);
        if (post is null || !string.Equals(post.AuthorActorId, actorUri, StringComparison.Ordinal))
        {
            return false; // no such post, or not the author's to remove: state intact, still a 202
        }

        var container = await _containers.FindAsync(post.ContainerSlug);
        return await PostEndpoints.RemoveContentAsync(
            post, actorUri, null, container?.Container.Policy.Version ?? post.PolicyVersion,
            _time.GetUtcNow(), _objects, _activities, _audit);
    }

    /// <summary>
    /// The attachments of one remote Note (GAP-44, data-model §2.6): a kind this instance cannot
    /// store as a blob stays the external link it is; a media kind is downloaded inside the SSRF
    /// perimeter, its SHA-256 computed from the bytes that actually arrived and compared against
    /// the digest the peer declared (a mismatch drops just that attachment), and the blob is saved
    /// content-addressed — a repeat of the same bytes is a no-op. Remote downloads never consult
    /// the quota: it belongs to the local upload route alone (GAP-44).
    /// </summary>
    private async Task<IReadOnlyList<Attachment>> AttachmentsAsync(
        string origin, RemoteCreate create, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var attachments = new List<Attachment>();
        foreach (var spec in create.Attachments)
        {
            if (attachments.Count >= Attachment.MaxPerPost) break;
            if (string.IsNullOrWhiteSpace(spec.Url)) continue;

            var declared = spec.MediaType?.Trim();
            var kind = declared is null ? AttachmentKind.Link : Attachment.KindOf(declared);
            if (kind == AttachmentKind.Link)
            {
                // No blob: the kind is unknown to §2.6 or the peer sent no media type — an external
                // link is kept as the link it is, never guessed into a media kind from a name.
                attachments.Add(new Attachment
                {
                    Kind = AttachmentKind.Link,
                    ContentHash = string.Empty,
                    MimeType = string.IsNullOrEmpty(declared) ? "application/octet-stream" : declared,
                    Url = spec.Url,
                    Alt = spec.Alt ?? string.Empty,
                    SizeBytes = 0,
                });
                continue;
            }

            if (Attachment.NeedsAlt(kind) && string.IsNullOrWhiteSpace(spec.Alt)) continue;

            var bytes = await _fetch.FetchAsync(spec.Url, cancellationToken);
            if (bytes is null || bytes.Length == 0) continue; // download failed: absent, never fatal

            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!string.IsNullOrWhiteSpace(spec.Sha256)
                && !hash.Equals(spec.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue; // the digest the peer declared is not the blob that arrived: refused
            }

            var uploader = create.AuthorActorId.TrimEnd('/').Split('/').LastOrDefault() ?? "federation";
            await _media.SaveAsync(bytes, new MediaRecord(
                hash, declared!, spec.Alt ?? string.Empty,
                null, null, null, null, bytes.LongLength, uploader, now));

            attachments.Add(new Attachment
            {
                Kind = kind,
                ContentHash = hash,
                MimeType = declared!,
                Url = $"{origin}/api/media/{hash}",
                // shortcut: no blurHash is computed for a remote image — §2.6's gate governs the
                // local upload path, and the reader already falls back to its placeholder.
                BlurHash = null,
                Alt = spec.Alt ?? string.Empty,
                SizeBytes = bytes.LongLength,
            });
        }

        return attachments;
    }

    /// <summary>The opaque id of a log event: 128 random bits, hex, never guessable from content.</summary>
    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
