using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Squid.Protocols.ActivityPub;

/// <summary>Why a message was refused — the inbox pipeline maps each one to its status code (contract activitypub §3).</summary>
public enum SignatureRejection
{
    /// <summary>Signature verified, digest matched, date inside the window.</summary>
    None = 0,

    /// <summary>No usable Signature/Signature-Input pair reached the verifier.</summary>
    MissingSignature,

    /// <summary>The pair is present but unparsable, or a covered component cannot be derived.</summary>
    MalformedSignature,

    /// <summary>The stated algorithm is not one this verifier holds a key for (or the key type disagrees).</summary>
    UnsupportedAlgorithm,

    /// <summary>The keyId resolved to nothing published.</summary>
    UnresolvedKey,

    /// <summary>The signature does not verify against the key published at the keyId.</summary>
    SignatureInvalid,

    /// <summary>The digest header does not match the body that arrived.</summary>
    DigestMismatch,

    /// <summary>Date missing-and-required is the pipeline's job; this is an unparsable or stale one.</summary>
    StaleDate,
}

/// <summary>The verdict of contract §3: accepted, or the single step that refused the message.</summary>
public readonly record struct SignatureVerdict(SignatureRejection Rejection)
{
    /// <summary>True only when signature, digest and date all held.</summary>
    public bool Accepted => Rejection == SignatureRejection.None;
}

/// <summary>The key material published at a <c>keyId</c>: RSA as SPKI DER and/or raw Ed25519 (32 bytes).</summary>
public sealed record FederatedKey(byte[]? RsaSpki, byte[]? Ed25519);

/// <summary>
/// The inbound message under verification: the request line pieces, the Host, the headers as the
/// wire carried them (multiple values already joined with <c>, </c> as HTTP does) and the body.
/// </summary>
public sealed record IncomingRequest(
    string Method,
    string Target,
    string Scheme,
    string Host,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);

/// <summary>
/// Inbound HTTP signature verification of contract activitypub §3 (SPEC-0011 section C, ADR-28):
/// draft-cavage <c>rsa-sha256</c>/<c>hs2019</c> and RFC 9421 (<c>Signature-Input</c> +
/// <c>Signature</c>, RSA or Ed25519), executed in the fixed order the contract states — 1) the
/// signature against the key published at the keyId, 2) the digest of the body, 3) the Date inside
/// ±5 min. Body size, nesting depth, rate limit and dedupe are later steps of the same order and
/// belong to the inbox pipeline (TASK-0044), not to this library.
/// </summary>
public static class HttpSignatureVerifier
{
    /// <summary>Replay window of contract §3 step 3, both directions (SPEC-0011 non-functional).</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private static readonly SignatureVerdict Accepted = new(SignatureRejection.None);

    /// <summary>
    /// Verifies <paramref name="request"/> in the contract's fixed order, resolving the key
    /// material through <paramref name="resolveKey"/> (the caller owns fetching a remote actor
    /// document and its cache). Never throws on hostile input: every failure is a verdict.
    /// </summary>
    public static async Task<SignatureVerdict> VerifyAsync(
        IncomingRequest request, Func<string, Task<FederatedKey?>> resolveKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolveKey);

        // §3 step 1 — signature present and verifiable: RFC 9421 when Signature-Input rides along,
        // draft-cavage otherwise; the pair is chosen before any content is looked at.
        var verdict = TryGetHeader(request, "signature-input", out var signatureInput)
            ? await VerifyRfc9421Async(request, signatureInput, resolveKey)
            : await VerifyDraftCavageAsync(request, resolveKey);
        if (!verdict.Accepted)
        {
            return verdict;
        }

        // §3 step 2 — the digest of the body confers with the header.
        verdict = VerifyDigest(request);
        if (!verdict.Accepted)
        {
            return verdict;
        }

