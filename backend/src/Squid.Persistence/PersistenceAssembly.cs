namespace Squid.Persistence;

/// <summary>
/// Marker type for the assembly that owns SQL and the provider implementations.
/// <c>IGrainStorage</c> over SQLite landed with TASK-0003 (doc 05 §4.7 #1 and #2).
/// </summary>
public static class PersistenceAssembly
{
    /// <summary>Name of the assembly that owns SQL and the provider implementations.</summary>
    public const string Name = "Squid.Persistence";
}