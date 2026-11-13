using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Squid.Protocols.ActivityPub;

/// <summary>The three headers an outbound request carries after signing (contract activitypub §5).</summary>
public sealed record SignedHeaders(string Signature, string? Digest, string Date);

/// <summary>
/// The outbound request being signed: method, target (path + query, exactly as it will hit the
/// wire), Host, the key id that ends in <c>#main-key</c>, the body (null = no body, e.g. GET) and
/// the clock — tests pass their own timestamp, production passes now.
/// </summary>
public sealed record SignRequest(
    string Method,
    string Target,
    string Host,
    string KeyId,
    byte[]? Body,
    DateTimeOffset Timestamp);

/// <summary>
/// Outbound HTTP signature in the draft-cavage <c>rsa-sha256</c> format the ecosystem verifies
/// (contract activitypub §5, ADR-28): <c>algorithm="rsa-sha256"</c>, <c>headers</c> ordered
/// <c>(request-target) host date digest</c>, <c>Digest: SHA-256=</c> only (Mastodon rejects
/// sha-512), <c>Date</c> as an HTTP-date in GMT, a body-less request such as GET signing
/// <c>(request-target) host date</c> because Mastodon requires <c>host</c> on GET, and a
/// <c>keyId</c> that always points at <c>#main-key</c> (§1.1).
/// </summary>
public static class HttpSignatureSigner
{
    /// <summary>The only outbound algorithm of this phase (ADR-28).</summary>
    public const string Algorithm = "rsa-sha256";

    private const string MainKeyFragment = "#main-key";

    /// <summary>
    /// Signs <paramref name="request"/> — <paramref name="sign"/> receives the signing string and
    /// returns the raw RSA-SHA256 signature (the grain's <see cref="Squid.Grains.Actors.ActorSigner"/>
    /// hands over exactly that) — and returns the headers to send. Throws when the key id does not
    /// point at <c>#main-key</c>: the fragment is the contract, not a caller's choice.
    /// </summary>
    public static SignedHeaders Sign(SignRequest request, Func<byte[], byte[]> sign)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sign);

        if (!request.KeyId.EndsWith(MainKeyFragment, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"keyId must end in {MainKeyFragment} (contract activitypub §5): {request.KeyId}",
                nameof(request));
        }

        var date = request.Timestamp.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);
        var digest = request.Body is null
            ? null
            : "SHA-256=" + Convert.ToBase64String(SHA256.HashData(request.Body));

        // draft-cavage §2.3: the method is lowercased, the path never is; header names are
        // lowercased and OWS-trimmed, values ride as they are sent.
        var lines = new List<string>(4)
        {
            $"(request-target): {request.Method.ToLowerInvariant()} {request.Target}",
            $"host: {request.Host}",
            $"date: {date}",
        };

        string? headerNames = "(request-target) host date";
        if (digest is not null)
        {
            headerNames += " digest";
            lines.Add($"digest: {digest}");
        }

        var signingString = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        var signature = Convert.ToBase64String(sign(signingString));

        return new SignedHeaders(
            $"keyId=\"{request.KeyId}\",algorithm=\"{Algorithm}\","
            + $"headers=\"{headerNames}\",signature=\"{signature}\"",
            digest,
            date);
    }
}
