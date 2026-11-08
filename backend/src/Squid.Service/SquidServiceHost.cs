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
using Squid.Api.Auth;
using Squid.Api.Discovery;
using Squid.Api.Recovery;
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

    /// <summary>Builds the full application: silo, SQLite grain storage, /health, /ready, dashboard.</summary>
    public static WebApplication Build(string[] args)
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

        builder.UseOrleans(silo =>
        {
            silo.Services.AddGrainStorage(StorageProviderName, (_, _) => storage);
            silo.AddDashboard();
            silo.UseLocalhostClustering();
        });
        builder.Services.AddSingleton(new StorageReadiness(connectionString));

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
