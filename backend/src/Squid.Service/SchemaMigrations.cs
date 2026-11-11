using System.Text;
using Squid.Persistence;

namespace Squid.Service;

/// <summary>
/// The versioned schema scripts where this composition root ships them (SPEC-0009 section B):
/// the files live in <c>backend/src/Squid.Persistence/schema/*.sql</c> — the storage project's
/// canonical home — and are embedded in THIS assembly, so the init path applies them from any
/// working directory without reaching for the file system. <see cref="SquidServiceHost.Build"/>
/// calls <see cref="Apply"/> on every dev/test/CI host, which is exactly what the human gate of
/// 2026-11-09 liberates (GAP-19); applying the scripts to PRODUCTION stays a human gate.
/// </summary>
public static class SchemaMigrations
{
    /// <summary>Manifest prefix of an embedded script: <c>schema.0001_init_projections.sql</c>.</summary>
    private const string ResourcePrefix = "schema.";

    private const string ResourceSuffix = ".sql";

    /// <summary>Separator between the version number and the name inside a file name.</summary>
    private const char VersionSeparator = '_';

    /// <summary>
    /// Every embedded script, ordered by version. A resource that does not carry the
    /// <c>NNNN_name.sql</c> shape is a wiring mistake, so it fails here rather than silently
    /// never being applied.
    /// </summary>
    public static IReadOnlyList<MigrationScript> Load()
    {
        var assembly = typeof(SchemaMigrations).Assembly;
        var scripts = new List<MigrationScript>();

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                                    && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            var file = resource[ResourcePrefix.Length..^ResourceSuffix.Length];
            var separator = file.IndexOf(VersionSeparator);
            if (separator <= 0 || !int.TryParse(file[..separator], out var version))
            {
                throw new InvalidOperationException(
                    $"embedded schema resource '{resource}' must be named NNNN_name.sql");
            }

            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"embedded schema resource '{resource}' cannot be opened");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            scripts.Add(new MigrationScript(version, file[(separator + 1)..], reader.ReadToEnd()));
        }

        if (scripts.Count == 0)
        {
            throw new InvalidOperationException(
                "no schema script is embedded in this assembly (expected schema.NNNN_*.sql — "
                + "check the EmbeddedResource items of Squid.Service.csproj)");
        }

        return scripts;
    }

    /// <summary>Applies every pending script; returns the versions applied now (empty when none were pending).</summary>
    public static IReadOnlyList<int> Apply(string connectionString) =>
        SchemaMigrator.ApplyAsync(connectionString, Load()).GetAwaiter().GetResult();
}
