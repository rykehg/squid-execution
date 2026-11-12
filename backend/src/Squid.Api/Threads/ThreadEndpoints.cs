using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Domain.Objects;

namespace Squid.Api.Threads;

/// <summary>The create body of contracts/http-api.md §6 (SPEC-0010 inputs): the kind plus the participants.</summary>
public sealed record CreateThreadRequest(string? Kind, IReadOnlyList<string>? ParticipantActorIds);

/// <summary>
/// The send body of contracts/http-api.md §6: the text alone. <c>replyToId</c> is deliberately not
/// bound — it joins the body with TASK-0037, and an extra JSON field a route never reads is
/// ignored rather than acted on.
/// </summary>
public sealed record SendMessageRequest(string? Content);

/// <summary>
/// SPEC-0010 section A — create and list conversations (FR-030 base, data-model section 2.7 +
/// emenda GAP-30). Session on every route (the 401 of AC-04) and CSRF on the mutation, both
/// already gated by the middleware <c>MapSquidAuth</c> installs before this map runs; the
/// participant counts of the MVP (<c>Direct</c> exactly 2, <c>Group</c> 2..50, the creator always
/// among them and the admin of a group); and a list that only ever answers with the threads the
/// actor participates in, <c>messageSeq</c> born 0 and the actor's own <c>lastReadSeq</c> (GAP-38,
/// born 0 until TASK-0036's monotonic marker lands). The thread id follows the origin rule of the
/// P-03 emenda: scheme + Host of the very request that asked.
/// </summary>
public static class ThreadEndpoints
{
    /// <summary>GAP-30: the two roles a <c>thread_participant</c> row can carry.</summary>
    private const string AdminRole = "admin";

    private const string MemberRole = "member";

    /// <summary>data-model section 2.7: a Group runs from 2 to 50 participants, creator included.</summary>
    private const int MinGroupParticipants = 2;

    private const int MaxGroupParticipants = 50;

    /// <summary>data-model section 2.5.1: message <c>Content</c> caps at 4000 characters.</summary>
    private const int MaxContentLength = 4000;

    /// <summary>The tail of the message routes — the only sub-route of a thread in this delivery.</summary>
    private const string MessagesSuffix = "/messages";

    /// <summary>Maps the thread routes; runs after <c>MapSquidAuth</c>, on the session pipeline.</summary>
    public static WebApplication MapSquidThreads(this WebApplication app)
    {
        app.MapPost("/api/threads", CreateAsync);
        app.MapGet("/api/threads", ListAsync);
        // A thread id is an opaque URI (scheme + Host + /t/{token}) and cannot sit in one path
        // segment, so the message routes ride the same catch-all shape /api/posts/{*target} and
        // /api/admin/actors/{*target} already use: the handler reads the {id}/messages tail off
        // the target, plain or percent-encoded, and anything else is the shared 404.
        app.MapPost("/api/threads/{*target}", MessageSendTargetAsync);
        app.MapGet("/api/threads/{*target}", MessageListTargetAsync);
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
            kind == "group" && actorId == creator ? AdminRole : MemberRole,
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
    /// sent) and the actor's own <c>lastReadSeq</c> (0 until TASK-0036).
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
    /// The POST tail of <c>/api/threads</c>: the contract has <c>{id}/messages</c> under a thread
    /// in this delivery — any other tail answers the shared 404 like nothing was ever there (the
    /// <c>/api/posts/{*target}</c> shape; TASK-0035+ extend this dispatcher as their routes land).
    /// </summary>
    private static async Task<IResult> MessageSendTargetAsync(
        string target, SendMessageRequest? request, HttpContext context, IThreadStore store, TimeProvider time)
    {
        var id = ThreadIdOf(target);
        if (id is null)
        {
            return HiddenThread(context);
        }

        return await MessageSendAsync(id, request, context, store, time);
    }

    /// <summary>
    /// <c>POST /api/threads/{id}/messages</c> (contract §6, SPEC-0010 section B): session (401),
    /// then the 404 rule of FR-031 — a thread the actor is not in and a thread that does not exist
    /// share one body, decided by the single participant probe before anything else is learned
    /// about the thread (AC-04, SC-005); then the Content ladder of data-model §2.5.1 (AC-02:
    /// empty or above 4000 is 400, GAP-24's sanitizer runs in between); and only then the write:
    /// the append takes the next seq under the thread's version guard and lands the row in the
    /// SAME transaction (GAP-29), so two senders racing never take a number twice and a 200 never
    /// names a message the delta cannot already return. The answer is 200 + seq — "entregue =
    /// persistido" (section D), not 201.
    /// </summary>
    private static async Task<IResult> MessageSendAsync(
        string id, SendMessageRequest? request, HttpContext context, IThreadStore store, TimeProvider time)
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

        var refusal = ContentRefusal(context, request?.Content, out var content);
        if (refusal is not null)
        {
            return refusal;
        }

        var stored = await store.AppendMessageAsync(
            id, $"{Origin(context)}/m/{NewToken()}", actorId, content, time.GetUtcNow());

        // Defensive twin of the probe: the thread vanished between check and append (it cannot
        // today — no route deletes one — but the answer stays the one body of FR-031 anyway).
        return stored is null
            ? HiddenThread(context)
            : Results.Json(MessageView.Of(stored));
    }

    /// <summary>
    /// The GET tail of <c>/api/threads</c>: <c>{id}/messages</c> only, anything else the shared 404.
    /// </summary>
    private static async Task<IResult> MessageListTargetAsync(
        string target, HttpContext context, IThreadStore store)
    {
        var id = ThreadIdOf(target);
        if (id is null)
        {
            return HiddenThread(context);
        }

        return await MessageListAsync(id, context, store);
    }

    /// <summary>
    /// <c>GET /api/threads/{id}/messages?since=</c> (contract §6/§9.3, SPEC-0010 section B): the
    /// same session and the same byte-identical 404 as the POST (AC-04), then the delta — only
    /// <c>seq &gt; since</c>, no duplicates, ordered by <c>seq</c>, with <c>nextSince</c> so a
    /// polling client (GAP-36) comes back exactly where it stopped: the highest number it was
    /// given, or <c>since</c> unchanged when nothing is new (AC-03). A <c>since</c> that is not a
    /// non-negative integer is a payload this route cannot take (400).
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

        if (!await store.IsParticipantAsync(
                id, ContainerEndpoints.ActorId(context, current.Handle)))
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
        return Results.Json(new
        {
            items = items.Select(MessageView.Of).ToList(),
            nextSince = items.Count > 0 ? items[^1].Seq : since,
        });
    }

    /// <summary>
    /// The <c>{id}/messages</c> tail off a catch-all target: the id arrives as the caller spelled
    /// it — the whole URI plain or percent-encoded, the same rule the post and admin routes read
    /// off theirs — so the unescape happens once here, and only a target whose tail is
    /// <c>/messages</c> names a message route. Null is any other tail (the shared 404).
    /// </summary>
    private static string? ThreadIdOf(string? target)
    {
        var path = Uri.UnescapeDataString(target ?? string.Empty).Trim().TrimEnd('/');
        if (!path.EndsWith(MessagesSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        // No id at all (`/api/threads//messages` after a doubled separator): not a thread worth
        // probing — the store guards its arguments, so the refusal has to happen here.
        var id = path[..^MessagesSuffix.Length];
        return string.IsNullOrWhiteSpace(id) ? null : id;
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
