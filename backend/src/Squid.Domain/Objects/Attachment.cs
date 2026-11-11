namespace Squid.Domain.Objects;

/// <summary>The kind of an attachment (data-model §2.6): <c>Link</c> carries no blob, the rest are media.</summary>
public enum AttachmentKind
{
    Link,
    Image,
    Gif,
    Sticker,
    Audio,
    Video,
}

/// <summary>
/// The attachment of data-model §2.6: the SHA-256 of the content-addressed blob, the media type a
/// magic-byte sniff decided (never the extension), the instance-served URL, and the metadata the
/// upload recorded — <c>BlurHash</c> mandatory for <c>Image</c>/<c>Gif</c>, <c>Alt</c> mandatory
/// and non-empty for <c>Image</c>/<c>Video</c>/<c>Gif</c> (FR-022), both checked at the two places
/// §2.6 names: the upload and the publication that attaches the hash (AC-02/AC-03).
/// </summary>
public sealed record Attachment
{
    /// <summary>data-model §2.5: a post carries at most 10 attachments.</summary>
    public const int MaxPerPost = 10;

    public required AttachmentKind Kind { get; init; }

    /// <summary>SHA-256 hex of the stored blob — the row key of the media record.</summary>
    public required string ContentHash { get; init; }

    public required string MimeType { get; init; }

    /// <summary>Where the instance serves the blob (<c>/api/media/{contentHash}</c>).</summary>
    public required string Url { get; init; }

    /// <summary>Required for <c>Image</c>/<c>Gif</c> (§2.6), computed after the EXIF strip — null elsewhere.</summary>
    public string? BlurHash { get; init; }

    /// <summary>Required and non-empty for <c>Image</c>/<c>Video</c>/<c>Gif</c> (FR-022).</summary>
    public required string Alt { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    /// <summary>Whole seconds of an MP4/MOV <c>mvhd</c> box (GAP-22); null for every other type.</summary>
    public int? DurationSeconds { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>FR-022: the kinds whose publication refuses an empty <c>alt</c> (§2.6).</summary>
    public static bool NeedsAlt(AttachmentKind kind) =>
        kind is AttachmentKind.Image or AttachmentKind.Video or AttachmentKind.Gif;

    /// <summary>§2.6: the kinds that must carry a blurHash — they decode to pixels, the others do not.</summary>
    public static bool NeedsBlurHash(AttachmentKind kind) =>
        kind is AttachmentKind.Image or AttachmentKind.Gif;

    /// <summary>The kind behind a sniffed media type — the wire never decides it from a name.</summary>
    public static AttachmentKind KindOf(string mimeType) => mimeType switch
    {
        "image/png" or "image/jpeg" => AttachmentKind.Image,
        "image/gif" => AttachmentKind.Gif,
        "video/mp4" or "video/quicktime" => AttachmentKind.Video,
        "audio/mpeg" or "audio/mp4" or "audio/wav" or "audio/ogg" or "audio/flac" => AttachmentKind.Audio,
        _ => AttachmentKind.Link,
    };
}
