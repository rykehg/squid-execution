using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Squid.Api.Auth;
using Squid.Api.Containers;

namespace Squid.Api.Threads;

/// <summary>The create body of contracts/http-api.md §6 (SPEC-0010 inputs): the kind plus the participants.</summary>
public sealed record CreateThreadRequest(string? Kind, IReadOnlyList<string>? ParticipantActorIds);

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

    /// <summary>Maps the thread routes; runs after <c>MapSquidAuth</c>, on the session pipeline.</summary>
    public static WebApplication MapSquidThreads(this WebApplication app)
    {
        app.MapPost("/api/threads", CreateAsync);
        app.MapGet("/api/threads", ListAsync);
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
    /// The identity origin of P-02/P-03: scheme + Host of the very request that asked — the same
    /// rule as <c>ContainerEndpoints</c> and <c>PostEndpoints</c>, never a configured domain
    /// (one resource, one URI, whether the instance is reached over http or https).
    /// </summary>
    private static string Origin(HttpContext context) =>
        $"{context.Request.Scheme}://{context.Request.Host.Value}";

    /// <summary>The opaque id of a thread: 128 random bits, hex — the <c>/t/{id}</c> tail.</summary>
    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static IResult Invalid(HttpContext context, string message) =>
        AuthErrors.Write(context, StatusCodes.Status400BadRequest, "validation_failed", message);
}
