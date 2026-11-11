using SkiaSharp;
using Squid.Api.Media;
using Squid.Domain.Objects;

namespace Squid.Service;

/// <summary>
/// <see cref="IMediaProcessor"/> over SkiaSharp — the one place the image package touches
/// (human gate 2026-11-09: SkiaSharp decodes, EXIF stripping and the blurHash encoder stay
/// hand-written). The order inside <see cref="Process"/> IS the data-model §2.6 invariant:
/// strip EXIF first, then decode the CLEAN bytes for width/height and blurHash — so nothing that
/// is hashed, persisted or blurred ever carried metadata (FR-021, AC-01/AC-05). Images that do
/// not decode answer null and the route turns that into 415; video and audio never decode here
/// (GAP-21), they only get their <c>mvhd</c> duration when the type is MP4/MOV (GAP-22).
/// </summary>
public sealed class SkiaMediaProcessor : IMediaProcessor
{
    /// <inheritdoc />
    public ProcessedMedia? Process(byte[] original, string mimeType)
    {
        ArgumentNullException.ThrowIfNull(original);

        var bytes = ExifStripper.Strip(original, mimeType);

        int? width = null;
        int? height = null;
        string? blurHash = null;

        if (Attachment.NeedsBlurHash(Attachment.KindOf(mimeType)))
        {
            using var data = SKData.CreateCopy(bytes);
            using var decoded = SKBitmap.Decode(data);
            if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
            {
                // A truncated or lying image: the route answers 415, never a stack trace.
                return null;
            }

            width = decoded.Width;
            height = decoded.Height;
            blurHash = BlurHashEncoder.Encode(decoded);
        }

        var duration = Attachment.KindOf(mimeType) == AttachmentKind.Video
            ? Mp4Duration.Seconds(bytes)
            : null;

        return new ProcessedMedia(bytes, width, height, blurHash, duration);
    }
}
