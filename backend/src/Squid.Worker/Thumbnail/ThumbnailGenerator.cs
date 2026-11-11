using SkiaSharp;
using Squid.Api.Media;

namespace Squid.Worker.Thumbnail;

/// <summary>
/// The SkiaSharp step of SPEC-0009 section J: decode the ALREADY EXIF-stripped blob the upload
/// stored (FR-021 — this pass never sees metadata), scale it to <see cref="MaxDimension"/> on
/// its longest side without upscaling, encode PNG — the one media type the serving route
/// answers for a thumbnail (<see cref="IMediaStore.ThumbnailMimeType"/>). Video and audio never
/// reach here (GAP-21): the service hands them the registered placeholder instead.
/// </summary>
public sealed class ThumbnailGenerator
{
    /// <summary>Longest side of a generated thumbnail; the spec fixes no number.</summary>
    public const int MaxDimension = 256;

    /// <summary>The thumbnail of <paramref name="blob"/>, or null when the bytes do not decode.</summary>
    public byte[]? Image(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        using var decoded = SKBitmap.Decode(blob);
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
        {
            return null;
        }

        var scale = Math.Min(1d, (double)MaxDimension / Math.Max(decoded.Width, decoded.Height));
        var width = Math.Max(1, (int)Math.Round(decoded.Width * scale));
        var height = Math.Max(1, (int)Math.Round(decoded.Height * scale));
        if (width == decoded.Width && height == decoded.Height)
        {
            return Encode(decoded);
        }

        using var scaled = new SKBitmap(width, height, decoded.ColorType, decoded.AlphaType);
        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(decoded, new SKRect(0, 0, width, height));
            canvas.Flush();
        }

        return Encode(scaled);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        return encoded.ToArray();
    }
}
