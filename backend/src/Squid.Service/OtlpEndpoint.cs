namespace Squid.Service;

/// <summary>
/// Resolution of the OTLP endpoint (SPEC-0002 GAP-09): the environment variable
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> wins, and without it the OTLP default
/// <c>http://localhost:4317</c> is used. Kept as a pure function so the resolved value can be
/// asserted by a test without a running exporter.
/// </summary>
public static class OtlpEndpoint
{
    /// <summary>Standard OTel environment variable holding the collector URL.</summary>
    public const string EnvironmentVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Default OTLP endpoint (gRPC) used when the environment variable is absent.</summary>
    public const string Default = "http://localhost:4317";

    /// <summary>Returns <paramref name="fromEnvironment"/> when it carries a URL, else <see cref="Default"/>.</summary>
    public static string Resolve(string? fromEnvironment) =>
        string.IsNullOrWhiteSpace(fromEnvironment) ? Default : fromEnvironment.Trim();
}
