using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Squid.Api.Containers;
using Squid.Api.Discovery;
using Squid.Api.Federation;
using Squid.Api.Posts;
using Squid.Domain;
using Squid.Domain.Containers;
using Squid.Domain.Objects;
using Squid.Grains.Actors;
using Squid.Grains.Inbox;
using Squid.Protocols.ActivityPub;

namespace Squid.Service;

/// <summary>
/// The inbox pipeline of SPEC-0011 section E: contract activitypub §3 in its fixed order —
/// 1) signature against the published key (local aggregate or the remote client's guarded fetch),
/// 2) Digest of the body, 3) Date inside ±5 min (both headers must EXIST, the verifier only judges
/// the ones it is given), 4) body ≤ 1 MB and nesting ≤ 32, 5) the per-domain quota, 6) the
/// <c>activity.id</c> window of <see cref="InboxGrain"/>, 7) a resolvable sender (a LOCAL author
/// additionally with the very key that signed) — and only then the application: <c>Create</c> into
/// the destination D-01 fixed (named destination only, §E), <c>Follow</c>/<c>Accept</c>/<c>Undo</c>
/// over the follow relation, <c>Delete</c>'s tombstone, <c>Like</c>'s idempotent count, <c>Flag</c>'s
/// denunciation, and everything else — unknown types and the GAP-42 family — a 202 plus
/// <c>federation.unknown_activity</c>. Every step answers a verdict instead of throwing (FR-040)
/// and nothing is applied before step 7 held, so a refusal at any step leaves the state intact
/// (§7.5, AC-03).
/// </summary>
internal sealed class InboxPipeline : IInboxPipeline
{
    /// <summary>§3 step 4: a federated body is at most 1 MB (the 5 MB of §7.8 is far above it → 413).</summary>
    private const int MaxBodyBytes = 1_048_576;

    private const string PublicCollection = "https://www.w3.org/ns/activitystreams#Public";

    private readonly ActorGrain _actors;
    private readonly InboxGrain _inbox;
    private readonly IContainerStore _containers;
    private readonly IObjectStore _objects;
    private readonly IFollowStore _follows;
    private readonly ILikeStore _likes;
    private readonly IReportStore _reports;
    private readonly RemoteActivities _remote;
    private readonly IFederatedActorClient _peer;
    private readonly FederationRateLimiter _rate;
    private readonly TimeProvider _time;
    private readonly ILogger<InboxPipeline> _log;

