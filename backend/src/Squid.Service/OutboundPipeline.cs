using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Squid.Api.Containers;
using Squid.Api.Federation;
using Squid.Api.Posts;
using Squid.Domain;
using Squid.Domain.Containers;
using Squid.Domain.Objects;
using Squid.Grains.Actors;
using Squid.Protocols.ActivityPub;

namespace Squid.Service;

/// <summary>
/// The outbound half of SPEC-0011 section F (contract §4, FR-036/037/038, R-05/R-08): fan-out of
/// a publication by visibility, the Delete that follows a local removal, the Follow the local
/// route initiates, and the send itself — which re-evaluates the decision with the policy in
/// force at THAT instant and cancels instead of leaking when it no longer holds (research R-05:
/// cancelar é barato, enviar com política velha não tem desfazer). Enqueue is a local write the
/// publish route awaits before answering (AC-05), the network is never on the request path
/// (FR-036), and every terminal outcome is a row with a reason — no exception reaches a caller
/// (FR-040). The backoff, circuit breaker, DLQ and per-pair rate that DRAIN this queue belong to
/// TASK-0046; here a delivery is enqueued, observed and, when asked, sent exactly once.
/// </summary>
internal sealed class OutboundPipeline : IOutboundPipeline
{
    /// <summary>Contract §4: <c>private</c> never federates in clear (R-11) — not one row.</summary>
    private const VisibilityPolicy NoFederation = VisibilityPolicy.Private;

    private readonly IFollowStore _follows;
    private readonly IContainerStore _containers;
    private readonly IObjectStore _objects;
    private readonly IPeerDeliveryStore _deliveries;
    private readonly ActorGrain _actors;
    private readonly IPeerSender _sender;
    private readonly TimeProvider _time;
    private readonly ILogger<OutboundPipeline> _log;

    /// <summary>Binds the pipeline to the relation, the containers, the objects, the queue, the signer and the wire.</summary>
    public OutboundPipeline(
        IFollowStore follows, IContainerStore containers, IObjectStore objects,
        IPeerDeliveryStore deliveries, ActorGrain actors, IPeerSender sender,
        TimeProvider time, ILogger<OutboundPipeline> log)
    {
        _follows = follows ?? throw new ArgumentNullException(nameof(follows));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public async Task EnqueueCreateAsync(string origin, PostObject post, Container container)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(container);

        if (post.VisibilityAtPublish == NoFederation) return; // §7.3: private não gera nenhuma entrega

        // Every Active membership is ADDRESSED (§2.1: cada ator membro ativo em `to`), while only
        // the remote ones earn a delivery row — a local member already has the post in their feed.
        var members = await ActiveMembersAsync(container.Slug);
        var followers = await RemoteFollowersAsync(origin, post.AuthorActorId);
        IReadOnlyList<string> recipients = post.VisibilityAtPublish switch
        {
            VisibilityPolicy.Unlisted => followers, // §4: seguidores apenas, nunca coleção pública
            VisibilityPolicy.MembersOnly => members.Where(uri => Remote(uri, origin)).ToList(), // §7.2/AC-02
            VisibilityPolicy.Public => [.. followers, .. members.Where(uri => Remote(uri, origin))], // §4: seguidores + coleção do contêiner
            _ => [],
        };
        recipients = recipients.Distinct(StringComparer.Ordinal).ToList();
        if (recipients.Count == 0) return;

        var followersCollection = $"{post.AuthorActorId}/followers";
        var (to, cc) = post.VisibilityAtPublish switch
        {
            VisibilityPolicy.Unlisted => (new[] { followersCollection }, Array.Empty<string>()),
            VisibilityPolicy.MembersOnly => (members.ToArray(), Array.Empty<string>()), // nada em cc (AC-02)
            _ => (new[] { ActivityJsonLd.PublicCollection }, new[] { followersCollection }), // §2.1 (AC-01)
        };

        var now = _time.GetUtcNow();
        var activityId = NewActivityId(origin);
        // GAP-48/AC-01: o payload NOMEIA o destino de lá fora — audience com a URI do contêiner é
        // o que a entrada remota resolve pela D-01, e o objeto já carrega squid:slug.
        var payload = ObjectJsonLd.Create(activityId, post, container, origin, to, cc);
        foreach (var recipient in recipients)
        {
            await _deliveries.AddAsync(new PeerDelivery(
                activityId, recipient, PeerActivity.Create, post.Id, payload,
                container.Policy.Version, 0, now, PeerDeliveryState.Pending, null));
        }
    }

