using Microsoft.AspNetCore.Builder;

namespace Squid.Api;

/// <summary>
/// The HTTP edge. It owns no route yet: endpoints arrive with the identity slice (P-02).
/// The composition root decides what runs; <c>Squid.Api</c> only exposes the surface.
/// </summary>
public static class SquidApi
{
    /// <summary>Name of the assembly that hosts the HTTP surface.</summary>
    public const string Name = "Squid.Api";

    /// <summary>
    /// Maps the API surface onto the given application. Currently empty by design: this
    /// method exists so the composition root has one call site to wire when endpoints land.
    /// </summary>
    /// <param name="app">The application being composed.</param>
    /// <returns>The same application, so registration can be chained.</returns>
    public static WebApplication MapSquidApi(this WebApplication app) => app;
}