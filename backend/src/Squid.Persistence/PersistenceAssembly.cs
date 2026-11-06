namespace Squid.Persistence;

/// <summary>
/// Marker type so the assembly is addressable before the storage contract exists.
/// <c>IGrainStorage</c> over SQLite lands with TASK-0003 (doc 05 §4.7 #1 and #2).
/// </summary>
public static class PersistenceAssembly
{
    /// <summary>Name of the assembly that owns SQL and the provider implementations.</summary>
    public const string Name = "Squid.Persistence";
}