using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.Api.Threads;
using Squid.Domain.Objects;
using Squid.ReadModels.Quota;

namespace Squid.Api.Media;

/// <summary>
/// SPEC-0009 section E (contract §5): <c>POST /api/media</c> takes the multipart upload
/// <c>file</c> + <c>alt</c> and answers the contract's metadata; <c>GET /api/media/{contentHash}</c>
/// serves the stored blob behind the GAP-25 rule (session + uploader, or a reader who sees at
/// least one post referencing the hash) with the byte-identical 404 of SC-005 — TASK-0039
/// ampliates that rule per SPEC-0010 section G: a participant of a thread whose MESSAGE references
/// the hash sees it too, while everyone else (a participant of another thread included) keeps the
/// same 404, and the post half of the rule stays exactly as it was. The order of the upload is the
/// §2.6 invariant: magic bytes first (415), alt of image/video/gif (400), per-type
/// limit (413), and only then the processor — which strips EXIF before anything is hashed,
/// persisted or blurred. Section F (TASK-0028) closes the ladder: the optimistic quota reservation
/// sits between the processed bytes and the blob write, so a <c>507 quota_exceeded</c> leaves the
/// volume and the ledger exactly as they were — nothing of that pipeline moves in this task.
/// </summary>
public static class MediaEndpoints
{
    /// <summary>Maps the three media routes; runs after <c>MapSquidAuth</c>, on the session pipeline.</summary>
    public static WebApplication MapSquidMedia(this WebApplication app)
    {
        app.MapPost("/api/media", UploadAsync);
        app.MapGet("/api/media/{contentHash}", GetAsync);
        app.MapGet("/api/media/{contentHash}/thumb", GetThumbnailAsync);
        return app;
    }

    /// <summary>
    /// <c>POST /api/media</c>: session (401), multipart (400), sniffed type (415 — §9.5, the
    /// declared type must agree with the bytes), non-empty <c>alt</c> for
    /// <see cref="AttachmentKind.Image"/>/<see cref="AttachmentKind.Video"/>/<see cref="AttachmentKind.Gif"/>
    /// (400), size of the sniffed type (413), process, the quota reservation of section F (507),
    /// then the content-addressed write and the contract body. Re-uploading identical bytes
    /// answers the same hash (content-addressed).
    /// </summary>
    private static async Task<IResult> UploadAsync(
        HttpContext context, IMediaStore store, IMediaProcessor processor,
        MediaOptions options, IQuotaLedger quota, TimeProvider time)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var request = context.Request;
        if (!request.HasFormContentType)
        {
            return Invalid(context, "esperado multipart/form-data com os campos file e alt.");
        }

