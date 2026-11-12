using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.Domain.Objects;

namespace Squid.Api.Threads;

/// <summary>The create body of contracts/http-api.md §6 (SPEC-0010 inputs): the kind plus the participants.</summary>
public sealed record CreateThreadRequest(string? Kind, IReadOnlyList<string>? ParticipantActorIds);

/// <summary>
/// The body of every POST under a thread (contract §6 + emenda GAP-31): <c>{content}</c> for the
/// message route, <c>{actorId}</c> for adding a participant, <c>{seq}</c> for the read marker of
/// section D. ONE record because the catch-all dispatcher binds a single body and the tail decides
/// which field it reads; the admin and ack routes take no body at all, and an extra field a route
/// never reads is ignored rather than acted on — <c>ReplyToId</c> joined this shape with
/// TASK-0037 and only the message route reads it (SPEC-0010 section E), <c>Emoji</c> with
/// TASK-0038 and only the reaction route reads it (SPEC-0010 section F, <c>{emoji}</c>),
/// <c>ContentHashes</c> with TASK-0039 and only the message route reads it (SPEC-0010 section G:
/// the references of ≤ 10 uploads already living in <c>POST /api/media</c> — GAP-35's emenda (b)).
/// </summary>
public sealed record ThreadPostRequest(
    string? Content, string? ActorId, long? Seq = null, string? ReplyToId = null, string? Emoji = null,
    IReadOnlyList<string>? ContentHashes = null);

/// <summary>
/// SPEC-0010 section A — create and list conversations (FR-030 base, data-model section 2.7 +
/// emenda GAP-30). Session on every route (the 401 of AC-04) and CSRF on the mutation, both
/// already gated by the middleware <c>MapSquidAuth</c> installs before this map runs; the
/// participant counts of the MVP (<c>Direct</c> exactly 2, <c>Group</c> 2..50, the creator always
/// among them and the admin of a group); and a list that only ever answers with the threads the
/// actor participates in, <c>messageSeq</c> born 0 and the actor's own <c>lastReadSeq</c> read from
/// the marker of 0004 (FR-033, GAP-38). The thread id follows the origin rule of the P-03 emenda:
/// scheme + Host of the very request that asked. Section C rides the same catch-all: add/remove
/// participants and the admin seat (emenda GAP-31), with GAP-32's succession enforced by the store
/// inside the removal. Section D rides it too: <c>ack-no-e2ee</c> records the FR-032 acceptance
/// (§9.6's gate — an append without it is 428, decided inside the store so no send path escapes
/// it), <c>read</c> moves the monotonic marker (a smaller value is ignored, above the total is
/// 400 — GAP-38), and a <c>Suspended</c> actor's send is denied and reaches the trail before the
/// 403 (data-model section 3.1, the publish ladder's own rule). Section E rides it as well:
/// <c>POST .../messages</c> takes <c>replyToId</c> of a message of the SAME thread — anything
/// else is <c>422 invalid_reply_target</c> (§9.9) — and the reference never widens the wall: the
/// participant probe above still answers a stranger with the byte-identical 404 (FR-057).
/// Section F rides it as well (FR-058, data-model section 2.14, §9.10): <c>POST
/// .../messages/{messageId}/reactions</c> with <c>{emoji}</c> writes ONE row per (message, actor,
/// emoji) and repeating it changes nothing (AC-01), <c>DELETE ...?emoji=</c> deletes the actor's own
/// row and a row that is not there is the 404 (AC-03), an emoji outside
/// <c>Reactions:AllowedEmojis</c> is <c>422 invalid_emoji</c> (GAP-34's whitelist, read from the
/// configuration on every request so changing the config changes the set — AC-02) BEFORE anything is
/// written, and the count a reader gets is <c>COUNT(*)</c> of the rows that exist now (projection,
/// never source). Neither route is gated by the 428 of §9.6 — section D binds that gate to the
/// SEND of a message —, and neither runs the §3.1 suspension ladder — that rule is about publishing
/// too: reacting writes no content, so the participant wall alone decides, and a stranger gets the
/// byte-identical 404 on GET and on both reaction routes alike (§9.10, AC-04).
/// </summary>
public static class ThreadEndpoints
{
    /// <summary>data-model section 2.7: a Group runs from 2 to 50 participants, creator included.</summary>
    private const int MinGroupParticipants = 2;

    private const int MaxGroupParticipants = 50;

    /// <summary>data-model section 2.5.1: message <c>Content</c> caps at 4000 characters.</summary>
    private const int MaxContentLength = 4000;

    /// <summary>The tail of the message routes.</summary>
    private const string MessagesSuffix = "/messages";

    /// <summary>The tail of <c>POST .../participants</c> (emenda GAP-31).</summary>
    private const string ParticipantsSuffix = "/participants";

    /// <summary>What separates the thread id from the addressed seat — a marker, never a suffix.</summary>
    private const string ParticipantsMarker = "/participants/";

    /// <summary>The tail of <c>POST/DELETE .../participants/{actorId}/admin</c>.</summary>
    private const string AdminSuffix = "/admin";

    /// <summary>The tail of <c>POST .../ack-no-e2ee</c> (SPEC-0010 section D, FR-032, §9.6).</summary>
    private const string AckNoE2eeSuffix = "/ack-no-e2ee";

    /// <summary>The tail of <c>POST .../read</c> (SPEC-0010 section D, FR-033).</summary>
    private const string ReadSuffix = "/read";

