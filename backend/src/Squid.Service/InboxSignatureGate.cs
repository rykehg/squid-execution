using Microsoft.AspNetCore.Http;
using Squid.Grains.Actors;
using Squid.Protocols.ActivityPub;

namespace Squid.Service;

/// <summary>
/// The pieces contract activitypub §3 steps 1–3 are assembled from (SPEC-0011 section C): the
/// header probe that lets the pipeline refuse before the body exists, the body and header readers,
/// and the keyId resolver — local ids through the actor aggregate this instance publishes, remote
/// ones through <see cref="IFederatedActorClient"/>'s guarded fetch with its TTL cache (the two
/// boundaries TASK-0043's gate documented as TASK-0044's to bring). The order itself, the size,
/// depth, rate, dedupe and sender steps live in <see cref="InboxPipeline"/>.
/// </summary>
internal static class InboxSignatureGate
{
    /// <summary>True when the request carries any signature header at all — §3 step 1, before a body exists.</summary>
    public static bool HasSignatureHeaders(HttpRequest request) =>
        request.Headers.ContainsKey("signature") || request.Headers.ContainsKey("signature-input");

    /// <summary>
    /// The key material behind <paramref name="keyId"/>, as the wire carried it. An id on THIS
    /// instance's origin answers from the local aggregate and nothing else — the process never
    /// fetches itself — while any other origin goes to the remote client, whose failure is null:
    /// the trust boundary never throws (FR-040).
    /// </summary>
    public static async Task<FederatedKey?> ResolveKeyAsync(
        string keyId, string origin, ActorGrain actors, IFederatedActorClient peer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(keyId)) return null;

        if (keyId.StartsWith(origin + "/", StringComparison.Ordinal))
        {
            return await LocalKeyAsync(keyId, origin, actors);
        }

        return await peer.ResolveKeyAsync(keyId, cancellationToken);
    }

    /// <summary>The headers as the wire carried them, multiple values joined with ", " (HTTP §5).</summary>
    public static IReadOnlyDictionary<string, string> Headers(HttpRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value.Select(value => value ?? string.Empty));
        }

        return headers;
    }

    /// <summary>The payload as it arrived: the whole body, because the Digest of §3 step 2 covers it.</summary>
    public static async Task<byte[]> BodyAsync(HttpRequest request, CancellationToken aborted)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, aborted);
        return buffer.ToArray();
    }

    /// <summary>A key published under <c>{origin}/users/{handle}#…</c>, or null — never throws.</summary>
    private static async Task<FederatedKey?> LocalKeyAsync(string keyId, string origin, ActorGrain actors)
    {
        const string marker = "/users/";
        if (!keyId.StartsWith(origin + marker, StringComparison.Ordinal)) return null;

        var tail = keyId[(origin.Length + marker.Length)..];
        var fragment = tail.IndexOf('#');
        if (fragment <= 0)
        {
            return null; // an actor id without a key fragment is not a keyId this instance published
        }

        var handle = tail[..fragment];
        if (!ActorHandle.IsValid(handle)) return null;

        var state = await actors.GetStateAsync(handle);
        var published = state?.Keys.PublicKeys.FirstOrDefault(
            key => string.Equals(key.KeyId, tail[fragment..], StringComparison.Ordinal));
        if (published is null) return null;

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
}