    /// <inheritdoc />
    public async Task EnqueueDeleteAsync(string origin, PostObject post, Container container)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(container);

        // R-08: a quem RECEBEU o Create (ou ia receber) — uma entrega que a política já cancelou
        // nunca chegou e não deve um Delete. Local write only; the signed POST is TASK-0046's clock.
        var creates = await _deliveries.ForObjectAsync(post.Id, PeerActivity.Create);
        var recipients = creates
            .Where(row => row.State != PeerDeliveryState.Cancelled)
            .Select(row => row.RecipientActorId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (recipients.Count == 0) return;

        var now = _time.GetUtcNow();
        foreach (var recipient in recipients)
        {
            var activityId = NewActivityId(origin);
            await _deliveries.AddAsync(new PeerDelivery(
                activityId, recipient, PeerActivity.Delete, post.Id,
                ActivityJsonLd.Delete(activityId, post.AuthorActorId, post.Id),
                container.Policy.Version, 0, now, PeerDeliveryState.Pending, null));
        }
    }

    /// <inheritdoc />
    public async Task EnqueueFollowAsync(string origin, string handle, string targetActorUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetActorUri);

        var now = _time.GetUtcNow();
        var activityId = NewActivityId(origin);
        var actorUri = $"{origin}/users/{handle}";
        await _deliveries.AddAsync(new PeerDelivery(
            activityId, targetActorUri, PeerActivity.Follow, targetActorUri,
            ActivityJsonLd.Follow(activityId, actorUri, targetActorUri),
            0, 0, now, PeerDeliveryState.Pending, null));
        // The edge is born PENDING (Established=false): the Accept of the peer settles it —
        // IFollowStore's own wording, TASK-0044's port adopted here without changing callers.
        await _follows.AddFollowingAsync(handle, targetActorUri, now);
    }

    /// <inheritdoc />
    public async Task<SendVerdict> SendAsync(PeerDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.State != PeerDeliveryState.Pending)
        {
            return new SendVerdict(delivery.State, delivery.Reason); // a resolved row never regresses
        }

        var denial = await DenialAsync(delivery);
        if (denial is not null)
        {
            // R-05/FR-038: negada → Cancelled com motivo auditável, sem erro e sem retry —
            // e o sender nem é consultado, que é justamente o que a AC-04 observa.
            var cancelled = delivery with { State = PeerDeliveryState.Cancelled, Reason = denial };
            await _deliveries.SaveAsync(cancelled);
            FederationMetrics.DeliveryCancelled();
            _log.LogInformation(
                "federated delivery cancelled on re-evaluation ({ActivityType}): {Reason}",
                delivery.Activity, denial);
            return new SendVerdict(PeerDeliveryState.Cancelled, denial);
        }

        // Sign AT SEND: the draft-cavage signature covers host and Date, which only exist now
        // (contract §5) — a queue row freezes the body, never the signature.
        var actorUri = PayloadActor(delivery.Payload);
        ActorSigner? signer;
        try
        {
            signer = await _actors.ActivateSigningKeyAsync(HandleOf(actorUri));
        }
        catch (Exception failure) when (failure is SigningKeyActivationException or ActorNotFoundException)
        {
            // SPEC-0011 section B: a refused activation is auditable and typed — the row stays
            // Pending (fix the key, the next attempt goes), the process never falls (FR-040).
            _log.LogWarning("outbound delivery held: signing key unavailable ({Cause})", failure.Message);
            return new SendVerdict(PeerDeliveryState.Pending, "signing_key_unavailable");
        }

        if (signer is null)
        {
            // No Squid:RecoveryKey at boot: output disabled, the queue keeps registering (§G).
            return new SendVerdict(PeerDeliveryState.Pending, "federation_output_disabled");
        }

        var inboxUri = delivery.RecipientActorId.TrimEnd('/') + "/inbox";
        if (!Uri.TryCreate(inboxUri, UriKind.Absolute, out var inbox)
            || inbox.Scheme is not ("http" or "https"))
        {
            // A recipient that is not an http(s) actor can never answer: permanent, DLQ-worthy.
            var failed = delivery with
            {
                State = PeerDeliveryState.Failed,
                Attempts = delivery.Attempts + 1,
                Reason = "invalid_recipient",
            };
            await _deliveries.SaveAsync(failed);
            return new SendVerdict(failed.State, failed.Reason);
        }

        var signed = HttpSignatureSigner.Sign(
            new SignRequest(
                "post", inbox.PathAndQuery, inbox.Authority, $"{actorUri}#main-key",
                Encoding.UTF8.GetBytes(delivery.Payload), _time.GetUtcNow()),
            signer.Sign);

        var accepted = await _sender.SendAsync(
            inbox.ToString(), signed.Signature, signed.Digest, signed.Date,
            delivery.Payload, cancellationToken);

        // shortcut: sem backoff aqui de propósito — reagendamento, 4xx permanente e DLQ são o
        // motor da TASK-0046; esta task apenas conta a tentativa e guarda o motivo.
        var outcome = accepted
            ? delivery with { State = PeerDeliveryState.Sent, Attempts = delivery.Attempts + 1, Reason = null }
            : delivery with { Attempts = delivery.Attempts + 1, Reason = "send_failed" };
        await _deliveries.SaveAsync(outcome);
        return new SendVerdict(outcome.State, outcome.Reason);
    }

    // ---- decision (R-05: recomputed with the policy in force NOW, never the one at enqueue) ----

    /// <summary>The audit reason a delivery must not go out with, or null when the policy still allows it.</summary>
    private async Task<string?> DenialAsync(PeerDelivery delivery)
    {
        if (delivery.Activity != PeerActivity.Create)
        {
            // Um Follow não tem política de contêiner a reavaliar, and um Delete removido o post
            // DE PROPÓSITO — a ausência do objeto é o motivo dele existir, e o payload carrega
            // nenhum conteúdo a vazar; só o Create reavalia contra a política vigente (R-05).
            return null;
        }

        var post = await _objects.FindAsync(delivery.ObjectId);
        if (post is null)
        {
            return "conteúdo removido: política vigente nega a entrega";
        }

        var container = (await _containers.FindAsync(post.ContainerSlug))?.Container;
        if (container is null || container.IsDeleted)
        {
            return "contêiner removido: política vigente nega a entrega";
        }

        // The visibility that governs the send is the MORE restrictive of the two — the one
        // frozen at publish and the container's current policy (§4 reavaliada; the read side's
        // "may only be more restrictive" rule, data-model §2.5). Enum order IS the ladder:
        // Public < Unlisted < MembersOnly < Private (declaration of VisibilityPolicy).
        var effective = (VisibilityPolicy)Math.Max(
            (int)post.VisibilityAtPublish, (int)container.Policy.Visibility);
        if (effective == NoFederation)
        {
            return $"política vigente (v{container.Policy.Version}) nega a entrega: private";
        }

        if (effective == VisibilityPolicy.MembersOnly
            && !await ActiveMemberAsync(container.Slug, delivery.RecipientActorId))
        {
            return $"política vigente (v{container.Policy.Version}) nega o destinatário: "
                + "membersOnly sem Membership.State = Active";
        }

        return null;
    }

    // ---- recipients (contract §4 table) -----------------------------------------------------

    private async Task<List<string>> RemoteFollowersAsync(string origin, string authorActorId)
    {
        var edges = await _follows.FollowersAsync(HandleOf(authorActorId));
        return edges.Select(edge => edge.ActorUri).Where(uri => Remote(uri, origin)).ToList();
    }

    private async Task<List<string>> ActiveMembersAsync(string slug)
    {
        var members = await _containers.ListMembershipsAsync(slug);
        return members
            .Where(membership => membership.State == MembershipState.Active)
            .Select(membership => membership.ActorId)
            .ToList();
    }

    private async Task<bool> ActiveMemberAsync(string slug, string recipientActorId)
    {
        foreach (var membership in await _containers.ListMembershipsAsync(slug))
        {
            if (membership.State == MembershipState.Active
                && string.Equals(membership.ActorId, recipientActorId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A delivery only ever crosses the wire to another instance — local actors already have the post.</summary>
    private static bool Remote(string actorUri, string origin) =>
        Uri.TryCreate(actorUri, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && !actorUri.StartsWith(origin + "/", StringComparison.Ordinal);

    /// <summary>The local handle inside a local actor URI — both signers and follower documents hang on it.</summary>
    private static string HandleOf(string actorUri) =>
        actorUri[(actorUri.LastIndexOf('/') + 1)..];

    /// <summary>The <c>actor</c> of a payload this instance just built — who signs it (contract §5).</summary>
    private static string PayloadActor(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("actor").GetString() ?? string.Empty;
    }

    /// <summary>The canonical activity URL of this instance (contract §2.1 spelling — minted, never fetched).</summary>
    private static string NewActivityId(string origin) =>
        $"{origin}/activities/{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";
}
