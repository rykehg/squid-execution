using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace Squid.Service;

/// <summary>
/// Best-effort export (SPEC-0002): the OTLP exporter reports an unreachable collector as a
/// failed result, not as an exception, so the failure would otherwise only reach the OTel
/// EventSource and never the process log. This wrapper turns every failure — result code or
/// thrown exception — into a Warning naming the endpoint (AC-03) and returns
/// <see cref="ExportResult.Failure"/> either way: nothing is rethrown, so no request and no
/// host shutdown can fail because of a collector that is down.
/// </summary>
public sealed class WarningOtlpExporter(BaseExporter<Activity> inner, Uri endpoint, ILogger logger)
    : BaseExporter<Activity>
{
    public override ExportResult Export(in Batch<Activity> batch)
    {
        try
        {
            var result = inner.Export(in batch);
            if (result != ExportResult.Success)
            {
                logger.LogWarning("OTLP export failed for {Endpoint}: {Result}", endpoint, result);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OTLP export failed for {Endpoint}: {Message}", endpoint, ex.Message);
            return ExportResult.Failure;
        }
    }
}