    /// <summary>The tail of <c>POST/DELETE .../reactions</c> (SPEC-0010 section F, FR-058).</summary>
    private const string ReactionsSuffix = "/reactions";

    /// <summary>What separates the thread id from the addressed message of <c>.../{id}/messages/{messageId}</c>.</summary>
    private const string MessagesMarker = "/messages/";

    /// <summary>
    /// The notice the acceptance of <c>ack-no-e2ee</c> is recorded against (FR-032: messages have
    /// no end-to-end encryption and the operator can read them) — section D keeps the text in the
    /// ack record itself; the UI that SHOWS it is P-08 (GAP-37).
    /// </summary>
    private const string NoticeNoE2ee =
        "As mensagens não têm criptografia ponta a ponta e são legíveis pelo operador.";

    /// <summary>
    /// The trail word for a send decision, local exactly like <c>PostEndpoints</c>' own words —
    /// the shared constants of <c>IPolicyAuditStore</c> live in the container files.
    /// </summary>
    private const string MessageDecision = "Message";

    /// <summary>Maps the thread routes; runs after <c>MapSquidAuth</c>, on the session pipeline.</summary>
    public static WebApplication MapSquidThreads(this WebApplication app)
    {
        app.MapPost("/api/threads", CreateAsync);
        app.MapGet("/api/threads", ListAsync);
        // A thread id is an opaque URI (scheme + Host + /t/{token}) and cannot sit in one path
        // segment, so every sub-route rides the same catch-all shape /api/posts/{*target} and
        // /api/admin/actors/{*target} already use: each handler reads its tail off the target,
        // plain or percent-encoded, and anything else is the shared 404.
        app.MapPost("/api/threads/{*target}", PostTargetAsync);
        app.MapGet("/api/threads/{*target}", GetTargetAsync);
        app.MapDelete("/api/threads/{*target}", DeleteTargetAsync);
        return app;
    }

    /// <summary>
    /// <c>POST /api/threads</c> (contract §6, SPEC-0010 A): session (401), the kind word, then the
    /// participants — the creator joins automatically and counts toward the range (AC-02's
    /// "criador incluso"), every id arrives the invite's own spelling (GAP-13: a handle or the
    /// canonical actor id ending in it, rewritten to this origin because threads never
    /// federate), duplicates collapse, and <c>Direct</c> = exactly 2 / <c>Group</c> = 2..50 or the
    /// request refuses with 400 before a row is written. Only then the insert: one thread row
    /// (version 1, message_seq 0) plus its participant rows, the creator as <c>admin</c> of a
    /// Group (GAP-30), and the 201 with the id on this request's own scheme+Host.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        CreateThreadRequest? request, HttpContext context, IThreadStore store, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        // No Enum.TryParse: it would accept "1" as a kind. The contract words are the only way in.
        var kind = (request?.Kind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "direct" => "direct",
            "group" => "group",
            _ => null,
        };
        if (kind is null)
        {
            return Invalid(context, "kind inválido: use 'direct' ou 'group'");
        }

        var creator = ContainerEndpoints.ActorId(context, current.Handle);
        var participants = new List<string> { creator };
        foreach (var sent in request?.ParticipantActorIds ?? [])
        {
            var handle = ContainerEndpoints.IdentityOf(null, sent);
            if (handle is null)
            {
                return Invalid(context,
                    "participantActorIds inválido: envie handle ou actorId de um ator local");
            }

            var actorId = ContainerEndpoints.ActorId(context, handle);
            if (!participants.Contains(actorId, StringComparer.Ordinal))
            {
                participants.Add(actorId); // one row per (thread, actor): the creator in the body counts once
            }
        }

        // The range measures the SET the thread gets, creator included (AC-02): Direct is exactly
        // two, Group is 2..50 — anything outside refuses here, before a single row is written.
        var inRange = kind == "direct"
            ? participants.Count == 2
            : participants.Count is >= MinGroupParticipants and <= MaxGroupParticipants;
        if (!inRange)
        {
            return Invalid(context, kind == "direct"
                ? "direct exige exatamente 2 participantes (o criador conta)"
                : "group exige de 2 a 50 participantes (o criador conta)");
        }

        var now = time.GetUtcNow();
        var id = $"{Origin(context)}/t/{NewToken()}";
        var thread = new ThreadState(id, kind, creator, 0, now, 1);
        var rows = participants.Select(actorId => new ThreadParticipant(
            id,
            actorId,
            kind == "group" && actorId == creator ? ThreadRoles.Admin : ThreadRoles.Member,
            now)).ToList();

        await store.CreateAsync(thread, rows);

        // The state version the client now holds (the store's ETag, decimal — containers' rule).
        context.Response.Headers.ETag = "\"1\"";
        return Results.Json(ThreadView.Of(thread, participants, lastReadSeq: 0),
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// <c>GET /api/threads</c> (contract §6, SPEC-0010 A): the actor's own threads only — the
    /// store's join starts from the actor's own participant row, so a conversation the caller is
    /// not in can never appear (FR-031) —, each with <c>messageSeq</c> (0 until a message is
    /// sent) and the actor's own <c>lastReadSeq</c> read from the marker of 0004 (0 until the
    /// actor marks one — FR-033, one value per participant).
    /// </summary>
    private static async Task<IResult> ListAsync(HttpContext context, IThreadStore store)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var actorId = ContainerEndpoints.ActorId(context, current.Handle);
        var items = await store.ListForAsync(actorId);
        return Results.Json(new { items = items.Select(ThreadView.Of).ToList() });
    }

