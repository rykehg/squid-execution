using Microsoft.AspNetCore.Http;
using Squid.Api.Discovery;
using Squid.Grains.Actors;
using Squid.Protocols.ActivityPub;

namespace Squid.Service;

/// <summary>
/// The signature gate the two inboxes are born behind (SPEC-0011 section C, contract §3 step 1,
/// TASK-0043): <c>false</c> means "no verifiable signature" and the route answers <c>401</c>
/// without the body ever being read; <c>true</c> means the signature held against a key this
/// instance publishes — together with the digest and the date the same verifier checks — and the
/// route answers <c>202</c> having applied nothing, because applying activities is TASK-0044.
///
/// Two boundaries are deliberate and both belong to that later task: only keys published by THIS
/// instance resolve (fetching a foreign actor with a TTL cache is step 7 of §3), and no size,
/// depth, rate limit or dedupe rule runs here (steps 4-6). Everything that arrives is therefore
/// either refused as unverifiable or accepted untouched — never half-processed.
/// </summary>
internal static class InboxSignatureGate
{
    /// <summary>True only for a request whose signature verifies against a key this instance publishes.</summary>
    public static async Task<bool> VerifyAsync(HttpContext context, ActorGrain actors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actors);

        var request = context.Request;

        // §3 step 1 first, and before the payload exists: a request carrying neither signature
        // header has nothing to verify, so the body is never read at all (AC-05).
        if (!request.Headers.ContainsKey("signature") && !request.Headers.ContainsKey("signature-input"))
        {
            return false;
        }

        var incoming = new IncomingRequest(
            request.Method,
            $"{request.Path.Value}{request.QueryString.Value}",
            request.Scheme,
            request.Host.Value ?? string.Empty,
            Headers(request),
            await BodyAsync(request, context.RequestAborted));

        var origin = CanonicalOrigin.Of(context);
        var verdict = await HttpSignatureVerifier.VerifyAsync(
            incoming, keyId => ResolveAsync(keyId, origin, actors));
        return verdict.Accepted;
    }

    /// <summary>
    /// The key material behind a <c>keyId</c>, as the wire carried it. Local ids only — one whose
    /// origin is not this instance's canonical one answers null, so the request is refused until
    /// TASK-0044 brings the remote resolver. Published material that will not decode resolves to
    /// nothing as well: the trust boundary never throws (FR-040).
    /// </summary>
    private static async Task<FederatedKey?> ResolveAsync(string keyId, string origin, ActorGrain actors)
    {
        const string marker = "/users/";
        if (string.IsNullOrEmpty(keyId) || !keyId.StartsWith(origin + marker, StringComparison.Ordinal))
        {
            return null;
        }

        var tail = keyId[(origin.Length + marker.Length)..];
        var fragment = tail.IndexOf('#');
        if (fragment <= 0)
        {
            return null; // an actor id without a key fragment is not a keyId this instance published
        }

        var handle = tail[..fragment];
        if (!ActorHandle.IsValid(handle))
        {
            return null;
        }

        var state = await actors.GetStateAsync(handle);
        var published = state?.Keys.PublicKeys.FirstOrDefault(
            key => string.Equals(key.KeyId, tail[fragment..], StringComparison.Ordinal));
        if (published is null)
        {
            return null;
        }

        try
        {
            var material = Convert.FromBase64String(published.PublicKey);
            return published.Algorithm switch
            {
                "RSA-2048" => new FederatedKey(material, null),   // SPKI DER, what ImportSubjectPublicKeyInfo reads
                "Ed25519" => new FederatedKey(null, material),    // the raw 32 bytes of RFC 8032
                _ => null,
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The headers as the wire carried them, multiple values joined with ", " (HTTP §5).</summary>
    private static IReadOnlyDictionary<string, string> Headers(HttpRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value.Select(value => value ?? string.Empty));
        }

        return headers;
    }

    /// <summary>
    /// The payload as it arrived. shortcut: no size or depth cap here — the 1 MB and depth-32
    /// rules of §3 step 4 belong to the TASK-0044 pipeline, and Kestrel's own body ceiling still
    /// bounds what a hostile caller can make this process buffer.
    /// </summary>
    private static async Task<byte[]> BodyAsync(HttpRequest request, CancellationToken aborted)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, aborted);
        return buffer.ToArray();
    }
}