        // §3 step 3 — Date inside ±5 min (replay window).
        return VerifyDate(request);
    }

    /// <summary>draft-cavage: <c>Signature: keyId="…",algorithm="…",headers="…",signature="…"</c>.</summary>
    private static async Task<SignatureVerdict> VerifyDraftCavageAsync(
        IncomingRequest request, Func<string, Task<FederatedKey?>> resolveKey)
    {
        if (!TryGetHeader(request, "signature", out var header))
        {
            return new(SignatureRejection.MissingSignature);
        }

        var parameters = ParseAuthParameters(header);
        if (parameters is null
            || !parameters.TryGetValue("keyid", out var keyId) || string.IsNullOrEmpty(keyId)
            || !parameters.TryGetValue("signature", out var rawSignature) || string.IsNullOrEmpty(rawSignature))
        {
            return new(SignatureRejection.MalformedSignature);
        }

        parameters.TryGetValue("algorithm", out var algorithm);
        if (algorithm is not null
            && !algorithm.Equals("rsa-sha256", StringComparison.OrdinalIgnoreCase)
            && !algorithm.Equals("hs2019", StringComparison.OrdinalIgnoreCase))
        {
            return new(SignatureRejection.UnsupportedAlgorithm); // draft-cavage §2.1.3: algorithm must match the key
        }

        parameters.TryGetValue("created", out var created);
        parameters.TryGetValue("expires", out var expires);
        var headerNames = parameters.TryGetValue("headers", out var names) && !string.IsNullOrWhiteSpace(names)
            ? names.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { "(created)" }; // draft-cavage §2.1.6: absent headers means (created)

        var signingString = BuildCavageSigningString(request, headerNames, created, expires);
        if (signingString is null)
        {
            return new(SignatureRejection.MalformedSignature);
        }

        var signature = DecodeBase64(rawSignature);
        if (signature is null)
        {
            return new(SignatureRejection.MalformedSignature);
        }

        var key = await resolveKey(keyId);
        if (key is null)
        {
            return new(SignatureRejection.UnresolvedKey);
        }

        return new(VerifySignature(key, algorithm, signingString, signature));
    }

    /// <summary>RFC 9421: the labelled <c>Signature-Input</c> dictionary plus its <c>Signature</c> byte sequence.</summary>
    private static async Task<SignatureVerdict> VerifyRfc9421Async(
        IncomingRequest request, string signatureInput, Func<string, Task<FederatedKey?>> resolveKey)
    {
        if (!TryGetHeader(request, "signature", out var signatureField))
        {
            return new(SignatureRejection.MissingSignature);
        }

        var inputs = ParseLabelledField(signatureInput);
        var signatures = ParseLabelledField(signatureField);
        if (inputs is null || signatures is null)
        {
            return new(SignatureRejection.MalformedSignature);
        }

        // Policy of this verifier: the first label carried by both fields (RFC 9421 §3.2 step 1.1).
        var label = inputs.Keys.FirstOrDefault(signatures.ContainsKey);
        if (label is null)
        {
            return new(SignatureRejection.MissingSignature);
        }

        var rawParameters = inputs[label];
        var components = ParseCoveredComponents(rawParameters, out var parameters);
        if (components is null || parameters is null)
        {
            return new(SignatureRejection.MalformedSignature);
        }

        if (!parameters.TryGetValue("keyid", out var keyId) || string.IsNullOrEmpty(keyId))
        {
            return new(SignatureRejection.MalformedSignature);
        }

        var signingString = Build9421SigningString(request, components, rawParameters);
        if (signingString is null)
        {
            return new(SignatureRejection.MalformedSignature);
        }

        var signature = DecodeByteSequence(signatures[label]);
        if (signature is null)
        {
            return new(SignatureRejection.MalformedSignature);
        }

        var key = await resolveKey(keyId);
        if (key is null)
        {
            return new(SignatureRejection.UnresolvedKey);
        }

        parameters.TryGetValue("alg", out var algorithm);
        return new(VerifySignature(key, algorithm, signingString, signature));
    }

    /// <summary>
    /// The cryptographic step: the algorithm comes from the signature parameters when stated and
    /// from the key material when not (RFC 9421 §3.2 step 6, draft-cavage §2.1.3). Any crypto
    /// failure is a rejection — the trust boundary never throws (FR-040).
    /// </summary>
    private static SignatureRejection VerifySignature(
        FederatedKey key, string? algorithm, byte[] signingString, byte[] signature)
    {
        switch (algorithm?.Trim().Trim('"').ToLowerInvariant())
        {
            case null or "" or "hs2019":
            {
                // Not stated, or the draft's "derive from the key" label: RSA first, because that
                // is what the ecosystem signs; a pure Ed25519 key falls through to its own check.
                var tried = false;
                if (key.RsaSpki is not null)
                {
                    tried = true;
                    if (RsaVerify(key.RsaSpki, signingString, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                        || RsaVerify(key.RsaSpki, signingString, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                    {
                        return SignatureRejection.None;
                    }
                }

                if (key.Ed25519 is not null)
                {
                    tried = true;
                    if (Ed25519Verify(key.Ed25519, signingString, signature))
                    {
                        return SignatureRejection.None;
                    }
                }

                return tried ? SignatureRejection.SignatureInvalid : SignatureRejection.UnsupportedAlgorithm;
            }

            // rsa-sha256 is draft-cavage's own label; rsa-v1_5-sha256 is RFC 9421 §3.3.2 — the same
            // primitive (RSASSA-PKCS1-v1_5 with SHA-256), which is exactly what a peer re-labels.
            case "rsa-sha256" or "rsa-v1_5-sha256":
                return key.RsaSpki is null
                    ? SignatureRejection.UnsupportedAlgorithm
                    : RsaVerify(key.RsaSpki, signingString, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                        ? SignatureRejection.None
                        : SignatureRejection.SignatureInvalid;

            case "rsa-v1_5-sha384":
                return Rsa(key.RsaSpki, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1);

            case "rsa-v1_5-sha512":
                return Rsa(key.RsaSpki, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);

            case "rsa-pss-sha256":
                return Rsa(key.RsaSpki, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

            case "rsa-pss-sha384":
                return Rsa(key.RsaSpki, HashAlgorithmName.SHA384, RSASignaturePadding.Pss);

            case "rsa-pss-sha512":
                return Rsa(key.RsaSpki, HashAlgorithmName.SHA512, RSASignaturePadding.Pss);

            case "ed25519":
                return key.Ed25519 is null
                    ? SignatureRejection.UnsupportedAlgorithm
                    : Ed25519Verify(key.Ed25519, signingString, signature)
                        ? SignatureRejection.None
                        : SignatureRejection.SignatureInvalid;

            default:
                // hmac-*, ecdsa-*, json-web-signature: no key of that kind exists in this instance.
                return SignatureRejection.UnsupportedAlgorithm;
        }

        SignatureRejection Rsa(byte[]? spki, HashAlgorithmName hash, RSASignaturePadding padding) =>
            spki is null
                ? SignatureRejection.UnsupportedAlgorithm
                : RsaVerify(spki, signingString, signature, hash, padding)
                    ? SignatureRejection.None
                    : SignatureRejection.SignatureInvalid;
    }

    /// <summary>RSASSA over the signing string; a broken key or a malformed signature is a false, never a throw.</summary>
    private static bool RsaVerify(
        byte[] spki, byte[] signingString, byte[] signature, HashAlgorithmName hash, RSASignaturePadding padding)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(spki, out _);
            return rsa.VerifyData(signingString, signature, hash, padding);
        }
        catch (Exception)
        {
            return false; // trust boundary: any crypto failure rejects (FR-040)
        }
    }

    /// <summary>Ed25519 (RFC 8032, pure — RFC 9421 §3.3.6 signs the signature base as it stands).</summary>
    private static bool Ed25519Verify(byte[] publicKey, byte[] signingString, byte[] signature)
    {
        if (publicKey.Length != 32 || signature.Length != 64)
        {
            return false;
        }

        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(signingString, 0, signingString.Length);
            return verifier.VerifySignature(signature);
        }
        catch (Exception)
        {
            return false; // trust boundary: any crypto failure rejects (FR-040)
        }
    }

    /// <summary>§3 step 2 — whichever digest header the sender used must match the body that arrived.</summary>
    private static SignatureVerdict VerifyDigest(IncomingRequest request)
    {
        var expected = Convert.ToBase64String(SHA256.HashData(request.Body));

        // Content-Digest (RFC 9530, the RFC 9421 carrier): sha-256=:<base64>: …
        if (TryGetHeader(request, "content-digest", out var contentDigest)
            && !MatchesContentDigest(contentDigest, expected))
        {
            return new(SignatureRejection.DigestMismatch);
        }

        // Digest (RFC 3230, the draft-cavage carrier): SHA-256=<base64> … and SHA-256 only, which
        // is the algorithm this instance accepts in either direction (ADR-28).
        if (TryGetHeader(request, "digest", out var digest) && !MatchesDigest(digest, expected))
        {
            return new(SignatureRejection.DigestMismatch);
        }

        return Accepted;
    }

    /// <summary>True when the Digest header carries a <c>SHA-256</c> entry equal to <paramref name="expected"/>.</summary>
    private static bool MatchesDigest(string header, string expected)
    {
        foreach (var entry in header.Split(','))
        {
            var candidate = entry.Trim();
            var separator = candidate.IndexOf('=');
            if (separator <= 0
                || !candidate[..separator].Trim().Equals("SHA-256", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(candidate[(separator + 1)..].Trim(), expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the Content-Digest header carries a <c>sha-256</c> member equal to <paramref name="expected"/>.</summary>
    private static bool MatchesContentDigest(string header, string expected)
    {
        foreach (var entry in header.Split(','))
        {
            var candidate = entry.Trim();
            var separator = candidate.IndexOf('=');
            if (separator <= 0
                || !candidate[..separator].Trim().Equals("sha-256", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = candidate[(separator + 1)..].Trim();
            if (value.Length > 2 && value[0] == ':' && value[^1] == ':'
                && string.Equals(value[1..^1], expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// §3 step 3 — HTTP-date in GMT, now ±5 min. A request carrying no Date has no clock for this
    /// library to judge: requiring the header itself is the inbox pipeline's step, not a guess
    /// this layer makes.
    /// </summary>
    private static SignatureVerdict VerifyDate(IncomingRequest request)
    {
        if (!TryGetHeader(request, "date", out var date))
        {
            return Accepted;
        }

        if (!DateTime.TryParseExact(date.Trim(), "r", CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var when))
        {
            return new(SignatureRejection.StaleDate);
        }

        var stamped = new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Utc));
        return Math.Abs((DateTimeOffset.UtcNow - stamped).TotalMinutes) > Window.TotalMinutes
            ? new(SignatureRejection.StaleDate)
            : Accepted;
    }

    /// <summary>The signing string of draft-cavage §2.3 — one <c>name: value</c> line per listed header, no trailing newline.</summary>
    private static byte[]? BuildCavageSigningString(
        IncomingRequest request, string[] headerNames, string? created, string? expires)
    {
        if (headerNames.Length == 0)
        {
            return null; // a zero-length headers parameter signs the empty string — refused (§2.1.6)
        }

        var lines = new List<string>(headerNames.Length);
        foreach (var name in headerNames)
        {
            var lower = name.ToLowerInvariant();
            string? value = lower switch
            {
                // draft-cavage §2.3 step 1: lowercased method, path untouched.
                "(request-target)" => $"{request.Method.ToLowerInvariant()} {request.Target}",
                "(created)" => long.TryParse(created, out var at) ? at.ToString(CultureInfo.InvariantCulture) : null,
                "(expires)" => long.TryParse(expires, out var until) ? until.ToString(CultureInfo.InvariantCulture) : null,
                _ => TryGetHeader(request, lower, out var header) ? header : null,
            };

            if (value is null)
            {
                return null; // a covered component that cannot be derived is an error (§2.5)
            }

            lines.Add($"{lower}: {value}");
        }

        return Encoding.UTF8.GetBytes(string.Join('\n', lines));
    }

    /// <summary>
    /// The signature base of RFC 9421 §2.5: each covered component as <c>"name": value</c> lines
    /// and the <c>"@signature-params"</c> line last — every line ends with LF except that final
    /// one, whose value is the Signature-Input entry exactly as the sender serialized it.
    /// </summary>
    private static byte[]? Build9421SigningString(
        IncomingRequest request, IReadOnlyList<Component> components, string rawParameters)
    {
        if (components.Count == 0)
        {
            return null;
        }

        var lines = new List<string>(components.Count + 1);
        foreach (var component in components)
        {
            var value = component.Name.StartsWith('@')
                ? DeriveComponent(request, component.Name)
                : TryGetHeader(request, component.Name, out var header) ? header : null;
            if (value is null)
            {
                return null; // unknown derived name or an absent covered field (§2.5 error)
            }

            lines.Add($"{component.Identifier}: {value}");
        }

        lines.Add($"\"@signature-params\": {rawParameters}");
        return Encoding.UTF8.GetBytes(string.Join('\n', lines));
    }

    /// <summary>The derived components of RFC 9421 §2.2 this verifier can pull from a request.</summary>
    private static string? DeriveComponent(IncomingRequest request, string name) => name switch
    {
        // §2.2.6: the method rides exactly as it arrived (HTTP methods are case-sensitive).
        "@method" => request.Method,
        "@target-uri" => $"{request.Scheme}://{request.Host}{request.Target}",
        "@authority" => request.Host,
        "@scheme" => request.Scheme,
        "@path" => TargetPath(request.Target),
        "@query" => TargetQuery(request.Target),
        _ => null, // @status-code and unknown names: not derivable from a request
    };

    private static string TargetPath(string target)
    {
        var query = target.IndexOf('?');
        var path = query >= 0 ? target[..query] : target;
        return path.Length == 0 ? "/" : path;
    }

    private static string TargetQuery(string target)
    {
        var query = target.IndexOf('?');
        return query >= 0 ? target[query..] : string.Empty;
    }

    private sealed record Component(string Name, string Identifier);

    /// <summary>
    /// Parses the value after a Signature-Input label: the parenthesised covered-component list
    /// followed by the <c>;name=value</c> signature parameters. Component parameters
    /// (<c>"digest";sf</c> and friends) are refused rather than half-supported
    /// shortcut: sf/bs/key component parameters unsupported — upgrade when a peer signs one.
    /// </summary>
    private static List<Component>? ParseCoveredComponents(
        string rawParameters, out Dictionary<string, string>? parameters)
    {
        parameters = null;
        if (rawParameters.Length == 0 || rawParameters[0] != '(')
        {
            return null;
        }

        var inQuote = false;
        var escaped = false;
        var close = -1;
        for (var i = 1; i < rawParameters.Length; i++)
        {
            var character = rawParameters[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inQuote && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && character == ')')
            {
                close = i;
                break;
            }
        }

        if (close < 0 || inQuote)
        {
            return null;
        }

        var components = ParseComponentList(rawParameters[1..close]);
        if (components is null || components.Count == 0)
        {
            return null;
        }

        var rest = rawParameters[(close + 1)..];
        if (rest.Length == 0)
        {
            parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return components;
        }

        if (rest[0] != ';')
        {
            return null;
        }

        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in SplitTopLevel(rest[1..], ';'))
        {
            var entry = chunk.Trim();
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                return null;
            }

            var name = entry[..separator].Trim();
            if (parsed.ContainsKey(name))
            {
                return null;
            }

            var value = entry[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            parsed[name] = value;
        }

        parameters = parsed;
        return components;
    }

    /// <summary>The quoted component names of an inner list, each with the exact identifier it was written as.</summary>
    private static List<Component>? ParseComponentList(string inner)
    {
        var components = new List<Component>();
        var index = 0;
        while (index < inner.Length)
        {
            if (char.IsWhiteSpace(inner[index]))
            {
                index++;
                continue;
            }

            if (inner[index] != '"')
            {
                return null; // component names are serialized as sf-strings (§2.5)
            }

            var start = index;
            index++;
            var name = new StringBuilder();
            var closed = false;
            while (index < inner.Length)
            {
                if (inner[index] == '\\' && index + 1 < inner.Length)
                {
                    name.Append(inner[index + 1]);
                    index += 2;
                    continue;
                }

                if (inner[index] == '"')
                {
                    closed = true;
                    index++;
                    break;
                }

                name.Append(inner[index]);
                index++;
            }

            if (!closed)
            {
                return null;
            }

            if (index < inner.Length && !char.IsWhiteSpace(inner[index]))
            {
                return null; // component parameters are not supported here (see ParseCoveredComponents)
            }

            components.Add(new Component(name.ToString(), inner[start..index]));
        }

        return components;
    }

    /// <summary>draft-cavage auth-params: <c>name=value</c> with quoted values; a duplicate name refuses the whole header (§2.2).</summary>
    private static Dictionary<string, string>? ParseAuthParameters(string header)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in SplitTopLevel(header, ','))
        {
            var entry = chunk.Trim();
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                return null;
            }

            var name = entry[..separator].Trim();
            if (parameters.ContainsKey(name))
            {
                return null;
            }

            var value = entry[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            parameters[name] = value;
        }

        return parameters.Count == 0 ? null : parameters;
    }

    /// <summary>The labelled dictionary of RFC 9421 §4 (<c>sig1=value,…</c>), labels compared exactly.</summary>
    private static Dictionary<string, string>? ParseLabelledField(string field)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chunk in SplitTopLevel(field, ','))
        {
            var entry = chunk.Trim();
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                return null;
            }

            var label = entry[..separator].Trim();
            if (entries.ContainsKey(label))
            {
                return null;
            }

            entries[label] = entry[(separator + 1)..].Trim();
        }

        return entries.Count == 0 ? null : entries;
    }

    /// <summary>Splits on <paramref name="separator"/> outside double quotes; backslash escapes inside a quote.</summary>
    private static IEnumerable<string> SplitTopLevel(string value, char separator)
    {
        var start = 0;
        var inQuote = false;
        var escaped = false;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inQuote && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (character == separator && !inQuote)
            {
                yield return value[start..i];
                start = i + 1;
            }
        }

        yield return value[start..];
    }

    /// <summary>Header lookup, case-insensitively — the wire is case-insensitive and callers hand over what arrived.</summary>
    private static bool TryGetHeader(IncomingRequest request, string name, out string value)
    {
        foreach (var header in request.Headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = header.Value.Trim();
                return true;
            }
        }

        if (name.Equals("host", StringComparison.OrdinalIgnoreCase))
        {
            value = request.Host; // the authority also travels as the request's own field
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Standard base64 (RFC 4648 §4) as both draft-cavage and SF byte sequences carry it.</summary>
    private static byte[]? DecodeBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>An SF byte sequence <c>:base64:</c>; a bare base64 body is accepted the same way.</summary>
    private static byte[]? DecodeByteSequence(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == ':' && trimmed[^1] == ':')
        {
            trimmed = trimmed[1..^1];
        }

        return DecodeBase64(trimmed);
    }
}