        // The body ceiling sits above the largest per-type limit, so a file at or just above its
        // own type's limit is read to the end here and answered with the contract's 413 envelope
        // (§1) instead of Kestrel's empty 413; past the ceiling the same envelope is written below.
        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = options.MaxRequestBytes;
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(context.RequestAborted);
        }
        catch (InvalidDataException)
        {
            return Invalid(context, "multipart inválido: o corpo não pôde ser lido.");
        }
        catch (Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException ex)
            when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Past the ceiling Kestrel stops the body before a byte of it is ours; §1 still demands
            // the envelope, so the route answers it instead of Kestrel's empty 413 on the wire.
            return AuthErrors.Write(context, StatusCodes.Status413PayloadTooLarge,
                "payload_too_large", $"corpo da requisição acima do teto de {options.MaxRequestBytes} bytes.");
        }

        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return Invalid(context, "arquivo obrigatório: informe o campo file.");
        }

        byte[] original;
        await using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, context.RequestAborted);
            original = buffer.ToArray();
        }

        var mimeType = MediaSignature.Sniff(original);
        if (mimeType is null)
        {
            return Unsupported(context, "o arquivo não corresponde a nenhum tipo de mídia suportado.");
        }

        // Declared vs actual (spec rule, §9.5): the bytes decide, so a lying media type is 415 —
        // a client that says nothing or says octet-stream simply gets what the bytes really are.
        // A part's content-type is a raw header: parameters (name=, charset=) are not part of the claim.
        var declaredHeader = file.Headers.ContentType.ToString();
        var declared = declaredHeader.Split(';')[0].Trim();
        if (!string.IsNullOrEmpty(declared)
            && !declared.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            && !declared.Equals(mimeType, StringComparison.OrdinalIgnoreCase))
        {
            return Unsupported(context, "o conteúdo do arquivo não corresponde ao tipo declarado.");
        }

        var kind = Attachment.KindOf(mimeType);
        var alt = form["alt"].ToString().Trim();
        if (Attachment.NeedsAlt(kind) && alt.Length == 0)
        {
            return Invalid(context, "alt obrigatório e não vazio para imagem, vídeo e gif (FR-022).");
        }

        var limit = options.LimitFor(mimeType);
        if (original.LongLength > limit)
        {
            return AuthErrors.Write(context, StatusCodes.Status413PayloadTooLarge,
                "payload_too_large", $"arquivo acima do limite de {limit} bytes para este tipo de mídia.");
        }

        var processed = processor.Process(original, mimeType);
        if (processed is null)
        {
            return Unsupported(context, "o arquivo não pôde ser decodificado como a imagem que suas magics declaram.");
        }

        var contentHash = Convert.ToHexStringLower(SHA256.HashData(processed.Bytes));

        // SPEC-0009 section F (TASK-0028): the optimistic reservation of the instance quota runs on
        // the PROCESSED size — the bytes that would actually be stored — and strictly before the
        // blob write, so a 507 leaves both volumes untouched: no file, no ledger change (the row
        // hash is identical before and after, AC-01). The daily blob sweep is the source of truth
        // and corrects any drift this fast gate leaves between two scans.
        if (!await quota.TryReserveAsync(
                processed.Bytes.LongLength, options.QuotaLimitBytes, time.GetUtcNow()))
        {
            return AuthErrors.Write(context, StatusCodes.Status507InsufficientStorage,
                "quota_exceeded", "cota de mídia da instância excedida.");
        }

        var record = new MediaRecord(
            contentHash, mimeType, alt, processed.Width, processed.Height,
            processed.DurationSeconds, processed.BlurHash, processed.Bytes.LongLength,
            current.Handle, time.GetUtcNow());
        await store.SaveAsync(processed.Bytes, record);

        return Results.Json(new
        {
            contentHash,
            mimeType,
            width = processed.Width,
            height = processed.Height,
            durationSeconds = processed.DurationSeconds,
            blurHash = processed.BlurHash,
            sizeBytes = processed.Bytes.LongLength,
        });
    }

    /// <summary>
    /// <c>GET /api/media/{contentHash}</c> (GAP-25 + SPEC-0010 section G): session (401), then serve —
    /// the uploader, a reader who sees at least one post referencing the hash through the post
    /// visibility ladder itself, or a participant of a thread whose message references it (the
    /// ampliation TASK-0039 owns). Absent hash and out-of-sight hash share one 404 body, no traceId (SC-005).
    /// </summary>
    private static async Task<IResult> GetAsync(
        string contentHash, HttpContext context, IMediaStore store, IObjectStore objects,
        IContainerStore containers, IPolicyAuditStore audit, IThreadStore threads)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var record = await VisibleAsync(
            contentHash, current, context, store, objects, containers, audit, threads);
        return record is null
            ? HiddenMedia(context)
            : await ServeAsync(context, store, record);
    }

    /// <summary>
    /// <c>GET /api/media/{contentHash}/thumb</c> (SPEC-0009 section J): the very same session +
    /// GAP-25 ladder as the blob route — the visibility rule of TASK-0027 is one rule, not two,
    /// now carrying TASK-0039's ampliation —
    /// then the thumbnail the Worker registered for the hash; a visible record without one yet
    /// (video/audio per GAP-21, or an image whose pass has not run) serves the registered
    /// placeholder instead, so any media a reader may see answers an image here.
    /// </summary>
    private static async Task<IResult> GetThumbnailAsync(
        string contentHash, HttpContext context, IMediaStore store, IObjectStore objects,
        IContainerStore containers, IPolicyAuditStore audit, IThreadStore threads)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var record = await VisibleAsync(
            contentHash, current, context, store, objects, containers, audit, threads);
        if (record is null)
        {
            return HiddenMedia(context);
        }

        var thumbnail = await store.ReadThumbnailAsync(record.ContentHash);
        return Results.File(
            thumbnail ?? MediaPlaceholders.Bytes, IMediaStore.ThumbnailMimeType);
    }

    /// <summary>
    /// The GAP-25 ladder both media GETs read with — the record when the reader is its uploader,
    /// or sees at least one post referencing the hash through the post visibility rule itself, or
    /// (TASK-0039's ampliation, SPEC-0010 section G) sits in a thread whose message references the
    /// hash; null when the hash is absent or outside the reader's sight — the caller owes the one
    /// byte-identical 404 (SC-005), so absent and hidden stay indistinguishable.
    /// </summary>
    private static async Task<MediaRecord?> VisibleAsync(
        string contentHash, AuthSessions.Resolved current, HttpContext context, IMediaStore store,
        IObjectStore objects, IContainerStore containers, IPolicyAuditStore audit, IThreadStore threads)
    {
        var record = await store.FindAsync(contentHash);
        if (record is null) return null;
        if (record.UploaderHandle == current.Handle) return record;

        foreach (var post in await objects.FindByContentHashAsync(record.ContentHash))
        {
            var container = await PostEndpoints.VisibleAsync(
                (await containers.FindAsync(post.ContainerSlug))?.Container, post.ContainerId,
                context, containers, audit, forRead: true);
            if (container is not null) return record;
        }

        // Section G (TASK-0039): the participant of a conversation carrying this hash reads it —
        // the post rule above decides nothing here, and a reader in no such thread still gets null.
        var actorId = ContainerEndpoints.ActorId(context, current.Handle);
        if (await threads.MessageMediaVisibleAsync(record.ContentHash, actorId)) return record;

        return null;
    }

    /// <summary>The stored bytes under their own media type; a missing blob is the same 404 as a missing hash.</summary>
    private static async Task<IResult> ServeAsync(HttpContext context, IMediaStore store, MediaRecord record)
    {
        var bytes = await store.ReadAsync(record.ContentHash);
        return bytes is null ? HiddenMedia(context) : Results.File(bytes, record.MimeType);
    }

    /// <summary>FR-018/SC-005: an absent hash and a hash outside the reader's sight share one body.</summary>
    private static IResult HiddenMedia(HttpContext context) =>
        AuthErrors.Write(context, StatusCodes.Status404NotFound,
            "not_found", "mídia não encontrada.", withTraceId: false);

    private static IResult Invalid(HttpContext context, string message) =>
        AuthErrors.Write(context, StatusCodes.Status400BadRequest, "validation_failed", message);

    private static IResult Unsupported(HttpContext context, string message) =>
        AuthErrors.Write(context, StatusCodes.Status415UnsupportedMediaType,
            "unsupported_media_type", message);
}