    /// <summary>
    /// The POST tail of <c>/api/threads</c>: <c>{id}/messages</c>, <c>{id}/participants</c> of
    /// contract §6 + emenda GAP-31, <c>{id}/ack-no-e2ee</c> and <c>{id}/read</c> of section D, and
    /// <c>{id}/messages/{messageId}/reactions</c> of section F — any other tail answers the shared
    /// 404 like nothing was ever there (the <c>/api/posts/{*target}</c> shape).
    /// </summary>
    private static async Task<IResult> PostTargetAsync(
        string target, ThreadPostRequest? request, HttpContext context, IThreadStore store,
        IActorStatusStore statuses, IPolicyAuditStore audit, IConfiguration configuration, TimeProvider time)
    {
        var route = ThreadRouteOf(target);
        if (route is null)
        {
            return HiddenThread(context);
        }

        return route.Tail switch
        {
            ThreadTail.Messages => await MessageSendAsync(
                route.Id, request, context, store, statuses, audit, time),
            ThreadTail.AddParticipant => await ParticipantAddAsync(route.Id, request, context, store, time),
            ThreadTail.SetAdmin => await ParticipantAdminAsync(
                route.Id, route.ActorId, promote: true, context, store),
            ThreadTail.AckNoE2ee => await AckNoE2eeAsync(route.Id, context, store, time),
            ThreadTail.Read => await ReadAsync(route.Id, request, context, store),
            ThreadTail.Reactions => await ReactionAddAsync(
                route.Id, route.MessageId!, request, context, store, configuration, time),
            _ => HiddenThread(context),
        };
    }

    /// <summary>
    /// The GET tail of <c>/api/threads</c>: <c>{id}/messages</c> only, anything else the shared 404.
    /// </summary>
    private static async Task<IResult> GetTargetAsync(
        string target, HttpContext context, IThreadStore store)
    {
        var route = ThreadRouteOf(target);
        if (route is null || route.Tail != ThreadTail.Messages)
        {
            return HiddenThread(context);
        }

        return await MessageListAsync(route.Id, context, store);
    }

    /// <summary>
    /// The DELETE tail of <c>/api/threads</c> (emenda GAP-31): <c>{id}/participants/{actorId}</c>,
    /// the demotion half of <c>.../{actorId}/admin</c> and <c>{id}/messages/{messageId}/reactions
    /// ?emoji=</c> of section F — anything else, messages included (contract §6 gives message no
    /// DELETE), the shared 404.
    /// </summary>
    private static async Task<IResult> DeleteTargetAsync(
        string target, HttpContext context, IThreadStore store, IConfiguration configuration)
    {
        var route = ThreadRouteOf(target);
        if (route is null)
        {
            return HiddenThread(context);
        }

        return route.Tail switch
        {
            ThreadTail.RemoveParticipant => await ParticipantRemoveAsync(
                route.Id, route.ActorId!, context, store),
            ThreadTail.SetAdmin => await ParticipantAdminAsync(
                route.Id, route.ActorId!, promote: false, context, store),
            ThreadTail.Reactions => await ReactionRemoveAsync(
                route.Id, route.MessageId!, context, store, configuration),
            _ => HiddenThread(context),
        };
    }

    /// <summary>
    /// <c>POST /api/threads/{id}/messages</c> (contract §6, SPEC-0010 sections B, E and G): session
    /// (401), then the 404 rule of FR-031 — a thread the actor is not in and a thread that does not exist
    /// share one body, decided by the single participant probe before anything else is learned
    /// about the thread (AC-04, SC-005); then data-model §3.1: a <c>Suspended</c> actor does not
    /// send — the refusal reaches the append-only trail BEFORE the 403, the publish ladder's own
    /// order (AC-05, FR-019); then the Content ladder of data-model §2.5.1 (AC-02: empty or above
    /// 4000 is 400, GAP-24's sanitizer runs in between); and only then the write: the append takes
    /// the next seq under the thread's version guard and lands the row in the SAME transaction
    /// (GAP-29) — and inside that same attempt the §9.6 gate answers <c>428</c> when the sender
    /// never recorded the FR-032 acceptance (AC-02; one actor's ack never unlocks another), and
    /// AFTER the gate section E checks <c>replyToId</c> against the thread's own messages, so a
    /// target from another thread or a target that does not exist is
    /// <c>422 invalid_reply_target</c> (§9.9, one answer for both cases) — 428 before 422 because
    /// §6 makes the acceptance absolute for every send. Section G (TASK-0039) rides the same
    /// attempt AFTER those two — precondition first: <c>contentHashes</c> (≤ 10) is validated
    /// inside the transaction against the upload records of <c>POST /api/media</c> — an unknown
    /// hash or an image/video/gif without <c>Alt</c> is <c>400</c> with nothing written (§2.6
    /// applied at publication) — and a standing list lands as the message's §2.6 attachments.
    /// No upload and no quota run here: the 507 stays in <c>POST /api/media</c> (GAP-35), so a
    /// message never charges twice. The answer is 200 + seq — "entregue = persistido" (section D),
    /// not 201.
    /// </summary>
    private static async Task<IResult> MessageSendAsync(
        string id, ThreadPostRequest? request, HttpContext context, IThreadStore store,
        IActorStatusStore statuses, IPolicyAuditStore audit, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var actorId = ContainerEndpoints.ActorId(context, current.Handle);
        if (!await store.IsParticipantAsync(id, actorId))
        {
            return HiddenThread(context);
        }

        // data-model §3.1 (the rule Posts enforces on publish and reply): a suspended actor does
        // not send — the decision is recorded like any other and reaches the trail before the 403.
        if (await statuses.GetAsync(current.Handle) == ActorStatus.Suspended)
        {
            await audit.AppendAsync(new PolicyAuditEntry(
                time.GetUtcNow(), actorId, MessageDecision, id, PolicyAudit.Deny, 0,
                "ator suspenso não envia mensagem (data-model §3.1)"));
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "o ator está suspenso e não pode enviar mensagens (data-model §3.1).");
        }

