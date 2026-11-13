using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Squid.Grains.Actors;

namespace Squid.Api.Discovery;

/// <summary>
/// Discovery surface of SPEC-0007 B-09..B-11: webfinger resolves a local handle and
/// <c>/users/{name}</c> serves the profile as JSON-LD with no session (FR-003). The instance
/// domain is the <c>Host</c> of the request itself (B-09a) — or the configured
/// <c>Instance:Domain</c> when the operator published one (SPEC-0011 section A): <c>subject</c>,
/// <c>link.href</c> and the document id all derive from that one origin
/// (<see cref="CanonicalOrigin"/>), a foreign domain answers 404 and nothing proxies it. The
/// JSON-LD bytes are produced by <c>Squid.Protocols</c> through the delegate the composition root
/// passes in — the mapping lives on the protocol side of the boundary (ADR-11) and this assembly
/// references no adapter (its .csproj is outside this task's touches).
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
    /// resource must be a well-formed acct address on a domain this instance owns — the
    /// configured <c>Instance:Domain</c> when the operator published one, or the Host the caller
    /// reached it on — and whose handle the rule accepts; only then is the local actor looked
    /// up. Unknown, malformed and foreign-domain resources all answer the same 404, so the
    /// endpoint never tells a caller what exists (contract §7 item 9).
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

        // B-09a (SPEC-0011 section A): the domains this instance answers for — the configured
        // Instance:Domain when the operator published one, and the Host the caller reached it on
        // — anything else is 404, no proxying. The PUBLISHED subject and href below are always
        // the canonical ones, so an internal authority is never echoed back as an identity.
        var canonical = CanonicalOrigin.Domain(context);
        var isOwn = string.Equals(domain, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)
            || (canonical is not null
                && string.Equals(domain, canonical, StringComparison.OrdinalIgnoreCase));
        if (!isOwn)
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
                subject = $"acct:{name}@{canonical ?? domain}",
                links = new[]
                {
                    new { rel = "self", type = ActivityJson, href = $"{origin}/users/{name}" },
                },
            },
            statusCode: StatusCodes.Status200OK,
            contentType: JrdJson);
    }

    /// <summary>
    /// The one origin of B-09a (SPEC-0011 section A): the configured <c>Instance:Domain</c> over
    /// https when the operator published one, else scheme + Host of the request that asked —
    /// forwarded headers only when <c>Instance:ForwardedHeaders</c> declared a trusted proxy
    /// (the composition root rewrites <c>Scheme</c>/<c>Host</c> before this runs).
    /// </summary>
    private static string Origin(HttpContext context) => CanonicalOrigin.Of(context);
}
