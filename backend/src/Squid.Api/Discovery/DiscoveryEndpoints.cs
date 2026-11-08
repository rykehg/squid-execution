using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Squid.Grains.Actors;

namespace Squid.Api.Discovery;

/// <summary>
/// Discovery surface of SPEC-0007 B-09..B-11: webfinger resolves a local handle and
/// <c>/users/{name}</c> serves the profile as JSON-LD with no session (FR-003). The instance
/// domain is the <c>Host</c> of the request itself (B-09a): <c>subject</c>, <c>link.href</c> and
/// the document id all derive from that one origin, a foreign domain answers 404 and nothing
/// proxies it. The JSON-LD bytes are produced by <c>Squid.Protocols</c> through the delegate the
/// composition root passes in — the mapping lives on the protocol side of the boundary (ADR-11)
/// and this assembly references no adapter (its .csproj is outside this task's touches).
/// </summary>
public static class DiscoveryEndpoints
{
    /// <summary>Media type of the profile document (contract activitypub §6).</summary>
    private const string ActivityJson = "application/activity+json";

    /// <summary>Media type of a webfinger JRD (RFC 7033).</summary>
    private const string JrdJson = "application/jrd+json";

    /// <summary>
    /// Maps webfinger and the profile route. <paramref name="profileJsonLd"/> turns an actor state
    /// plus this request's origin into the JSON-LD document; it is the seam to Squid.Protocols.
    /// </summary>
    public static WebApplication MapSquidDiscovery(
        this WebApplication app, Func<ActorState, string, string> profileJsonLd)
    {
        app.MapGet("/.well-known/webfinger", WebFingerAsync);
        app.MapGet("/users/{name}", ProfileAsync);
        return app;

        // Local function: it captures the mapper seam, and an explicit Task<IResult> keeps the
        // route binding identical to the named handlers of AuthEndpoints.
        async Task<IResult> ProfileAsync(string name, ActorGrain actors, HttpContext context)
        {
            if (!ActorHandle.IsValid(name))
            {
                return Results.NotFound();
            }

            var state = await actors.GetStateAsync(name);
            if (state is null)
            {
                return Results.NotFound();
            }

            return Results.Text(profileJsonLd(state, Origin(context)), ActivityJson);
        }
    }

    /// <summary>
    /// <c>GET /.well-known/webfinger?resource=acct:{nome}@{dominio}</c> (B-09, B-09a): the
    /// resource must be a well-formed acct address whose domain is this very request's Host and
    /// whose handle the rule accepts; only then is the local actor looked up. Unknown, malformed
    /// and foreign-domain resources all answer the same 404, so the endpoint never tells a caller
    /// what exists (contract §7 item 9).
    /// </summary>
    private static async Task<IResult> WebFingerAsync(HttpContext context, ActorGrain actors)
    {
        var resource = context.Request.Query["resource"].ToString();
        if (!resource.StartsWith("acct:", StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var address = resource["acct:".Length..];
        var at = address.IndexOf('@');
        if (at <= 0 || at != address.LastIndexOf('@'))
        {
            return Results.NotFound();
        }

        var name = address[..at];
        var domain = address[(at + 1)..];
        if (!ActorHandle.IsValid(name))
        {
            return Results.NotFound();
        }

        // B-09a: the domain is this request's Host — anything else is 404, no proxying (I-11).
        if (!string.Equals(domain, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        var state = await actors.GetStateAsync(name);
        if (state is null)
        {
            return Results.NotFound();
        }

        var origin = Origin(context);
        return Results.Json(
            new
            {
                subject = $"acct:{name}@{domain}",
                links = new[]
                {
                    new { rel = "self", type = ActivityJson, href = $"{origin}/users/{name}" },
                },
            },
            statusCode: StatusCodes.Status200OK,
            contentType: JrdJson);
    }

    /// <summary>
    /// The one origin of B-09a: scheme + Host of the request that asked. Forwarded headers are
    /// deliberately ignored here — proxy/Caddy support is parked as I-11 for the proxy phase.
    /// </summary>
    private static string Origin(HttpContext context) =>
        $"{context.Request.Scheme}://{context.Request.Host.Value}";
}