        var refusal = ContentRefusal(context, request?.Content, out var content);
        if (refusal is not null)
        {
            return refusal;
        }

        var result = await store.AppendMessageAsync(
            id, $"{Origin(context)}/m/{NewToken()}", actorId, content, time.GetUtcNow(),
            request?.ReplyToId, request?.ContentHashes, Origin(context));

        // Five answers of §6/§8/§9.6/§9.9/§2.6: the absent thread keeps FR-031's one body, the missing
        // acceptance keeps the precondition the contract asks for (and, per §6, wins over a reply
        // refusal in the same send), a reply target outside this thread keeps §9.9's word, the
        // attachment refusal of section G is the payload 400 the store decided inside the same
        // attempt, and the stored row is the 200.
        return result.Outcome switch
        {
            AppendOutcome.NoSuchThread => HiddenThread(context), // defensive twin of the probe
            AppendOutcome.AckRequired => AuthErrors.Write(context,
                StatusCodes.Status428PreconditionRequired, "precondition_required",
                "aceite o aviso de não-E2EE antes de enviar (POST /api/threads/{id}/ack-no-e2ee)."),
            AppendOutcome.InvalidReplyTarget => AuthErrors.Write(context,
                StatusCodes.Status422UnprocessableEntity, "invalid_reply_target",
                "replyToId deve apontar para uma mensagem desta mesma thread (FR-057, §9.9)."),
            AppendOutcome.InvalidAttachment => Invalid(context,
                result.Reason ?? "anexo inválido: verifique contentHashes (SPEC-0010 section G)."),
            _ => Results.Json(MessageView.Of(result.Message!)),
        };
    }

    /// <summary>
    /// <c>GET /api/threads/{id}/messages?since=</c> (contract §6/§9.3, SPEC-0010 section B): the
    /// same session and the same byte-identical 404 as the POST (AC-04), then the delta — only
    /// <c>seq &gt; since</c>, no duplicates, ordered by <c>seq</c>, with <c>nextSince</c> so a
    /// polling client (GAP-36) comes back exactly where it stopped: the highest number it was
    /// given, or <c>since</c> unchanged when nothing is new (AC-03). A <c>since</c> that is not a
    /// non-negative integer is a payload this route cannot take (400). The envelope also carries
    /// the READER's own <c>lastReadSeq</c> (section D, FR-033) — read only after the participant
    /// probe, so a non-participant is refused with the 404 that carries no marker at all (AC-04).
    /// Every item also carries its derived <c>reactions</c> of section F (contract §6,
    /// <c>{emoji, count, mine}</c>): COUNT(*) of the rows that exist at this instant, read after
    /// that same probe — a non-participant receives neither a marker nor a count (§9.10, AC-04).
    /// </summary>
    private static async Task<IResult> MessageListAsync(
        string id, HttpContext context, IThreadStore store)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var reader = ContainerEndpoints.ActorId(context, current.Handle);
        if (!await store.IsParticipantAsync(id, reader))
        {
            return HiddenThread(context);
        }

        // Absent or empty = the 0 the contract starts from; anything else has to be the integer
        // the cursor is (StringValues joins a repeated ?since= into one unparsable string → 400).
        var raw = context.Request.Query["since"].ToString();
        long since;
        if (raw.Length == 0)
        {
            since = 0;
        }
        else if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out since))
        {
            return Invalid(context, "since inválido: use um inteiro maior ou igual a zero");
        }

        var items = await store.ListMessagesAsync(id, since);
        // section F rides the SAME delta: the derived projection of every message of this page,
        // assembled from the rows of 0006 at this instant — read only after the participant probe,
        // so a non-participant never receives a count (§9.10, AC-04).
        var reactions = await store.ListReactionsAsync(id, since, reader);
        var byMessage = reactions
            .GroupBy(reaction => reaction.MessageId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ReactionCount>)group.ToList());
        return Results.Json(new
        {
            items = items.Select(message => MessageView.Of(message, byMessage.GetValueOrDefault(message.Id))).ToList(),
            nextSince = items.Count > 0 ? items[^1].Seq : since,
            lastReadSeq = await store.LastReadSeqAsync(id, reader),
        });
    }

    /// <summary>
    /// <c>POST /api/threads/{id}/ack-no-e2ee</c> (contract §6, SPEC-0010 section D, FR-032): the
    /// consent the §9.6 gate reads. Session (401), the FR-031 probe (a thread the actor is not in
    /// and a thread that never existed share the byte-identical 404 — an outsider cannot even
    /// learn whether a notice exists), then the record: one row per (actor, thread) carrying the
    /// notice text itself (section D), inserted with conflict-does-nothing so REPEATING the route
    /// keeps the first acceptance and answers the same 200 (AC-01's idempotence). The gate that
    /// consumes this row lives in the store's append — every send passes through it.
    /// </summary>
    private static async Task<IResult> AckNoE2eeAsync(
        string id, HttpContext context, IThreadStore store, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var actorId = ContainerEndpoints.ActorId(context, current.Handle);
        if (!await store.IsParticipantAsync(id, actorId))
        {
            return HiddenThread(context);
        }

        await store.AckNoE2eeAsync(id, actorId, NoticeNoE2ee, time.GetUtcNow());
        return Results.Json(new { acked = true });
    }

    /// <summary>
    /// <c>POST /api/threads/{id}/read</c> (contract §6, SPEC-0010 section D, FR-033): session
    /// (401), the FR-031 probe on the same snapshot that carries the thread's total, then the
    /// ladder — no <c>seq</c> or a negative one is a payload this route cannot take (400), a seq
    /// above the thread's total is refused by GAP-38's rule (400, nothing written), and only then
    /// the monotonic write: the answer is the marker IN FORCE, so a request that asked for a
    /// smaller value still reads back the high-water mark (the marker only advances). No status
    /// rule ties this route to §3.1 — reading is not publishing.
    /// </summary>
    private static async Task<IResult> ReadAsync(
        string id, ThreadPostRequest? request, HttpContext context, IThreadStore store)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var snapshot = await store.FindAsync(id);
        var reader = ContainerEndpoints.ActorId(context, current.Handle);
        if (snapshot is null || !snapshot.Participants.Any(participant => participant.ActorId == reader))
        {
            return HiddenThread(context);
        }

        var seq = request?.Seq;
        if (seq is null || seq < 0)
        {
            return Invalid(context, "seq obrigatório: informe um inteiro maior ou igual a zero");
        }

        if (seq > snapshot.Thread.MessageSeq)
        {
            return Invalid(context,
                $"seq acima do total da thread ({snapshot.Thread.MessageSeq}): leia só o que existe.");
        }

        return Results.Json(new { lastReadSeq = await store.MarkReadAsync(id, reader, seq.Value) });
    }

    /// <summary>
    /// <c>POST /api/threads/{id}/messages/{messageId}/reactions</c> (contract §6, SPEC-0010 section
    /// F, FR-058 + data-model section 2.14): session (401), then the ONE probe — the caller's seat
    /// AND the message of THIS thread — so an absent thread, a stranger and a message of another
    /// thread share the byte-identical 404 (AC-04, §9.10, SC-005) before any other fact of the
    /// request is learned. Then the instance whitelist (GAP-34): an emoji outside
    /// <c>Reactions:AllowedEmojis</c> is <c>422 invalid_emoji</c> (AC-02), read from the
    /// configuration on EVERY request, which is what makes a changed config change the accepted set.
    /// Only then the write — one row per (message, actor, emoji), a repeat of the SAME triple
    /// answering <c>Repeated</c> with no second row and no count change (AC-01) — and the answer is
    /// the SAME derived projection either way, <c>{reactions: [{emoji, count, mine}]}</c> reconciled
    /// from the rows that exist at this instant (AC-03). No 428 on this path: section D binds that
    /// gate to the SEND of a message ("sem aceite, o ENVIO responde 428"), and no §3.1 ladder
    /// either: that rule refuses a publication, and reacting publishes no content — the participant
    /// wall is the whole authorization.
    /// </summary>
    private static async Task<IResult> ReactionAddAsync(
        string id, string messageId, ThreadPostRequest? request, HttpContext context, IThreadStore store,
        IConfiguration configuration, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var actorId = ContainerEndpoints.ActorId(context, current.Handle);
        if (!await store.MessageVisibleAsync(id, messageId, actorId))
        {
            return HiddenThread(context);
        }

        var emoji = request?.Emoji?.Trim();
        if (!ReactionOptions.From(configuration).Allows(emoji))
        {
            return InvalidEmoji(context);
        }

        var outcome = await store.AddReactionAsync(id, messageId, actorId, emoji!, time.GetUtcNow());
        return outcome == ReactionOutcome.Missing
            ? HiddenThread(context) // defensive twin of the probe: the message left between the two reads
            : await ReactionAnswerAsync(store, id, messageId, actorId);
    }

    /// <summary>
    /// <c>DELETE /api/threads/{id}/messages/{messageId}/reactions?emoji=</c> (contract §6,
    /// SPEC-0010 section F, AC-03): the same session, the same ONE probe and the same whitelist
    /// refusal in the same order (401 → 404 → 422 → effect), because the query value is input like
    /// any other — an emoji this instance does not allow is <c>422 invalid_emoji</c>, never an
    /// effect on a row that could not exist. The delete takes the ACTOR's OWN row for that emoji
    /// only (one row per (message, actor, emoji)), and removing a reaction that does not exist —
    /// never written, already gone, or an emoji of a message of another thread — answers with the
    /// SAME byte-identical 404 as an absent thread (AC-03, AC-04). The answer carries the
    /// projection read AFTER the delete, so the count shown is what the rows still say (AC-03:
    /// derived, never a stored counter).
    /// </summary>
    private static async Task<IResult> ReactionRemoveAsync(
        string id, string messageId, HttpContext context, IThreadStore store, IConfiguration configuration)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var actorId = ContainerEndpoints.ActorId(context, current.Handle);
        if (!await store.MessageVisibleAsync(id, messageId, actorId))
        {
            return HiddenThread(context);
        }

        var emoji = context.Request.Query["emoji"].ToString().Trim();
        if (!ReactionOptions.From(configuration).Allows(emoji))
        {
            return InvalidEmoji(context);
        }

        var outcome = await store.RemoveReactionAsync(id, messageId, actorId, emoji);
        return outcome == ReactionOutcome.Removed
            ? await ReactionAnswerAsync(store, id, messageId, actorId)
            : HiddenThread(context); // nothing to remove: the ONE 404 of the thread routes (AC-03)
    }

    /// <summary>
    /// The body both reaction routes answer with: the message's own projection, reconciled from
    /// <c>message_reaction</c> at this instant (SPEC-0010 section F — counts are read, never stored),
    /// already filtered to rows a participant of this thread may see (the caller probed the wall).
    /// </summary>
    private static async Task<IResult> ReactionAnswerAsync(
        IThreadStore store, string id, string messageId, string actorId)
    {
        var reactions = await store.ReactionsOfAsync(id, messageId, actorId);
        return Results.Json(new { reactions = reactions.Select(ReactionView.Of).ToList() });
    }

    /// <summary>
    /// <c>422 invalid_emoji</c> in the house format (contract §8 family + SPEC-0010 section F):
    /// one word for "outside the whitelist", for an empty value and for an emoji the instance once
    /// allowed and no longer does — the refusal says nothing about which rows exist.
    /// </summary>
    private static IResult InvalidEmoji(HttpContext context) =>
        AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity, "invalid_emoji",
            "emoji fora da whitelist da instância (Reactions:AllowedEmojis, GAP-34).");

    /// <summary>Which sub-route of §6 a catch-all thread target names.</summary>
    private enum ThreadTail
    {
        Messages,
        AddParticipant,
        RemoveParticipant,
        SetAdmin,

        /// <summary><c>POST .../ack-no-e2ee</c> — the FR-032 acceptance §9.6 gates sends with.</summary>
        AckNoE2ee,

        /// <summary><c>POST .../read</c> — the FR-033 monotonic marker.</summary>
        Read,

        /// <summary><c>POST/DELETE .../reactions</c> — the FR-058 rows of data-model section 2.14.</summary>
        Reactions,
    }

    /// <summary>
    /// One parsed target: the thread id, the tail it ended in, the addressed seat (the participant
    /// routes) and the addressed message (the reaction routes of section F).
    /// </summary>
    private sealed record ThreadRoute(string Id, ThreadTail Tail, string? ActorId, string? MessageId = null);

    /// <summary>
    /// The tail off a catch-all target: the id arrives as the caller spelled it — the whole URI
    /// plain or percent-encoded, the same rule the post and admin routes read off theirs — so the
    /// unescape happens once here and the LAST marker decides the route. The <c>/admin</c> suffix
    /// reads as the admin route only while what stays in front of it is a local identity: a handle
    /// of exactly <c>admin</c> (the rule allows one) then keeps spelling a plain removal. The
    /// <c>/participants/</c> marker wins over the section-D tails for the same reason: a seat
    /// named <c>read</c> is still that participant's removal, exactly as it was before those tails
    /// existed. The reaction tail of section F is read FIRST because it is the most specific of
    /// the family: <c>{id}/messages/{messageId}/reactions</c> splits on the LAST <c>/messages/</c>
    /// marker — both ids are whole URIs that carry slashes of their own once unescaped
    /// (<c>/t/{token}</c> and <c>/m/{token}</c>), and neither can contain that marker, so the
    /// split is stable however the caller spelled them. Null is any tail no route of the contract
    /// owns (the shared 404) — including <c>GET .../reactions</c>, which §6 never gives a method.
    /// </summary>
    private static ThreadRoute? ThreadRouteOf(string? target)
    {
        var path = Uri.UnescapeDataString(target ?? string.Empty).Trim().TrimEnd('/');

        if (path.EndsWith(ReactionsSuffix, StringComparison.Ordinal))
        {
            var addressed = path[..^ReactionsSuffix.Length];
            var split = addressed.LastIndexOf(MessagesMarker, StringComparison.Ordinal);
            if (split <= 0)
            {
                return null;
            }

            var id = addressed[..split];
            var messageId = addressed[(split + MessagesMarker.Length)..];
            return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(messageId)
                ? null
                : new ThreadRoute(id, ThreadTail.Reactions, null, messageId);
        }

        if (path.EndsWith(MessagesSuffix, StringComparison.Ordinal))
        {
            var id = path[..^MessagesSuffix.Length];
            return string.IsNullOrWhiteSpace(id) ? null : new ThreadRoute(id, ThreadTail.Messages, null);
        }

        if (path.EndsWith(ParticipantsSuffix, StringComparison.Ordinal))
        {
            var id = path[..^ParticipantsSuffix.Length];
            return string.IsNullOrWhiteSpace(id) ? null : new ThreadRoute(id, ThreadTail.AddParticipant, null);
        }

        var marker = path.LastIndexOf(ParticipantsMarker, StringComparison.Ordinal);
        if (marker > 0)
        {
            var threadId = path[..marker];
            var actor = path[(marker + ParticipantsMarker.Length)..];
            if (actor.Length == 0)
            {
                return null;
            }

            if (actor.EndsWith(AdminSuffix, StringComparison.Ordinal))
            {
                var candidate = actor[..^AdminSuffix.Length];
                if (ContainerEndpoints.IdentityOf(null, candidate) is not null)
                {
                    return new ThreadRoute(threadId, ThreadTail.SetAdmin, candidate);
                }
            }

            return new ThreadRoute(threadId, ThreadTail.RemoveParticipant, actor);
        }

        if (path.EndsWith(AckNoE2eeSuffix, StringComparison.Ordinal))
        {
            var id = path[..^AckNoE2eeSuffix.Length];
            return string.IsNullOrWhiteSpace(id) ? null : new ThreadRoute(id, ThreadTail.AckNoE2ee, null);
        }

        if (path.EndsWith(ReadSuffix, StringComparison.Ordinal))
        {
            var id = path[..^ReadSuffix.Length];
            return string.IsNullOrWhiteSpace(id) ? null : new ThreadRoute(id, ThreadTail.Read, null);
        }

        return null;
    }

    /// <summary>
    /// <c>POST /api/threads/{id}/participants</c> (SPEC-0010 section C, emenda GAP-31, AC-01):
    /// session (401), then the FR-031 probe (a foreign and an absent thread share the byte-identical
    /// 404), then the ladder in the order that keeps every AC reachable — Direct refuses with 400
    /// BEFORE the role does, because nobody in a Direct holds the admin seat and AC-01's
    /// "Direct → 400" would otherwise be unreachable; then 403 for a common participant, the
    /// payload (400), the idempotent re-add (200, nothing written) and the 50-ceiling (400);
    /// only then the insert as <c>member</c> — the creator and GAP-32's succession are the only
    /// doors to <c>admin</c> — answered 201 with the thread as data-model §2.7 carries it.
    /// </summary>
    private static async Task<IResult> ParticipantAddAsync(
        string id, ThreadPostRequest? request, HttpContext context, IThreadStore store, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var snapshot = await store.FindAsync(id);
        var caller = ContainerEndpoints.ActorId(context, current.Handle);
        if (snapshot is null || !snapshot.Participants.Any(participant => participant.ActorId == caller))
        {
            return HiddenThread(context);
        }

        if (!string.Equals(snapshot.Thread.Kind, "group", StringComparison.Ordinal))
        {
            return Invalid(context,
                "Direct não aceita participantes novos: a rota vale só para group (GAP-40).");
        }

        if (!IsAdmin(snapshot, caller))
        {
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "só um admin do grupo pode adicionar participantes.");
        }

        var target = ActorOf(context, request?.ActorId);
        if (target is null)
        {
            return Invalid(context, "actorId inválido: envie handle ou actorId de um ator local");
        }

        var participants = snapshot.Participants.Select(participant => participant.ActorId).ToList();
        if (participants.Contains(target, StringComparer.Ordinal))
        {
            return Results.Json(ThreadView.Of(snapshot.Thread, participants, lastReadSeq: 0));
        }

        if (participants.Count >= MaxGroupParticipants)
        {
            return Invalid(context,
                $"group no teto: máximo de {MaxGroupParticipants} participantes (o criador conta).");
        }

        await store.AddParticipantAsync(
            new ThreadParticipant(id, target, ThreadRoles.Member, time.GetUtcNow()));
        participants.Add(target);
        return Results.Json(ThreadView.Of(snapshot.Thread, participants, lastReadSeq: 0),
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// <c>DELETE /api/threads/{id}/participants/{actorId}</c> (SPEC-0010 section C, AC-02): any
    /// participant takes THEMSELVES out (a common member never removes a third party — that half
    /// belongs to an admin); an admin removes anyone EXCEPT the creator, whose removal by another
    /// admin is 403 because GAP-32 moves that seat only through succession; and the store decides
    /// the rest inside one transaction — the last seat never leaves (<c>409 last_participant</c>,
    /// GAP-38's new code of the §9.11 family) and the creator's own leave carries the succession.
    /// The removed seat loses access the moment the commit lands (every thread route probes the
    /// rows), while the history stays with whoever remains.
    /// </summary>
    private static async Task<IResult> ParticipantRemoveAsync(
        string id, string actor, HttpContext context, IThreadStore store)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var snapshot = await store.FindAsync(id);
        var caller = ContainerEndpoints.ActorId(context, current.Handle);
        if (snapshot is null || !snapshot.Participants.Any(participant => participant.ActorId == caller))
        {
            return HiddenThread(context);
        }

        var target = ActorOf(context, actor);
        if (target is null)
        {
            return Invalid(context, "actorId inválido: envie handle ou actorId de um ator local");
        }

        if (!string.Equals(target, caller, StringComparison.Ordinal))
        {
            if (!IsAdmin(snapshot, caller))
            {
                return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                    "policy_denied", "só um admin do grupo pode remover outros participantes.");
            }

            if (string.Equals(target, snapshot.Thread.CreatedBy, StringComparison.Ordinal))
            {
                return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                    "policy_denied",
                    "o criador da thread não sai por mão de outro admin: saia você mesmo (GAP-32).");
            }
        }

        var removal = await store.RemoveParticipantAsync(id, target);
        return removal switch
        {
            ParticipantRemoval.Removed => Results.Json(new { actorId = target }),
            ParticipantRemoval.LastParticipant => AuthErrors.Write(context,
                StatusCodes.Status409Conflict, "last_participant",
                "o último participante não sai: a thread precisa de ao menos um."),
            _ => HiddenThread(context),
        };
    }

    /// <summary>
    /// <c>POST/DELETE /api/threads/{id}/participants/{actorId}/admin</c> (SPEC-0010 section C,
    /// emenda GAP-31, AC-03): session, the FR-031 probe, the payload — then ANY admin promotes or
    /// demotes, admins elected by another admin included (admins are elected by admins); the
    /// CREATOR's seat never changes here, by a third party or by themselves, because the creator
    /// is the admin a Group is born with and GAP-32's succession is the only road out of that
    /// seat (403 both ways, keeping the invariant: a Group with participants keeps an admin).
    /// Promoting an already-admin seat and demoting a plain member are no-ops, not errors.
    /// </summary>
    private static async Task<IResult> ParticipantAdminAsync(
        string id, string? actor, bool promote, HttpContext context, IThreadStore store)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var snapshot = await store.FindAsync(id);
        var caller = ContainerEndpoints.ActorId(context, current.Handle);
        if (snapshot is null || !snapshot.Participants.Any(participant => participant.ActorId == caller))
        {
            return HiddenThread(context);
        }

        var target = ActorOf(context, actor);
        if (target is null)
        {
            return Invalid(context, "actorId inválido: envie handle ou actorId de um ator local");
        }

        if (!IsAdmin(snapshot, caller))
        {
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied", "só um admin do grupo pode mudar papéis de participante.");
        }

        if (!promote && string.Equals(target, snapshot.Thread.CreatedBy, StringComparison.Ordinal))
        {
            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                "policy_denied",
                "o papel do criador não muda por aqui: a sucessão acontece só quando ele sai (GAP-32).");
        }

        var role = promote ? ThreadRoles.Admin : ThreadRoles.Member;
        return await store.SetRoleAsync(id, target, role)
            ? Results.Json(new { actorId = target, role })
            : HiddenThread(context); // defensive twin of the probe: the seat is not there (SC-005's body)
    }

    /// <summary>Whether the snapshot carries the admin seat for this actor (GAP-30, one role per participant).</summary>
    private static bool IsAdmin(ThreadSnapshot snapshot, string actorId) =>
        snapshot.Participants.Any(participant =>
            participant.ActorId == actorId && participant.Role == ThreadRoles.Admin);

    /// <summary>
    /// One local identity from a body or a path segment: the handle as sent, or the handle its
    /// canonical actor id ends in, rewritten to this origin — the rule <c>CreateAsync</c> already
    /// follows (GAP-13). Null when neither, so a malformed value never addresses a row.
    /// </summary>
    private static string? ActorOf(HttpContext context, string? value)
    {
        var handle = ContainerEndpoints.IdentityOf(null, value);
        return handle is null ? null : ContainerEndpoints.ActorId(context, handle);
    }

    /// <summary>
    /// The Content ladder of data-model §2.5.1 (AC-02), the posts' own ladder with this task's
    /// cap: empty refuses before anything is written, the raw text has to fit 4000, then GAP-24's
    /// sanitization runs and the SANITIZED text has to fit as well — escaping multiplies the text
    /// (<c>&amp;</c> becomes <c>&amp;amp;</c>, five characters) while §2.5.1 caps what is PERSISTED,
    /// and a text that sanitizes to nothing (controls only) is empty too. The checked text leaves
    /// through <paramref name="sanitized"/> so the caller stores exactly what was validated —
    /// running the sanitizer a second time would double-escape the entities this pass just wrote.
    /// </summary>
    private static IResult? ContentRefusal(HttpContext context, string? content, out string sanitized)
    {
        sanitized = string.Empty;

        if (string.IsNullOrEmpty(content))
        {
            return Invalid(context, "content obrigatório: informe o texto da mensagem");
        }

        if (content.Length > MaxContentLength)
        {
            return Invalid(context, $"content inválido: máximo de {MaxContentLength} caracteres");
        }

        sanitized = PostSanitizer.Sanitize(content);
        if (string.IsNullOrEmpty(sanitized))
        {
            return Invalid(context, "content obrigatório: informe o texto da mensagem");
        }

        return sanitized.Length > MaxContentLength
            ? Invalid(context,
                $"content inválido: máximo de {MaxContentLength} caracteres após a sanitização")
            : null;
    }

    /// <summary>
    /// The identity origin of P-02/P-03: scheme + Host of the very request that asked — the same
    /// rule as <c>ContainerEndpoints</c> and <c>PostEndpoints</c>, never a configured domain
    /// (one resource, one URI, whether the instance is reached over http or https).
    /// </summary>
    private static string Origin(HttpContext context) =>
        $"{context.Request.Scheme}://{context.Request.Host.Value}";

    /// <summary>The opaque id of a thread: 128 random bits, hex — the <c>/t/{id}</c> tail.</summary>
    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// FR-031/SC-005 (AC-04): absent thread and foreign thread share ONE body — no traceId, so
    /// the byte-identical pair holds across GET and POST and neither existence nor the participant
    /// list can ever leak out of the refusal.
    /// </summary>
    private static IResult HiddenThread(HttpContext context) =>
        AuthErrors.Write(context, StatusCodes.Status404NotFound,
            "not_found", "thread não encontrada.", withTraceId: false);

    private static IResult Invalid(HttpContext context, string message) =>
        AuthErrors.Write(context, StatusCodes.Status400BadRequest, "validation_failed", message);
}
