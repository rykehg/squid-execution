namespace Squid.ReadModels;

/// <summary>
/// Marker type so the assembly is addressable before the projections exist.
/// Read projections are a separate path from the write side (doc 05 §4.4).
/// </summary>
public static class ReadModelsAssembly
{
    /// <summary>Name of the assembly that hosts the read projections.</summary>
    public const string Name = "Squid.ReadModels";
}