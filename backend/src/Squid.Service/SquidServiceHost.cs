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
using Squid.Api.Recovery;
using Squid.Domain;
using Squid.Grains.Actors;
using Squid.Persistence;
using Squid.Protocols.ActivityPub;

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
