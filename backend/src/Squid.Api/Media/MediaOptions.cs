using Microsoft.Extensions.Configuration;
using Squid.Domain.Objects;

namespace Squid.Api.Media;

/// <summary>
/// The configuration of the media slice (task gaps, SPEC-0009 section E/F): the named blob volume
/// <c>Media:BlobRoot</c> (data-model §5), the per-type limits <c>Media:MaxBytes:Image|Video|
/// Audio</c> — the doc gives no number, so the defaults are 10 MiB for an image and 50 MiB for a
/// video/audio, every one of them configurable (413 above the limit of the sniffed type) — and the
/// instance quota <c>Quota:LimitBytes</c>, whose default is the 10 GiB of data-model §2.13.
/// </summary>
public sealed record MediaOptions(
    string BlobRoot, long MaxImageBytes, long MaxVideoBytes, long MaxAudioBytes, long QuotaLimitBytes)
{
    /// <summary>Default image/gif limit: 10 MiB.</summary>
    public const long DefaultImageBytes = 10 * 1024 * 1024;

    /// <summary>Default video limit: 50 MiB.</summary>
    public const long DefaultVideoBytes = 50 * 1024 * 1024;

    /// <summary>Default audio limit — the video number, the doc only fixes the image/video pair.</summary>
    public const long DefaultAudioBytes = 50 * 1024 * 1024;

    /// <summary>Default instance quota: 10 GiB, integer bytes (R-19, data-model §2.13).</summary>
    public const long DefaultQuotaBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>
    /// Reads the options from configuration, falling back to the defaults above. The blob root
    /// defaults beside the binaries so a dev host works with no configuration at all; tests point
    /// it at a temp directory.
    /// </summary>
    public static MediaOptions From(IConfiguration configuration) => new(
        configuration["Media:BlobRoot"] ?? Path.Combine(AppContext.BaseDirectory, "media-blobs"),
        Bytes(configuration["Media:MaxBytes:Image"], DefaultImageBytes),
        Bytes(configuration["Media:MaxBytes:Video"], DefaultVideoBytes),
        Bytes(configuration["Media:MaxBytes:Audio"], DefaultAudioBytes),
        Bytes(configuration["Quota:LimitBytes"], DefaultQuotaBytes));

    /// <summary>
    /// How far the body ceiling sits above the largest per-type limit (QA-0026 achado 1): a file
    /// AT that limit and a file just above it still reach the route, which answers the contract's
    /// 413 envelope (§1 http-api.md) — with the ceiling equal to the limit, Kestrel refused the
    /// body first and put an empty 413 on the wire. The absolute backstop stays: nothing beyond
    /// the ceiling is ever read, and the route answers that case with the same envelope.
    /// </summary>
    public const long BodyHeadroomBytes = 16 * 1024 * 1024;

    /// <summary>The ceiling the request body reader may accept: the largest per-type limit plus its headroom.</summary>
    public long MaxRequestBytes =>
        Math.Max(MaxImageBytes, Math.Max(MaxVideoBytes, MaxAudioBytes)) + BodyHeadroomBytes;

    /// <summary>The limit of the sniffed type — a file above it answers 413, whatever its declared type said.</summary>
    public long LimitFor(string mimeType) => Attachment.KindOf(mimeType) switch
    {
        AttachmentKind.Video => MaxVideoBytes,
        AttachmentKind.Audio => MaxAudioBytes,
        _ => MaxImageBytes,
    };

    private static long Bytes(string? raw, long fallback) =>
        long.TryParse(raw, out var value) && value > 0 ? value : fallback;
}
