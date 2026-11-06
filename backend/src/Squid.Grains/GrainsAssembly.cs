namespace Squid.Grains;

/// <summary>
/// Marker type so the assembly is addressable before the first grain exists.
/// Grains arrive with the P-02 event sourcing slice; this file exists only to give
/// <c>Squid.Grains</c> a compiled identity today.
/// </summary>
public static class GrainsAssembly
{
    /// <summary>Name of the assembly that hosts the Orleans grains.</summary>
    public const string Name = "Squid.Grains";
}