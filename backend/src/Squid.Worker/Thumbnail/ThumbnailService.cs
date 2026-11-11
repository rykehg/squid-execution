using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Squid.Api.Media;
using Squid.Domain.Objects;

namespace Squid.Worker.Thumbnail;

/// <summary>
/// SPEC-0009 section J (TASK-0032): the hosted thumbnail pass of the Worker — one sweep over the
/// media volume that registers a thumbnail for every content-addressed blob lacking one, off the
/// upload path (R-18, same shape as <c>QuotaReconcileService</c>). Image/Gif decode through
/// SkiaSharp from the EXIF-stripped bytes the upload stored (FR-021); video/audio get the
/// registered placeholder (GAP-21 — SkiaSharp never decodes video). One pass as soon as the
/// worker starts and then every <c>Thumbnail:IntervalSeconds</c> (default 60); a failing pass
/// logs and keeps the schedule. Tests call <see cref="RunOnceAsync"/> directly — the same pass
/// the loop runs — so no test ever sleeps.
/// </summary>
public sealed class ThumbnailService : BackgroundService
{
    private readonly string _blobRoot;

    private readonly ThumbnailGenerator _generator;

    private readonly TimeSpan _interval;

    private readonly ILogger<ThumbnailService> _logger;

    /// <summary>Wires the hosted run to the volume it sweeps (tests pass a null logger's instance).</summary>
    public ThumbnailService(
        string blobRoot, ThumbnailGenerator generator, TimeSpan interval,
        ILogger<ThumbnailService>? logger = null)
    {
        _blobRoot = blobRoot ?? throw new ArgumentNullException(nameof(blobRoot));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _interval = interval;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>One pass over the volume; answers how many thumbnails it registered — the pass the tests invoke.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_blobRoot)) return 0;

        var generated = 0;
        // Top level only: the originals live flat under the root, the thumbnails in their own
        // subfolder — so the sweep never mistakes a thumbnail for media lacking one.
        foreach (var file in Directory.EnumerateFiles(_blobRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(file);
            if (name.Length != 64 || !name.All(Uri.IsHexDigit)) continue; // only content-addressed blobs are media

            var thumbnailPath = Path.Combine(_blobRoot, IMediaStore.ThumbnailFolder, name);
            if (File.Exists(thumbnailPath)) continue; // already registered: the queue is "lacking one"

            var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            var mimeType = MediaSignature.Sniff(bytes);
            if (mimeType is null) continue; // not an upload the media store could have written

            var thumbnail = Attachment.KindOf(mimeType) is AttachmentKind.Image or AttachmentKind.Gif
                ? _generator.Image(bytes)
                : MediaPlaceholders.Bytes; // GAP-21: video/audio never decode into pixels
            if (thumbnail is null)
            {
                _logger.LogWarning("thumbnail pass cannot decode blob {Hash}; it stays queued for the next pass", name);
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.Combine(_blobRoot, IMediaStore.ThumbnailFolder));
                await using var stream = new FileStream(
                    thumbnailPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await stream.WriteAsync(thumbnail, cancellationToken);
            }
            catch (IOException) when (File.Exists(thumbnailPath))
            {
                // A racing pass registered the same content-addressed thumbnail first.
            }

            generated++;
        }

        return generated;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                // One failed pass must not stop the schedule; the next one picks the queue up.
                _logger.LogWarning(failure, "thumbnail pass failed; it keeps the schedule");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