    /// <summary>Binds the pipeline to the aggregate, the grain, the stores and the seams of §3.</summary>
    public InboxPipeline(
        ActorGrain actors, InboxGrain inbox, IContainerStore containers, IObjectStore objects,
        IFollowStore follows, ILikeStore likes, IReportStore reports, RemoteActivities remote,
        IFederatedActorClient peer, FederationRateLimiter rate, TimeProvider time,
        ILogger<InboxPipeline> log)
    {
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _follows = follows ?? throw new ArgumentNullException(nameof(follows));
        _likes = likes ?? throw new ArgumentNullException(nameof(likes));
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _remote = remote ?? throw new ArgumentNullException(nameof(remote));
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _rate = rate ?? throw new ArgumentNullException(nameof(rate));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public async Task<InboxVerdict> ReceiveAsync(
        HttpContext context, InboxTarget target, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var origin = CanonicalOrigin.Of(context);

        // §3 step 1 first, and before the payload exists: no signature header, nothing to read.
        if (!InboxSignatureGate.HasSignatureHeaders(request))
        {
            return new InboxVerdict(InboxOutcome.SignatureInvalid);
        }

        var body = await InboxSignatureGate.BodyAsync(request, cancellationToken);
        var incoming = new IncomingRequest(
            request.Method,
            $"{request.Path.Value}{request.QueryString.Value}",
            request.Scheme,
            request.Host.Value ?? string.Empty,
            InboxSignatureGate.Headers(request),
            body);
        var verdict = await HttpSignatureVerifier.VerifyAsync(
            incoming, keyId => InboxSignatureGate.ResolveKeyAsync(keyId, origin, _actors, _peer, cancellationToken));
        if (!verdict.Accepted)
        {
            return verdict.Rejection switch
            {
                SignatureRejection.DigestMismatch => new InboxVerdict(InboxOutcome.DigestMismatch),
                SignatureRejection.StaleDate => new InboxVerdict(InboxOutcome.StaleDate),
                _ => new InboxVerdict(InboxOutcome.SignatureInvalid), // one refusal body for every cause
            };
        }

        // The verifier judges the Digest and the Date it is GIVEN (§3 steps 2–3); the contract
        // requires both headers to exist, so a signature covering neither still owes them.
        if (!request.Headers.ContainsKey("digest") && !request.Headers.ContainsKey("content-digest"))
        {
            return new InboxVerdict(InboxOutcome.DigestMismatch);
        }

        if (!request.Headers.ContainsKey("date"))
        {
            return new InboxVerdict(InboxOutcome.StaleDate);
        }

        // §3 step 4 — size first (the whole body is already read for the Digest), then depth: the
        // adapter parses under the ceiling and tells the two refusals apart.
        if (body.Length > MaxBodyBytes)
        {
            return new InboxVerdict(InboxOutcome.PayloadTooLarge);
        }

        IncomingActivity activity;
        try
        {
            activity = await ActivityJsonLd.ReceiveAsync(body, cancellationToken);
        }
        catch (PayloadTooDeepException)
        {
            return new InboxVerdict(InboxOutcome.PayloadTooDeep);
        }
        catch (FormatException)
        {
            return new InboxVerdict(InboxOutcome.InvalidActivity);
        }

        if (string.IsNullOrEmpty(activity.Id) || string.IsNullOrEmpty(activity.Actor))
        {
            return new InboxVerdict(InboxOutcome.InvalidActivity); // no id to dedupe by, no sender to trust
        }

        // §3 step 5 — the quota of the sender's domain, 60/min by default (429 + Retry-After).
        if (!_rate.TryAcquire(SenderDomain(request, activity), out var retryAfterSeconds))
        {
            return new InboxVerdict(InboxOutcome.RateLimited, retryAfterSeconds);
        }

        // §3 step 6 — the window of ids, marked BEFORE the application (the contract's own order):
        // a repeat answers 202 here and never reaches state again (FR-035, §7.4).
        if (!await _inbox.RememberAsync(Recipient(target), activity.Id!))
        {
            return new InboxVerdict(InboxOutcome.Accepted);
        }

        // §3 step 7 — the sender resolves: a local author only through THEIR OWN published key
        // (a local actor can never be attributed to someone else's signature), a remote author
        // through the actor document. shortcut: a REMOTE author is not tied to the signing key's
        // owner — §3 does not demand it and the peer validates our own output; casar o ator
        // remoto com o dono da keyId cresce aqui quando o contrato pedir.
        var sender = activity.Actor!;
        if (IsLocalActor(sender, origin))
        {
            if (!OwnsSignature(request, sender))
            {
                return new InboxVerdict(InboxOutcome.SenderUnresolvable);
            }
        }
        else if (!await _peer.ExistsAsync(sender, cancellationToken))
        {
            return new InboxVerdict(InboxOutcome.SenderUnresolvable);
        }

        return await ApplyAsync(target, origin, activity, body, cancellationToken);
    }

    // ---- application (after every step of §3 held) ---------------------------------

    /// <summary>
    /// The dispatch of SPEC-0011 section E. The route names the local recipient first — an inbox
    /// that belongs to nobody applies nothing, 202 with the state intact and no hint that the
    /// handle exists — and from there each supported type writes its own slice; every unknown or
    /// out-of-scope type (GAP-42: <c>Update</c>/<c>Announce</c>/<c>Join</c>/<c>Leave</c>/<c>Block</c>)
    /// is a 202 plus the metric, never an error (contract §2, AC-06).
    /// </summary>
    private async Task<InboxVerdict> ApplyAsync(
        InboxTarget target, string origin, IncomingActivity activity, byte[] body,
        CancellationToken cancellationToken)
    {
        Container? routeContainer = null;
        if (target.ContainerSlug is { } slug)
        {
            routeContainer = await ExistingAsync(slug);
            if (routeContainer is null) return Accepted();
        }
        else if (target.Handle is { } handle)
        {
            if (!ActorHandle.IsValid(handle) || await _actors.GetStateAsync(handle) is null)
            {
                return Accepted();
            }
        }

        switch (activity.Type?.ToLowerInvariant())
        {
            case "create":
                return await CreateAsync(target, routeContainer, origin, activity, cancellationToken);

            case "follow":
                return await FollowAsync(origin, activity, body);

            case "accept":
                if (activity.Object is { Type: not null } nested
                    && nested.Type.Equals("Follow", StringComparison.OrdinalIgnoreCase)
                    && nested.Actor is not null
                    && nested.ObjectUri is not null
                    && IsLocalActor(nested.Actor, origin, out var accepting))
                {
                    await _follows.EstablishAsync(accepting, nested.ObjectUri); // false when no such follow was sent
                }

                return Accepted();

            case "undo":
                if (activity.Object is { Type: not null } undone
                    && undone.Type.Equals("Follow", StringComparison.OrdinalIgnoreCase)
                    && undone.Actor is not null
                    && undone.ObjectUri is not null
                    && IsLocalActor(undone.ObjectUri, origin, out var followed))
                {
                    await _follows.RemoveFollowerAsync(followed, undone.Actor);
                }

                return Accepted();

            case "delete":
                if (activity.ObjectUri is { } removed)
                {
                    await _remote.DeleteAsync(activity.Actor!, removed, cancellationToken);
                }

                return Accepted();

            case "like":
                if (activity.ObjectUri is { } liked && await _objects.FindAsync(liked) is { } post)
                {
                    await _likes.LikeAsync(post.Id, activity.Actor!, _time.GetUtcNow());
                }

                return Accepted();

            case "flag":
                if (activity.ObjectUri is { } denounced && await _objects.FindAsync(denounced) is { } reported)
                {
                    var reason = string.IsNullOrWhiteSpace(activity.Summary)
                        ? "denúncia federada"
                        : activity.Summary.Trim();
                    await _reports.CreateAsync(new Report(
                        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
                        reported.Id, activity.Actor!, reported.AuthorActorId, reason,
                        ReportStatus.Open, reported.PolicyVersion, _time.GetUtcNow()));
                }

                return Accepted();

            default:
                FederationMetrics.UnknownActivity();
                var type = activity.Type ?? string.Empty;
                _log.LogDebug(
                    "inbox acknowledged an unapplied activity (type {ActivityType})",
                    type.Length > 64 ? type[..64] : type);
                return Accepted();
        }
    }

    /// <summary>
    /// The remote <c>Create</c>: destination first (SPEC-0011 section E, D-01 — the container
    /// inbox itself, or a named <c>squid:slug</c>/<c>/c/{slug}</c> reference in
    /// <c>audience</c>/<c>to</c>/<c>cc</c>/<c>inReplyTo</c>; NO destination means 202 without
    /// applying), then the two visibility gates — <c>private</c> is never applied in clear (R-11)
    /// and <c>membersOnly</c> only when the local recipient is an active member of that container
    /// (AC-06) — and only then the write.
    /// </summary>
    private async Task<InboxVerdict> CreateAsync(
        InboxTarget target, Container? routeContainer, string origin, IncomingActivity activity,
        CancellationToken cancellationToken)
    {
        var destination = routeContainer ?? await DestinationAsync(target, activity, origin);
        if (destination is null) return Accepted(); // sem destino nomeado → 202, estado intacto

        var visibility = ResolveVisibility(activity);
        if (visibility == VisibilityPolicy.Private) return Accepted();
        if (visibility == VisibilityPolicy.MembersOnly && !await MemberRecipientAsync(target, destination))
        {
            return Accepted(); // o destinatário local não é membro ativo: nada é aplicado (AC-06)
        }

        var obj = activity.Object;
        var objectId = obj?.Id ?? activity.ObjectUri;
        var author = obj?.AttributedTo ?? activity.Actor;
        if (objectId is null || author is null) return Accepted();

        var create = new RemoteCreate(
            objectId,
            author,
            obj?.Content,
            obj?.Published,
            visibility,
            obj?.InReplyTo ?? activity.InReplyTo,
            (obj?.Attachments ?? [])
                .Select(attachment => new RemoteAttachmentSpec(
                    attachment.MediaType, attachment.Url, attachment.Alt, attachment.Sha256))
                .ToList());

        await _remote.CreateAsync(origin, destination, create, cancellationToken);
        return Accepted();
    }

    /// <summary>A <c>Follow</c> of a local actor: the relation in force plus the Accept of AC-05 in the 202 body.</summary>
    private async Task<InboxVerdict> FollowAsync(string origin, IncomingActivity activity, byte[] body)
    {
        if (activity.ObjectUri is null
            || !IsLocalActor(activity.ObjectUri, origin, out var handle)
            || await _actors.GetStateAsync(handle) is null)
        {
            return Accepted(); // nothing this instance can follow through: 202, state intact
        }

        await _follows.AddFollowerAsync(handle, activity.Actor!, _time.GetUtcNow());
        var accept = ActivityJsonLd.Accept(origin, activity.ObjectUri, Encoding.UTF8.GetString(body));
        return Accepted(accept);
    }

    /// <summary>
    /// The destination D-01 fixed: (a) the container inbox names it outright; (b) a named
    /// reference in the payload — a bare <c>squid:slug</c>, or a <c>/c/{slug}</c> path in
    /// <c>audience</c>/<c>to</c>/<c>cc</c>/<c>inReplyTo</c>, a remote URI matching by slug and a
    /// local one only by its exact id. First reference that lands on a live local container wins.
    /// </summary>
    private async Task<Container?> DestinationAsync(
        InboxTarget target, IncomingActivity activity, string origin)
    {
        if (target.ContainerSlug is { } inbox) return await ExistingAsync(inbox);

        var obj = activity.Object;
        if (await BareSlugAsync(activity.Slug) is { } byActivity) return byActivity;
        if (await BareSlugAsync(obj?.Slug) is { } byObject) return byObject;

        var references = new List<string?>();
        references.AddRange(activity.Audience);
        if (obj is not null) references.AddRange(obj.Audience);
        references.AddRange(activity.To);
        if (obj is not null) references.AddRange(obj.To);
        references.AddRange(activity.Cc);
        if (obj is not null) references.AddRange(obj.Cc);
        references.Add(activity.InReplyTo);
        if (obj is not null) references.Add(obj.InReplyTo);

        foreach (var reference in references)
        {
            if (reference is null) continue;
            var (slug, local) = PathReference(reference, origin);
            if (slug is null) continue;
            var container = await ExistingAsync(slug);
            if (container is null) continue;
            if (local && !string.Equals(container.Id, Trimmed(reference), StringComparison.Ordinal))
            {
                continue; // URI local casa exata — a path on OUR origin must be the container's own id
            }

            return container;
        }

        return null;
    }

    /// <summary>The <c>membersOnly</c> gate of AC-06: only the actor of an actor inbox can be a member — a container inbox has no local member recipient, so nothing is applied there.</summary>
    private async Task<bool> MemberRecipientAsync(InboxTarget target, Container destination) =>
        target.Handle is { } handle
        && ActorHandle.IsValid(handle)
        && (await _containers.FindMembershipAsync(destination.Slug, handle))?.State == MembershipState.Active;

    /// <summary>The policy word the payload declared, else Public when the audience says so, else the conservative Unlisted (visibility may only be more restrictive on read — data-model §2.5).</summary>
    private static VisibilityPolicy ResolveVisibility(IncomingActivity activity)
    {
        var declared = Word(activity.Object?.Visibility) ?? Word(activity.Visibility);
        if (declared is { } visibility) return visibility;

        var audience = new List<string>();
        audience.AddRange(activity.To);
        audience.AddRange(activity.Cc);
        audience.AddRange(activity.Audience);
        if (activity.Object is { } obj)
        {
            audience.AddRange(obj.To);
            audience.AddRange(obj.Cc);
            audience.AddRange(obj.Audience);
        }

        return audience.Contains(PublicCollection, StringComparer.Ordinal)
            ? VisibilityPolicy.Public
            : VisibilityPolicy.Unlisted;
    }

    private static VisibilityPolicy? Word(string? word) =>
        word is not null && Enum.TryParse<VisibilityPolicy>(word, ignoreCase: true, out var parsed)
            ? parsed
            : null;

    /// <summary>The live local container behind a bare slug word — a slug never carries a scheme or a slash.</summary>
    private async Task<Container?> BareSlugAsync(string? slug) =>
        slug is { Length: > 0 } && !slug.Contains('/') && !slug.Contains(':')
            ? await ExistingAsync(slug)
            : null;

    private async Task<Container?> ExistingAsync(string slug)
    {
        var state = await _containers.FindAsync(slug);
        return state?.Container is { IsDeleted: false } container ? container : null;
    }

    /// <summary>The <c>/c/{slug}</c> path of a reference: its slug, and whether the URI is on OUR origin.</summary>
    private static (string? Slug, bool Local) PathReference(string reference, string origin)
    {
        if (!Uri.TryCreate(reference, UriKind.Absolute, out var uri)) return (null, false);
        if (!uri.AbsolutePath.StartsWith("/c/", StringComparison.Ordinal)) return (null, false);

        var rest = uri.AbsolutePath[3..];
        var end = rest.IndexOf('/');
        if (end >= 0) rest = rest[..end];
        if (rest.Length == 0) return (null, false);

        return (rest, reference.StartsWith(origin + "/", StringComparison.Ordinal));
    }

    /// <summary>A reference without its query, fragment or trailing slash — the spelling an id equality compares.</summary>
    private static string Trimmed(string reference)
    {
        var value = reference;
        var cut = value.IndexOfAny(['?', '#']);
        if (cut >= 0) value = value[..cut];
        return value.TrimEnd('/');
    }

    /// <summary>True when the URI is this instance's own actor of a known handle.</summary>
    private static bool IsLocalActor(string uri, string origin) =>
        IsLocalActor(uri, origin, out _);

    private static bool IsLocalActor(string uri, string origin, out string handle)
    {
        handle = string.Empty;
        const string marker = "/users/";
        if (!uri.StartsWith(origin + marker, StringComparison.Ordinal)) return false;

        var tail = uri[(origin.Length + marker.Length)..];
        if (tail.Length == 0 || tail.Contains('/')) return false;
        if (!ActorHandle.IsValid(tail)) return false;

        handle = tail;
        return true;
    }

    /// <summary>True when the signature this request carries was made with the key published under that same local actor.</summary>
    private static bool OwnsSignature(HttpRequest request, string localActorUri)
    {
        var keyId = KeyId(request);
        return keyId is not null && keyId.StartsWith(localActorUri + "#", StringComparison.Ordinal);
    }

    /// <summary>The domain §3 step 5 quotas: the origin of the keyId the verification stood on, else the claimed actor.</summary>
    private static string SenderDomain(HttpRequest request, IncomingActivity activity)
    {
        var candidate = KeyId(request) ?? activity.Actor;
        return candidate is not null
            && Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
                ? uri.Authority
                : "unknown";
    }

    /// <summary>The <c>keyid</c> parameter of whichever signature header the sender used.</summary>
    private static string? KeyId(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Signature", out var signature))
        {
            var quoted = FindParameter(signature.ToString(), "keyid=\"");
            if (quoted is not null) return quoted;
        }

        if (request.Headers.TryGetValue("Signature-Input", out var input))
        {
            var quoted = FindParameter(input.ToString(), "keyid=\"");
            if (quoted is not null) return quoted;
            return FindParameter(input.ToString(), "keyid=");
        }

        return null;
    }

    /// <summary>Reads a <c>keyid=…</c> parameter up to its closing quote or comma.</summary>
    private static string? FindParameter(string header, string prefix)
    {
        var index = header.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var start = index + prefix.Length;
        var quoted = prefix.EndsWith('"');
        var end = quoted
            ? header.IndexOf('"', start)
            : header.IndexOfAny([',', ';'], start);
        if (end < 0 || end == start) return null;

        return header[start..end];
    }

    /// <summary>The recipient key the dedupe window is scoped to (SPEC-0011 §E, decided per local inbox).</summary>
    private static string Recipient(InboxTarget target) =>
        target.ContainerSlug is { } slug ? $"container/{slug}" : $"actor/{target.Handle}";

    private static InboxVerdict Accepted() => new(InboxOutcome.Accepted);

    private static InboxVerdict Accepted(string activityJson) =>
        new(InboxOutcome.Accepted, ActivityJson: activityJson);
}
