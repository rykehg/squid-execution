namespace Squid.Api.Media;

/// <summary>
/// What <c>POST /api/media</c> recorded about one upload (SPEC-0009 section E): the metadata of
/// the contract response plus the uploader — the record the publish path reads back to build the
/// <c>Attachment</c> (alt inherited from the upload, AC-03) and the GET route reads to decide
/// visibility (GAP-25). Persisted as a keyed document, one row per content hash.
/// </summary>
public sealed record MediaRecord(
    string ContentHash,
    string MimeType,
    string Alt,
    int? Width,
    int? Height,
    int? DurationSeconds,
    string? BlurHash,
    long SizeBytes,
    string UploaderHandle,
    DateTimeOffset UploadedAt);

/// <summary>
/// The media store of ADR-35: the edge declares it, <c>Squid.Service</c> implements it over the
/// same SQLite documents plus the content-addressed blob volume under <c>Media:BlobRoot</c>
/// (data-model §5, named volume — no external service). Every method is keyed by the SHA-256 hex
/// of the stored bytes; the store never accepts a hash that is not 64 hex characters, so a route
/// value can never walk outside the blob root.
/// </summary>
public interface IMediaStore
{
    /// <summary>
    /// The subfolder of the volume where the Worker registers thumbnails (SPEC-0009 section J):
    /// one file per original hash, written by <c>Squid.Worker</c> and read through the store —
    /// the same cross-process path convention the original blobs already are, never a route value.
    /// </summary>
    public const string ThumbnailFolder = "thumbs";

    /// <summary>The one media type a registered thumbnail carries — the Worker's generator encodes PNG.</summary>
    public const string ThumbnailMimeType = "image/png";

    /// <summary>The upload record of <paramref name="contentHash"/>, or null when it never happened.</summary>
    Task<MediaRecord?> FindAsync(string contentHash);

    /// <summary>The stored bytes of <paramref name="contentHash"/> — exactly what was hashed — or null.</summary>
    Task<byte[]?> ReadAsync(string contentHash);

    /// <summary>Writes blob and record under the hash; an already stored hash is a no-op (content-addressed).</summary>
    Task SaveAsync(byte[] bytes, MediaRecord record);

    /// <summary>
    /// The registered thumbnail of <paramref name="contentHash"/> (section J), or null when the
    /// Worker has not generated one yet — the serving route answers that case with the
    /// placeholder, never with the original bytes.
    /// </summary>
    Task<byte[]?> ReadThumbnailAsync(string contentHash);
}
