using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using Orleans.Dashboard;
using Orleans.Hosting;
using Orleans.Runtime.Hosting;
using Serilog;
using Serilog.Configuration;
using Serilog.Formatting.Json;
using Squid.Api;
using Squid.Api.Admin;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Api.Discovery;
using Squid.Api.Media;
using Squid.Api.Posts;
using Squid.Api.Recovery;
using Squid.Api.Threads;
using Squid.Domain;
using Squid.Grains.Actors;
using Squid.Persistence;
using Squid.Protocols.ActivityPub;
using Squid.ReadModels.Projections;
using Squid.ReadModels.Quota;

namespace Squid.Service;

/// <summary>
/// Composition root of the service process: silo + API in one host (doc 05 section 11). This is
/// the only place that knows every layer at once; <c>Program</c> and the host tests both build
/// the application here, so what the tests start is what <c>dotnet run</c> runs. It is also where
/// the two observability pipelines enter (SPEC-0002): Serilog for logs and OpenTelemetry for
/// traces.
/// </summary>
public static class SquidServiceHost
{
    /// <summary>Name of the Orleans storage provider that wraps <see cref="SqliteGrainStorage"/>.</summary>
    public const string StorageProviderName = "sqlite";

    /// <summary>
    /// Builds the full application: silo, SQLite grain storage, /health, /ready, dashboard.
    /// <paramref name="configure"/> is the test seam of GAP-17: it runs after every production
    /// registration, so a test can swap the <see cref="TimeProvider"/> of the section G timers
    /// (and any configuration) before the application is built; production passes nothing.
    /// </summary>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Serilog is the only logger of the process (SPEC-0002 GAP-10): structured console JSON
        // with PII masked. ClearProviders runs before UseSerilog registers Serilog's provider, so
        // no default plain-text provider can print an unmasked line next to it.
        builder.Logging.ClearProviders();
        builder.Host.UseSerilog((_, log) => log
            .MinimumLevel.Information()
            .Enrich.With<PiiSanitizingEnricher>()
            .WriteTo.Console(new PiiMaskingFormatter(new JsonFormatter(renderMessage: true))));

