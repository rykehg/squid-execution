using System.Globalization;
using Microsoft.AspNetCore.Http;
using Squid.Api.Auth;

namespace Squid.Api.Federation;

/// <summary>
/// Content negotiation of contract activitypub §6 (SPEC-0011 section D, AC-06): the documents of
/// the federated surface are served only to a caller whose <c>Accept</c> carries
/// <c>application/activity+json</c> or <c>application/ld+json</c> — the ActivityStreams profile
/// parameter rides either type — or a wildcard; anything else is answered 406 before a single row
/// is read. An absent header means "anything" (RFC 9110 §12.5.1) and so does <c>*&#47;*</c>, which
/// is what every bare client and every test client sends; <c>q=0</c> refuses its type the way it
/// does in any other negotiation. The two media types are equal here on purpose: the same document
/// answers both spellings, and a peer picks one (contract §6).
/// </summary>
public static class AcceptNegotiation
{
    private const string ActivityJson = "application/activity+json";

    private const string LdJson = "application/ld+json";

    /// <summary>True when <paramref name="request"/>'s Accept admits the ActivityPub documents.</summary>
    public static bool Accepts(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var header = request.Headers.Accept;
        if (header.Count == 0)
        {
            return true; // no Accept at all: every representation is acceptable
        }

        foreach (var value in header)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // One header line carries a comma-separated list of entries; each entry is a media
            // range plus its parameters, and the range is the part before the first ";".
            foreach (var entry in value.Split(','))
            {
                var parts = entry.Split(';');
                var media = parts[0].Trim();
                if (media.Length == 0 || Quality(parts) <= 0)
                {
                    continue;
                }

                if (media.Equals("*", StringComparison.OrdinalIgnoreCase)
                    || media.Equals("*/*", StringComparison.OrdinalIgnoreCase)
                    || media.Equals("application/*", StringComparison.OrdinalIgnoreCase)
                    || media.Equals(ActivityJson, StringComparison.OrdinalIgnoreCase)
                    || media.Equals(LdJson, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The q parameter of one entry, default 1 — q=0 makes the entry unacceptable.</summary>
    private static double Quality(string[] parts)
    {
        foreach (var parameter in parts.Skip(1))
        {
            var separator = parameter.IndexOf('=');
            if (separator <= 0 || !parameter[..separator].Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return double.TryParse(
                parameter[(separator + 1)..].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 1; // an unparsable q is the default, never a silent refusal
        }

        return 1;
    }

    /// <summary>
    /// AC-06's single refusal, in one place so the profile document and the documents of
    /// <c>FederationEndpoints</c> can never answer it differently: null when the request may read,
    /// the <c>406</c> envelope when it may not — built BEFORE any lookup, so a caller that cannot
    /// read JSON-LD learns nothing about what exists.
    /// </summary>
    public static IResult? RefuseUnlessAccepted(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Accepts(context.Request)
            ? null
            : AuthErrors.Write(context, StatusCodes.Status406NotAcceptable, "not_acceptable",
                "a requisição não aceita JSON-LD ActivityPub (contract §6).", withTraceId: false);
    }
}
