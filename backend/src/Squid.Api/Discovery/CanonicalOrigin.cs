using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Squid.Api.Discovery;

/// <summary>
/// The one canonical origin every published id is built from (SPEC-0011 section A, I-11): the
/// configured <c>Instance:Domain</c> — always <c>https</c>, never derived from the request Host,
/// so an instance behind Caddy publishes the public domain — and, when the operator published no
/// domain, scheme + Host of the very request that asked: byte-identical to the rule the four
/// points had (GAP-45). <c>X-Forwarded-*</c> is not read here — it reaches <c>Scheme</c>/<c>Host</c>
/// only through the middleware the composition root installs when <c>Instance:ForwardedHeaders</c>
/// declares a trusted proxy, so the default pipeline never sees the headers at all. The key is
/// read from the request's own configuration, never from a static: one process may host a second
/// application with a different configuration (the tests do).
/// </summary>
public static class CanonicalOrigin
{
    /// <summary>The configured public domain, trimmed; null when the instance published none.</summary>
    public static string? Domain(HttpContext context)
    {
        var configured = context.RequestServices.GetService<IConfiguration>()?["Instance:Domain"]?.Trim();
        return string.IsNullOrEmpty(configured) ? null : configured;
    }

    /// <summary>The origin of every canonical id: the configured domain over https, else scheme + Host.</summary>
    public static string Of(HttpContext context) =>
        Domain(context) is { } domain
            ? $"https://{domain}"
            : $"{context.Request.Scheme}://{context.Request.Host.Value}";
}
