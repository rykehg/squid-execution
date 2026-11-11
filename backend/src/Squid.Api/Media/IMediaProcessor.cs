namespace Squid.Api.Media;

/// <summary>
/// The entry pipeline's outcome (SPEC-0009 section E): the bytes as they will be persisted —
/// EXIF already gone — plus the metadata the contract response carries. Width/height and
/// blurHash are set for <c>Image</c>/<c>Gif</c> only, duration only for an MP4/MOV (GAP-22).
/// </summary>
public sealed record ProcessedMedia(
    byte[] Bytes,
    int? Width,
    int? Height,
    string? BlurHash,
    int? DurationSeconds);

/// <summary>
/// The processing step of the upload pipeline, declared at the edge (ADR-35) and implemented in
/// <c>Squid.Service</c> behind SkiaSharp — no Skia type ever crosses this interface. The order is
/// the §2.6 invariant: strip EXIF from the incoming bytes FIRST, then decode and compute
/// blurHash/width/height over the stripped bytes, then the caller persists what comes back —
/// the served blob therefore never carries EXIF/GPS (FR-021, AC-01/AC-05).
/// </summary>
public interface IMediaProcessor
{
    /// <summary>Processes sniffed <paramref name="mimeType"/> bytes; null when the image cannot be decoded (→ 415).</summary>
    ProcessedMedia? Process(byte[] original, string mimeType);
}
