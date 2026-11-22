using System.Security.Cryptography;
using System.Text;

namespace Squid.Worker.Backup;

/// <summary>
/// The hand-written AWS Signature version 4 core SPEC-0012 section B fixes (GAP-52): canonical
/// request, string to sign, the HMAC derivation chain and the <c>Authorization</c> header —
/// framework crypto only, zero new packages (AC-01). The in-process fake S3 of the tests
/// re-derives the same algorithm from the spec with its OWN implementation, which is what makes
/// the signature check of AC-02 a real one instead of a shared-code agreement.
/// </summary>
internal static class S3SigV4
{
    /// <summary>The algorithm name the <c>Authorization</c> header and string to sign open with.</summary>
    public const string Algorithm = "AWS4-HMAC-SHA256";

    /// <summary>SHA-256 of the empty payload — what a bodyless GET/HEAD/DELETE commits to.</summary>
    public const string EmptyPayloadHash =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    /// <summary>Lowercase hex SHA-256 of <paramref name="data"/> — the <c>x-amz-content-sha256</c> value.</summary>
    public static string HashHex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>
    /// RFC 3986 percent-encoding with uppercase hex — the AWS canonical form. Object keys are
    /// plain names of unreserved characters (the target refuses anything else), so on the path
    /// this is the identity; the query rides it for real endpoints whose continuation tokens
    /// carry <c>+ / =</c>.
    /// </summary>
    public static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in Encoding.UTF8.GetBytes(value))
        {
            var letter = (char)character;
            if (IsUnreserved(letter))
            {
                builder.Append(letter);
            }
            else
            {
                builder.Append('%').Append(character.ToString("X2"));
            }
        }

        return builder.ToString();
    }

    /// <summary>The canonical query string: pairs sorted by key, each side AWS-encoded.</summary>
    public static string CanonicalQuery(IReadOnlyList<KeyValuePair<string, string>> parameters) =>
        string.Join('&', parameters
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .Select(parameter => $"{Encode(parameter.Key)}={Encode(parameter.Value)}"));

    /// <summary>
    /// The <c>Authorization</c> value of one request: canonical request → string to sign →
    /// HMAC-SHA256 derivation chain (<c>AWS4+secret</c> → date → region → service →
    /// <c>aws4_request</c>) → hex signature, exactly the SigV4 algorithm S3 validates.
    /// </summary>
    public static string Sign(
        string method,
        string canonicalUri,
        string canonicalQuery,
        string host,
        string payloadHash,
        string amzDate,
        string accessKey,
        string secretKey,
        string region)
    {
        // Only these three headers are signed; x-amz-* must always be among them (S3's rule).
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalHeaders =
            $"host:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";

        var canonical = string.Join(
            '\n', method, canonicalUri, canonicalQuery, canonicalHeaders, signedHeaders, payloadHash);
        var dateStamp = amzDate[..8];
        var scope = $"{dateStamp}/{region}/s3/aws4_request";
        var toSign = string.Join(
            '\n', Algorithm, amzDate, scope, HashHex(Encoding.UTF8.GetBytes(canonical)));

        var key = DeriveSigningKey(secretKey, dateStamp, region);
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(toSign)));
        CryptographicOperations.ZeroMemory(key);

        return $"{Algorithm} Credential={accessKey}/{scope}, "
            + $"SignedHeaders={signedHeaders}, Signature={signature}";
    }

    /// <summary>The <c>AWS4+secret</c> → date → region → s3 → aws4_request HMAC chain.</summary>
    private static byte[] DeriveSigningKey(string secretKey, string dateStamp, string region)
    {
        static byte[] Hmac(byte[] key, string text) =>
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text));

        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretKey), dateStamp);
        key = Hmac(key, region);
        key = Hmac(key, "s3");
        return Hmac(key, "aws4_request");
    }

    /// <summary>RFC 3986 unreserved characters: ALPHA / DIGIT / "-" / "." / "_" / "~".</summary>
    private static bool IsUnreserved(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '-' or '.' or '_' or '~';
}
