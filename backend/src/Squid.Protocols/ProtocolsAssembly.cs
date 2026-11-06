namespace Squid.Protocols;

/// <summary>
/// Marker type so the assembly is addressable before the adapter exists.
/// <c>IFederationProtocol</c> and the ActivityPub adapter belong to the federation slice;
/// JSON-LD stays on this side of the boundary and never reaches <c>Squid.Domain</c> (ADR-11).
/// </summary>
public static class ProtocolsAssembly
{
    /// <summary>Name of the assembly that hosts the protocol adapters.</summary>
    public const string Name = "Squid.Protocols";
}