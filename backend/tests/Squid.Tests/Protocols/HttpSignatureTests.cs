using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Squid.Protocols.ActivityPub;

namespace Squid.Tests.Protocols;

/// <summary>
/// AC-04/AC-05 of TASK-0042 (SPEC-0011 section C, contract activitypub §3/§5): the outbound
/// draft-cavage shape under an independent verifier — the signing string here is rebuilt from the
/// contract text, never through the library — and the inbound acceptance/rejection the contract
/// fixes. Every signature the verifier is asked to accept is built in this file.
/// </summary>
public sealed class HttpSignatureTests
{
    private const string Host = "silo-a.test";
    private const string Scheme = "https";
    private const string Target = "/users/alice/inbox";
    private const string KeyId = "https://silo-a.test/users/alice#main-key";

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"type\":\"Create\",\"content\":\"ola\"}");

    [Fact] // AC-04
    public async Task Ac04_post_signature_matches_contract_5_and_an_independent_verifier_accepts_the_pair()
    {
        using var rsa = RSA.Create(2048);
        var request = new SignRequest("POST", Target, Host, KeyId, Body, DateTimeOffset.UtcNow);
        var signed = HttpSignatureSigner.Sign(
            request, data => rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        // The format of contract §5, field by field.
        var parameters = ParseAuthParameters(signed.Signature);
        Assert.EndsWith("#main-key", parameters["keyId"], StringComparison.Ordinal);
        Assert.Equal("rsa-sha256", parameters["algorithm"]);
        Assert.Equal("(request-target) host date digest", parameters["headers"]);
        Assert.Equal(
            "SHA-256=" + Convert.ToBase64String(SHA256.HashData(Body)),
            signed.Digest);

        // Date as an HTTP-date in GMT (§5), parseable and inside the window.
        Assert.EndsWith(" GMT", signed.Date, StringComparison.Ordinal);
        Assert.True(DateTime.TryParseExact(signed.Date, "r", CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var when));
        Assert.True(Math.Abs(
            (DateTimeOffset.UtcNow - new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Utc))).TotalMinutes)
            < 5);

        // The independent verifier: lines rebuilt from the contract, in the order the headers
        // parameter declares, checked with raw RSA — the library is not consulted for this half.
        var lines = new List<string>();
        foreach (var component in parameters["headers"].Split(' '))
        {
            lines.Add(component switch
            {
                "(request-target)" => "(request-target): post " + request.Target,
                "host" => "host: " + request.Host,
                "date" => "date: " + signed.Date,
                "digest" => "digest: " + signed.Digest,
                _ => throw new InvalidOperationException($"unexpected covered header '{component}'"),
            });
        }

        Assert.True(rsa.VerifyData(
                Encoding.UTF8.GetBytes(string.Join('\n', lines)),
                Convert.FromBase64String(parameters["signature"]),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1),
            "the independent verifier did not accept the signature our signer produced");

        // And our own verifier accepts the same message: the sign → verify pair round-trips.
        var verdict = await HttpSignatureVerifier.VerifyAsync(
            new IncomingRequest("POST", Target, Scheme, Host, Headers(
                ("date", signed.Date),
                ("digest", signed.Digest!),
                ("signature", signed.Signature)),
                Body),
            Resolver(KeyId, new FederatedKey(rsa.ExportSubjectPublicKeyInfo(), null)));

        Assert.True(verdict.Accepted, verdict.Rejection.ToString());
    }

    [Fact] // AC-04 (GET half)
    public async Task Ac04_get_signs_request_target_host_date_and_no_digest()
    {
        using var rsa = RSA.Create(2048);
        var request = new SignRequest("GET", "/users/alice/outbox?page=2", Host, KeyId, null, DateTimeOffset.UtcNow);
        var signed = HttpSignatureSigner.Sign(
            request, data => rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        var parameters = ParseAuthParameters(signed.Signature);
        Assert.Equal("(request-target) host date", parameters["headers"]);
        Assert.Null(signed.Digest); // a body-less request carries no Digest at all

        var signingString =
            "(request-target): get /users/alice/outbox?page=2\n" // lowercased method, query kept
            + "host: " + Host + "\n"
            + "date: " + signed.Date;
        Assert.True(rsa.VerifyData(
            Encoding.UTF8.GetBytes(signingString),
            Convert.FromBase64String(parameters["signature"]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        var verdict = await HttpSignatureVerifier.VerifyAsync(
            new IncomingRequest("GET", request.Target, Scheme, Host, Headers(
                ("date", signed.Date),
                ("signature", signed.Signature)),
                []),
            Resolver(KeyId, new FederatedKey(rsa.ExportSubjectPublicKeyInfo(), null)));

        Assert.True(verdict.Accepted, verdict.Rejection.ToString());
    }

    [Fact] // AC-04 (the fragment is the contract's, not the caller's)
    public void Ac04_a_key_id_without_the_main_key_fragment_is_refused()
    {
        var request = new SignRequest("GET", "/", Host, "https://silo-a.test/users/alice", null, DateTimeOffset.UtcNow);

        var failure = Assert.Throws<ArgumentException>(
            () => HttpSignatureSigner.Sign(request, _ => [1, 2, 3]));

        Assert.Contains("#main-key", failure.Message, StringComparison.Ordinal);
    }

    [Theory] // AC-05 (draft half)
    [InlineData("rsa-sha256")]
    [InlineData("hs2019")]
    public async Task Ac05_a_peers_draft_cavage_signature_is_accepted(string algorithm)
    {
        using var rsa = RSA.Create(2048);
        var date = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var digest = "SHA-256=" + Convert.ToBase64String(SHA256.HashData(Body));

        // Built straight from contract §5 — the peer's side, never through our signer.
        var signingString =
            $"(request-target): post {Target}\n"
            + $"host: {Host}\n"
            + $"date: {date}\n"
            + $"digest: {digest}";
        var signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(signingString), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var header =
            $"keyId=\"{KeyId}\",algorithm=\"{algorithm}\","
            + $"headers=\"(request-target) host date digest\",signature=\"{signature}\"";

        var verdict = await HttpSignatureVerifier.VerifyAsync(
            new IncomingRequest("POST", Target, Scheme, Host, Headers(
                ("date", date),
                ("digest", digest),
                ("signature", header)),
                Body),
            Resolver(KeyId, new FederatedKey(rsa.ExportSubjectPublicKeyInfo(), null)));

        Assert.True(verdict.Accepted, verdict.Rejection.ToString());
    }

    [Fact] // AC-05 (RFC 9421, RSA half)
    public async Task Ac05_an_rfc9421_signature_with_rsa_is_accepted()
    {
        using var rsa = RSA.Create(2048);
        var date = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var contentDigest = "sha-256=:" + Convert.ToBase64String(SHA256.HashData(Body)) + ":";
        var parameters =
            $"(\"@method\" \"@authority\" \"@path\" \"content-digest\");"
            + $"created={DateTimeOffset.UtcNow.ToUnixTimeSeconds()};"
            + $"keyid=\"{KeyId}\";alg=\"rsa-v1_5-sha256\"";

        // The signature base written out by hand from RFC 9421 §2.5: LF after every line, none
        // after "@signature-params", whose value is the Signature-Input entry exactly as sent.
        var signingString =
            "\"@method\": POST\n"
            + $"\"@authority\": {Host}\n"
            + $"\"@path\": {Target}\n"
            + $"\"content-digest\": {contentDigest}\n"
            + $"\"@signature-params\": {parameters}";
        var signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(signingString), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        var verdict = await HttpSignatureVerifier.VerifyAsync(
            new IncomingRequest("POST", Target, Scheme, Host, Headers(
                ("signature-input", $"sig1={parameters}"),
                ("signature", $"sig1=:{signature}:"),
                ("date", date),
                ("content-digest", contentDigest)),
                Body),
            Resolver(KeyId, new FederatedKey(rsa.ExportSubjectPublicKeyInfo(), null)));

        Assert.True(verdict.Accepted, verdict.Rejection.ToString());
    }

    [Fact] // AC-05 (RFC 9421, Ed25519 half — the additional key of R-02)
    public async Task Ac05_an_rfc9421_signature_with_ed25519_is_accepted()
    {
        var seed = new byte[32];
        RandomNumberGenerator.Fill(seed);
        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        var date = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var parameters =
            $"(\"@method\" \"@authority\" \"@path\");"
            + $"created={DateTimeOffset.UtcNow.ToUnixTimeSeconds()};"
            + $"keyid=\"{KeyId}\";alg=\"ed25519\"";
        var signingString =
            "\"@method\": POST\n"
            + $"\"@authority\": {Host}\n"
            + $"\"@path\": {Target}\n"
            + $"\"@signature-params\": {parameters}";

        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        var message = Encoding.UTF8.GetBytes(signingString);
        signer.BlockUpdate(message, 0, message.Length);
        var signature = signer.GenerateSignature();

        var verdict = await HttpSignatureVerifier.VerifyAsync(
            new IncomingRequest("POST", Target, Scheme, Host, Headers(
                ("signature-input", $"sig1={parameters}"),
                ("signature", $"sig1=:{Convert.ToBase64String(signature)}:"),
                ("date", date)),
                Body),
            Resolver(KeyId, new FederatedKey(null, privateKey.GeneratePublicKey().GetEncoded())));

        Assert.True(verdict.Accepted, verdict.Rejection.ToString());
    }

    [Fact] // AC-05 (rejection 1: Date out of the window)
    public async Task Ac05_a_date_outside_the_window_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var stale = (DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10)).ToString("R", CultureInfo.InvariantCulture);
        var digest = "SHA-256=" + Convert.ToBase64String(SHA256.HashData(Body));

        // The signature itself is valid — only the clock disqualifies the message.
        var signingString =
            $"(request-target): post {Target}\n"
            + $"host: {Host}\n"
            + $"date: {stale}\n"
            + $"digest: {digest}";
        var signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(signingString), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var header =
            $"keyId=\"{KeyId}\",algorithm=\"rsa-sha256\","
            + $"headers=\"(request-target) host date digest\",signature=\"{signature}\"";

        var verdict = await HttpSignatureVerifier.VerifyAsync(
            new IncomingRequest("POST", Target, Scheme, Host, Headers(
                ("date", stale),
                ("digest", digest),
                ("signature", header)),
                Body),
            Resolver(KeyId, new FederatedKey(rsa.ExportSubjectPublicKeyInfo(), null)));

        Assert.Equal(SignatureRejection.StaleDate, verdict.Rejection);
    }

    [Fact] // AC-05 (rejection 2: the digest no longer describes the body)
    public async Task Ac05_a_digest_that_no_longer_matches_the_body_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var date = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var digest = "SHA-256=" + Convert.ToBase64String(SHA256.HashData(Body));
        var signingString =
            $"(request-target): post {Target}\n"
            + $"host: {Host}\n"
            + $"date: {date}\n"
            + $"digest: {digest}";
        var signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(signingString), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var header =
            $"keyId=\"{KeyId}\",algorithm=\"rsa-sha256\","
            + $"headers=\"(request-target) host date digest\",signature=\"{signature}\"";

        // Headers untouched (so the signature still verifies); only the body changed in flight.
        var verdict = await HttpSignatureVerifier.VerifyAsync(
            new IncomingRequest("POST", Target, Scheme, Host, Headers(
                ("date", date),
                ("digest", digest),
                ("signature", header)),
                Encoding.UTF8.GetBytes("corpo trocado no caminho")),
            Resolver(KeyId, new FederatedKey(rsa.ExportSubjectPublicKeyInfo(), null)));

        Assert.Equal(SignatureRejection.DigestMismatch, verdict.Rejection);
    }

    [Fact] // AC-05 (rejection 3: signed by a key that is not the one published at the keyId)
    public async Task Ac05_a_signature_of_a_key_that_is_not_the_published_one_is_rejected()
    {
        using var sender = RSA.Create(2048);   // the key the impostor signs with
        using var published = RSA.Create(2048); // the key the keyId really publishes
        var date = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var digest = "SHA-256=" + Convert.ToBase64String(SHA256.HashData(Body));
        var signingString =
            $"(request-target): post {Target}\n"
            + $"host: {Host}\n"
            + $"date: {date}\n"
            + $"digest: {digest}";
        var signature = Convert.ToBase64String(
            sender.SignData(Encoding.UTF8.GetBytes(signingString), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var header =
            $"keyId=\"{KeyId}\",algorithm=\"rsa-sha256\","
            + $"headers=\"(request-target) host date digest\",signature=\"{signature}\"";

        var verdict = await HttpSignatureVerifier.VerifyAsync(
            new IncomingRequest("POST", Target, Scheme, Host, Headers(
                ("date", date),
                ("digest", digest),
                ("signature", header)),
                Body),
            Resolver(KeyId, new FederatedKey(published.ExportSubjectPublicKeyInfo(), null)));

        Assert.Equal(SignatureRejection.SignatureInvalid, verdict.Rejection);
    }

    private static Dictionary<string, string> Headers(params (string Key, string Value)[] entries)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            headers[key] = value;
        }

        return headers;
    }

    /// <summary>The resolver of the contract's step 1: the keyId answers only for its own id.</summary>
    private static Func<string, Task<FederatedKey?>> Resolver(string keyId, FederatedKey key) =>
        asked => Task.FromResult<FederatedKey?>(
            string.Equals(asked, keyId, StringComparison.Ordinal) ? key : null);

    /// <summary>Parses a draft-cavage Signature header with plain regex — no library code involved.</summary>
    private static Dictionary<string, string> ParseAuthParameters(string header)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(header, "([A-Za-z0-9_-]+)=\"([^\"]*)\""))
        {
            parameters[match.Groups[1].Value] = match.Groups[2].Value;
        }

        return parameters;
    }
}