        // Tracing (SPEC-0002 GAP-09): every HTTP request becomes a span exported over OTLP to
        // OTEL_EXPORTER_OTLP_ENDPOINT, defaulting to http://localhost:4317. The exporter is
        // wrapped so an unreachable collector becomes a Warning in the log instead of an error
        // that could reach a request or a shutdown (AC-03); the OTLP client reports failure as a
        // result code, which is exactly what the wrapper watches.
        var otlpEndpoint = new Uri(OtlpEndpoint.Resolve(
            Environment.GetEnvironmentVariable(OtlpEndpoint.EnvironmentVariable)));
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddProcessor(serviceProvider =>
            {
                var exporter = new OtlpTraceExporter(new OtlpExporterOptions { Endpoint = otlpEndpoint });
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Squid.Service.Otlp");
                return new BatchActivityExportProcessor(
                    new WarningOtlpExporter(exporter, otlpEndpoint, logger),
                    2048, // exporter queue size (OTel default)
                    5000, // batch delay in ms (OTel default)
                    30000); // export timeout in ms (OTel default)
            }));

        var storagePath = builder.Configuration["Storage:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "squid-grainstate.db");
        // Pooling off: a pooled connection keeps the file handle open, so a rename would be
        // invisible to /ready and the file could not be moved away (AC-02).
        var connectionString = $"Data Source={storagePath};Pooling=False";

        // Created eagerly: the file and its schema exist before the first request is served.
        var storage = new SqliteGrainStorage(connectionString);
        // SPEC-0009 section B: the first versioned migration lands here, at init, before anything
        // reads or writes a projection table — the dev/test/CI path the human gate of 2026-11-09
        // liberates (GAP-19); running the same scripts against PRODUCTION stays a human gate.
        // It is idempotent (schema_version + checksum), so every host build re-apply is a no-op.
        SchemaMigrations.Apply(connectionString);

        // Identity wiring (SPEC-0007): the actor aggregate resolves through the production
        // IActorEventStore adapter — it lives here because ADR-35 forbids Squid.Grains ->
        // Squid.Persistence (backlog S-21) — and the credential row behind the auth surface is
        // read through IAuthStore, whose Squid.Api side cannot see the storage either.
        builder.Services.AddSingleton<IActorEventStore>(_ => new SqliteActorEventStore(connectionString));
        builder.Services.AddTransient<ActorGrain>();
        builder.Services.AddSingleton<IAuthStore>(_ => new SqliteAuthStore(storage));
        // Kit verification marker (SPEC-0007 B-13): same storage, its own store name — the row
        // FR-007 reads lives beside the credential row without widening the closed auth slice.
        builder.Services.AddSingleton<IRecoveryStore>(_ => new SqliteRecoveryStore(storage));
        // Container core (SPEC-0008 A): the edge declares IContainerStore (ADR-35) and this root
        // binds it to the same SQLite file, under its own store names.
        builder.Services.AddSingleton<IContainerStore>(_ => new SqliteContainerStore(storage, connectionString));
        // Policy decisions (SPEC-0008 B/C): the single evaluator of data-model §3.2 and the
        // append-only trail of FR-019, both over the same file — the trail is a container of the
        // same slice, so it lives beside the rows it describes.
        builder.Services.AddSingleton<IPolicyEvaluator, PolicyEvaluator>();
        builder.Services.AddSingleton<IPolicyAuditStore>(_ => new SqlitePolicyAuditStore(storage));
        // Publish core (SPEC-0009 A): the object rows with their per-container Object.Seq, the
        // append-only activity log (the source of the projections) and the §3.1 actor status the
        // publish ladder refuses — same file, own store names. The global Activity.Seq counter of
        // GAP-23 now lives in the migration's single-row table (TASK-0024) and is taken there by
        // the store's atomic increment; the interface callers see did not change.
        builder.Services.AddSingleton<IObjectStore>(_ => new SqliteObjectStore(storage));
        builder.Services.AddSingleton<IActivityStore>(_ => new SqliteActivityStore(storage, connectionString));
        builder.Services.AddSingleton<IActorStatusStore>(_ => new SqliteActorStatusStore(storage));
        // Denunciation queue (SPEC-0009 section I, TASK-0031, FR-028): the operator's reports
        // over the same SQLite file — one document, the ETag-guarded pattern of the trail, and
        // the GAP-28 idempotency (post, reporter) enforced inside its critical section.
        builder.Services.AddSingleton<IReportStore>(_ => new SqliteReportStore(storage));
        // Thread core (SPEC-0010 section A, TASK-0033): the store over the migrated thread tables —
        // the GAP-29 total order is a version-guarded UPDATE in SQL, never a grain (decision 9).
        // TASK-0039 (section G): it also reads the upload records, because the contentHashes of a
        // message are validated INSIDE the append transaction (the same ladder as the 428/422) —
        // resolved lazily, after every registration below exists.
        builder.Services.AddSingleton<IThreadStore>(provider =>
            new SqliteThreadStore(connectionString, provider.GetRequiredService<IMediaStore>()));
        // Media slice (SPEC-0009 section E, TASK-0027): the content-addressed blob volume and its
        // upload record over the same SQLite file (data-model §5: Media:BlobRoot is a NAMED
        // volume, never an external service), and the SkiaSharp pipeline whose order §2.6 fixes —
        // EXIF stripped before blurHash and before the blob is written. The options read the
        // configuration instance lazily (first resolution is after `configure` ran), so the test
        // seam can still point Media:BlobRoot at a temp directory.
        var configuration = builder.Configuration;
        builder.Services.AddSingleton(_ => MediaOptions.From(configuration));
        builder.Services.AddSingleton<IMediaStore>(provider =>
            new SqliteMediaStore(storage, provider.GetRequiredService<MediaOptions>().BlobRoot));
        builder.Services.AddSingleton<IMediaProcessor, SkiaMediaProcessor>();
        // Link unfurl (SPEC-0009 section G, TASK-0029): a publish whose content is a single URL
        // fetches its OpenGraph card synchronously inside the create path — Unfurl:TimeoutMs
        // (default 3 s) bounds the WHOLE chain over the caller's token, Unfurl:MaxBytes (default
        // 1 MB) caps the body. The handler never follows redirects on its own: LinkUnfurler walks
        // Location by hand so every hop is re-validated by the SSRF guard, and the resolver seam
        // is what the gate's tests replace — no test touches real DNS or sockets (AC-05).
        builder.Services.AddSingleton(_ => UnfurlOptions.From(configuration));
        builder.Services.AddSingleton<IUnfurlResolver, DnsUnfurlResolver>();
        builder.Services.AddSingleton<HttpMessageHandler>(_ => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // redirects are followed hop by hop, re-validated (GAP-26)
            UseCookies = false,        // a link preview never carries a jar between hosts
        });
        builder.Services.AddSingleton(provider => new HttpClient(
            provider.GetRequiredService<HttpMessageHandler>(), disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan, // LinkUnfurler owns the budget (Unfurl:TimeoutMs)
        });
        builder.Services.AddSingleton<LinkUnfurler>();
        // Live channel of SPEC-0009 section D (TASK-0026): the in-process wake hub behind
        // GET /api/stream — one instance for the whole host, beside the log its subscribers read,
        // no broker (R-16). Publishing to it never blocks the publish path (AC-05).
        builder.Services.AddSingleton<StreamHub>();
        // Read side of SPEC-0009 section B (TASK-0024): the Dapper projector and its event source
        // are registered so a test — or any composition that wants them — resolves exactly what
        // the worker pump runs. Registered, never started: the projection stays off the write path
        // (R-18), and the pump itself is the Squid.Worker hosted service.
        builder.Services.AddSingleton(new SqliteActivityLog(connectionString));
        builder.Services.AddSingleton<IActivityProjector>(_ => new ActivityProjector(connectionString));
        // Read side of SPEC-0009 section C (TASK-0025): the feed/timeline windows over the same
        // migrated file, registered beside the projector that fills it — read only, and never
        // started: the routes of Squid.Api/Posts resolve it, R-18 keeps it off the write path.
        builder.Services.AddSingleton(new SqliteFeedTimeline(connectionString));
        // Instance quota of SPEC-0009 section F (TASK-0028): the Dapper ledger the upload gate
        // reserves against, over the same migrated file. Registered, never started — the daily
        // blob sweep that corrects it is a Squid.Worker hosted service, off this write path.
        builder.Services.AddSingleton<IQuotaLedger>(_ => new SqliteQuotaLedger(connectionString));
        // GAP-17: one injectable clock for the section G timers — the succession inactivity and
        // the delete grace read it instead of the wall, so no test ever sleeps 90 days.
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

        builder.UseOrleans(silo =>
        {
            silo.Services.AddGrainStorage(StorageProviderName, (_, _) => storage);
            silo.AddDashboard();
            silo.UseLocalhostClustering();
        });
        builder.Services.AddSingleton(new StorageReadiness(connectionString));

        configure?.Invoke(builder);
        var app = builder.Build();

        app.UseRouting();

        // Liveness answers from memory: no database, no network (SPEC-0002).
        app.MapGet("/health", () => Results.Ok());
        // Readiness touches only the storage file: missing, renamed or denied means 503.
        app.MapGet("/ready", (StorageReadiness readiness) => readiness.IsAccessible()
            ? Results.Ok()
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        // Bare /dashboard must keep the natural 301 to /dashboard/ (TASK-0011): a rewritten 200
        // leaves the browser on the slashless URL, where the bundle's relative paths resolve to 404.
        app.MapOrleansDashboard("/dashboard");
        app.MapSquidApi();
        // Auth routes and the session/CSRF middleware (TASK-0014); one call site, owned by Auth/**.
        app.MapSquidAuth();
        // Container core (TASK-0017): create/read/edit plus the owner membership (SPEC-0008 A).
        // It maps after auth, so the session/CSRF middleware above already guards these routes.
        app.MapSquidContainers();
        // Publish, read and reply (TASK-0023): it maps after auth, so the session/CSRF middleware
        // above already guards these routes, and after the containers routes it shares visibility
        // rules and the trail with.
        app.MapSquidPosts();
        // Media upload and serving (TASK-0027): it maps after auth, so the session/CSRF middleware
        // above already guards both routes, and after posts, whose visibility ladder the media GET
        // reuses for the byte-identical 404 (SPEC-0009 section E, GAP-25 — contract §5).
        app.MapSquidMedia();
        // Feed and personal timeline with the ?since= delta (TASK-0025): it maps after auth, so
        // the session middleware above already guards the timeline, and after posts, whose
        // visibility ladder it reuses for the byte-identical 404 (SPEC-0009 section C).
        app.MapSquidFeed();
        // Live SSE stream (TASK-0026): GET /api/stream, mapped after auth so the session
        // middleware above already guards it, and after the publish routes whose writes wake it
        // (SPEC-0009 section D, contract §5 — event: post, Last-Event-ID, ping).
        app.MapSquidStream();
        // Threads (TASK-0033): create and list, mapped after auth so the session/CSRF middleware
        // above already guards both routes (SPEC-0010 section A, AC-04).
        app.MapSquidThreads();
        // Policy, trail and operator (TASK-0018): PUT /policy rides with the container routes,
        // and GET /api/admin/audit sits on the same session pipeline behind the operator gate
        // (SPEC-0008 B/C/D). The operator list is configuration — Instance:Operators, GAP-14.
        app.MapSquidAdmin();
        // Recovery kit (TASK-0016): generate, verify, import (SPEC-0007 B-12..B-15). It maps after
        // auth so the session/CSRF middleware above is already in the pipeline these routes run on.
        app.MapSquidRecovery();
        // Discovery (TASK-0015): webfinger + profile JSON-LD (SPEC-0007 B-09..B-11). The document
        // bytes come from Squid.Protocols through this one delegate — the edge never sees the
        // adapter (ADR-11), and every absolute URL derives from the request's own Host (B-09a).
        app.MapSquidDiscovery(ProfileJsonLd.Serialize);

        return app;
    }
}
