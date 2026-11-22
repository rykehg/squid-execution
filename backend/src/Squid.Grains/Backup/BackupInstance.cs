namespace Squid.Grains.Backup;

/// <summary>
/// The identity a backup run is keyed by (SPEC-0012 section A: the coordinator orchestrates "por
/// <c>instanceId</c>"). The key and its default live here, beside the coordinator, so the edge
/// route and the scheduler of the composition roots resolve the SAME value — one host serves one
/// instance, and the migrated SQLite file is what that instance is.
/// </summary>
public static class BackupInstance
{
    /// <summary>Configuration key of the instance id; absent means <see cref="DefaultId"/>.</summary>
    public const string IdKey = "Backup:InstanceId";

    /// <summary>The id an instance gets when no configuration names one.</summary>
    public const string DefaultId = "default";

    /// <summary>Resolves the configured id: blank or absent falls back to <see cref="DefaultId"/>.</summary>
    public static string Resolve(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultId : configured.Trim();
}
