// Composition root of the worker process: media, backup, cipher and Reed-Solomon (doc 05 §4.6).
// It stays a separate process exactly so that heavy CPU never lands on the silo scheduler.
// First work item — SPEC-0009 section B (TASK-0024): the projection pump, a hosted service that
// consumes the activity log into Squid.ReadModels' tables in Seq order, off the write path (R-18).
// The database is the service's own file (Storage:Path, the same configuration key SquidServiceHost
// reads); the schema it writes into is applied by Squid.Service's init — the dev/test/CI path the
// human gate of 2026-11-09 liberates (GAP-19), so start the service before the worker on a fresh
// machine.
// Second work item — SPEC-0009 section F (TASK-0028): the quota reconcile sweep, a hosted service
// that rewrites UsedBytes from a scan of the blob volume (the source of truth) off the upload path.
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Squid.ReadModels.Projections;
using Squid.ReadModels.Quota;
using Squid.Worker.Projection;
using Squid.Worker.Quota;
using Squid.Worker.Thumbnail;

var builder = Host.CreateApplicationBuilder(args);
var storagePath = builder.Configuration["Storage:Path"]
    ?? Path.Combine(AppContext.BaseDirectory, "squid-grainstate.db");
var connectionString = $"Data Source={storagePath};Pooling=False";

builder.Services.AddSingleton(new SqliteActivityLog(connectionString));
builder.Services.AddSingleton<IActivityProjector>(_ => new ActivityProjector(connectionString));
builder.Services.AddHostedService<ActivityProjectionPump>();

// SPEC-0009 section F (TASK-0028): the daily quota sweep over the same SQLite file and the same
// blob volume Squid.Service writes. Media:BlobRoot is the key the API host reads too — the fallback
// below is MediaOptions' own default, so a deployment with a custom root configures both processes
// with the same value (compose does). Quota:ReconcileHours defaults to 24 (task gap in force).
var quotaLedger = new SqliteQuotaLedger(connectionString);
builder.Services.AddSingleton<IQuotaLedger>(quotaLedger);
var blobRoot = builder.Configuration["Media:BlobRoot"]
    ?? Path.Combine(AppContext.BaseDirectory, "media-blobs");
var reconcileHours = double.TryParse(
        builder.Configuration["Quota:ReconcileHours"], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
    && hours > 0
    ? hours
    : 24;
builder.Services.AddSingleton<IHostedService>(provider => new QuotaReconcileService(
    new QuotaReconciler(quotaLedger, blobRoot),
    TimeSpan.FromHours(reconcileHours),
    provider.GetRequiredService<ILogger<QuotaReconcileService>>()));

// SPEC-0009 section J (TASK-0032): the thumbnail pass over that same blob volume — Image/Gif get
// a SkiaSharp thumbnail from the EXIF-stripped blob, Video/Audio the registered placeholder
// (GAP-21). Media:BlobRoot is the key the API host reads too; Thumbnail:IntervalSeconds defaults
// to 60 (task gap in force), and the tests invoke the pass directly through RunOnceAsync.
var thumbnailSeconds = double.TryParse(
        builder.Configuration["Thumbnail:IntervalSeconds"], NumberStyles.Float, CultureInfo.InvariantCulture, out var interval)
    && interval > 0
    ? interval
    : 60;
builder.Services.AddSingleton<IHostedService>(provider => new ThumbnailService(
    blobRoot,
    new ThumbnailGenerator(),
    TimeSpan.FromSeconds(thumbnailSeconds),
    provider.GetRequiredService<ILogger<ThumbnailService>>()));

await builder.Build().RunAsync();
