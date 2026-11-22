using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Squid.Worker.Backup;

/// <summary>
/// <see cref="IBackupTarget"/> over an S3-compatible endpoint (SPEC-0012 section B, contract
/// backup-recovery section 1): the client speaks <c>PUT</c>/<c>GET</c>/<c>LIST</c>/<c>DELETE</c>
/// (plus <c>HEAD</c> for <see cref="ExistsAsync"/>) BY HAND with SigV4 — zero new packages,
/// GAP-52/AC-01 — against the endpoint of <c>Backup:S3:*</c> (AC-05: the credential comes from
/// configuration and nowhere else). It mirrors <see cref="LocalDiskTarget"/> exactly: one
/// envelope is <c>&lt;id&gt;.bin</c> (the ciphertext) plus <c>&lt;id&gt;.json</c> (the header,
/// written LAST as the commit marker, S-48), the S-47 seam writes the decided state back through
/// <see cref="WriteEnvelopeStateAsync"/>, and a header <see cref="ListAsync"/> cannot read is
/// skipped instead of breaking the only discovery of what was sent. The destination sees
/// ciphertext and the wrapped-DK header, never a key in clear (contract section 7); object keys
/// are plain names of unreserved characters, so the canonical request is byte-identical to the
/// path on the wire.
/// </summary>
public sealed class S3CompatibleTarget : IBackupTarget, IEnvelopeStateWriter, IDisposable
{
    private readonly BackupS3Options _options;
    private readonly HttpClient _http;

    /// <summary>
    /// Binds the target to <paramref name="options"/>; <paramref name="handler"/> and
    /// <paramref name="timeout"/> exist for the tests (loopback fake, short timeout), production
    /// wiring passes neither.
    /// </summary>
    public S3CompatibleTarget(BackupS3Options options, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = options.Endpoint;
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc />
    public string TargetId => "s3-offsite";

    /// <inheritdoc />
    public async Task PutAsync(BackupEnvelope envelope, Stream ciphertext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(ciphertext);

        // The payload hash is part of the signature, so the bytes are in hand before the send —
        // the pipeline hands an in-memory blob anyway.
        using var buffer = new MemoryStream();
        await ciphertext.CopyToAsync(buffer, ct);

        await SendAsync(HttpMethod.Put, KeyOf(envelope.Id, ".bin"), null, buffer.ToArray(), ct);
        await WriteEnvelopeStateAsync(envelope, ct); // header LAST: the commit marker (S-48)
    }

    /// <summary>Re-PUTs the envelope header — the S-47 state seam the pipeline re-reads decide on.</summary>
    public async Task WriteEnvelopeStateAsync(BackupEnvelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var header = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(envelope, BackupEnvelope.JsonOptions));
        await SendAsync(HttpMethod.Put, KeyOf(envelope.Id, ".json"), null, header, ct);
    }

    /// <inheritdoc />
    public async Task<Stream> GetAsync(string envelopeId, CancellationToken ct)
    {
        var blob = await SendAsync(HttpMethod.Get, KeyOf(envelopeId, ".bin"), null, null, ct);
        return new MemoryStream(blob, writable: false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupEnvelope>> ListAsync(long fromSeq, CancellationToken ct)
    {
        var found = new List<BackupEnvelope>();
        string? token = null;
        do
        {
            var query = new List<KeyValuePair<string, string>> { new("list-type", "2") };
            if (token is not null)
            {
                query.Add(new("continuation-token", token));
            }

            var page = await ListPageAsync(query, ct);
            foreach (var key in page.Keys)
            {
                // The ciphertext half is discovered through its header, never alone; a foreign
                // key from the listing is never turned into a path on the wire.
                if (!key.EndsWith(".json", StringComparison.Ordinal) || !IsPlainName(key))
                {
                    continue;
                }

                // S-48: a header that cannot be read (torn, removed under us) is skipped, never a
                // throw that breaks ListAsync — contract section 1 makes this the ONLY discovery.
                BackupEnvelope? envelope;
                try
                {
                    var header = await SendAsync(HttpMethod.Get, key, null, null, ct);
                    envelope = JsonSerializer.Deserialize<BackupEnvelope>(header, BackupEnvelope.JsonOptions);
                }
                catch (Exception failure) when (failure is JsonException
                    or HttpRequestException
                    or IOException
                    or InvalidOperationException)
                {
                    continue;
                }

                if (envelope is null)
                {
                    continue;
                }

                if (envelope.SeqTo >= fromSeq)
                {
                    found.Add(envelope);
                }
            }

            token = page.Truncated ? page.NextToken : null;
            // Never loop forever on an endpoint that truncates without a token (principle VI).
            if (page.Truncated && string.IsNullOrEmpty(token))
            {
                break;
            }
        }
        while (token is not null);

        // Restore takes envelopes in order of seqTo (contract section 4): hand them over sorted.
        return found
            .OrderBy(envelope => envelope.SeqTo)
            .ThenBy(envelope => envelope.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string envelopeId, CancellationToken ct)
    {
        // Both halves: an envelope whose ciphertext went missing is NOT a stored backup.
        return await HeadAsync(KeyOf(envelopeId, ".json"), ct)
            && await HeadAsync(KeyOf(envelopeId, ".bin"), ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string envelopeId, CancellationToken ct)
    {
        await DeleteObjectAsync(KeyOf(envelopeId, ".bin"), ct);
        await DeleteObjectAsync(KeyOf(envelopeId, ".json"), ct);
    }

    /// <summary>Releases the HTTP transport; the composition root keeps one for the process.</summary>
    public void Dispose() => _http.Dispose();

    /// <summary>One LIST page — <c>IsTruncated</c>/<c>NextContinuationToken</c> drive the loop above.</summary>
    private async Task<ListPage> ListPageAsync(
        IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken ct)
    {
        var bytes = await SendAsync(HttpMethod.Get, null, query, null, ct);
        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        static string? Value(XDocument source, string name) =>
            source.Descendants().FirstOrDefault(element => element.Name.LocalName == name)?.Value;

        return new ListPage(
            document.Descendants()
                .Where(element => element.Name.LocalName == "Key")
                .Select(element => element.Value)
                .ToList(),
            string.Equals(Value(document, "IsTruncated"), "true", StringComparison.OrdinalIgnoreCase),
            Value(document, "NextContinuationToken"));
    }

    /// <summary>HEAD: 200 exists, 404 does not, anything else is the destination failing.</summary>
    private async Task<bool> HeadAsync(string key, CancellationToken ct)
    {
        using var response = await SendRawAsync(HttpMethod.Head, key, null, null, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// DELETE is idempotent (rotation/purge may repeat it, contract section 7): a destination
    /// that already lost the object answers "nothing to do", not a failure.
    /// </summary>
    private async Task DeleteObjectAsync(string key, CancellationToken ct)
    {
        using var response = await SendRawAsync(HttpMethod.Delete, key, null, null, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Sends one signed request and returns the body of a successful response.</summary>
    private async Task<byte[]> SendAsync(
        HttpMethod method, string? key, IReadOnlyList<KeyValuePair<string, string>>? query, byte[]? body,
        CancellationToken ct)
    {
        using var response = await SendRawAsync(method, key, query, body, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// The wire: path-style <c>/bucket/key</c>, <c>x-amz-content-sha256</c> over the exact body,
    /// <c>x-amz-date</c> and the SigV4 <c>Authorization</c> header over all three.
    /// </summary>
    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method, string? key, IReadOnlyList<KeyValuePair<string, string>>? query, byte[]? body,
        CancellationToken ct)
    {
        var payloadHash = body is null ? S3SigV4.EmptyPayloadHash : S3SigV4.HashHex(body);
        var amzDate = DateTimeOffset.UtcNow.ToString(
            "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var path = "/" + _options.Bucket + (key is null ? string.Empty : "/" + key);
        var queryString = S3SigV4.CanonicalQuery(query ?? []);

        var request = new HttpRequestMessage(
            method, path + (queryString.Length == 0 ? string.Empty : "?" + queryString));
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            S3SigV4.Sign(
                method.Method, path, queryString, _options.Endpoint.Authority, payloadHash, amzDate,
                _options.AccessKey, _options.SecretKey, _options.Region));
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
        }

        return await _http.SendAsync(request, ct);
    }

    /// <summary>
    /// The object key of one envelope: a plain bucket name, never a path — the canonical request
    /// stays byte-identical to the wire path because nothing here needs escaping.
    /// </summary>
    private static string KeyOf(string envelopeId, string extension)
    {
        if (!IsPlainName(envelopeId))
        {
            throw new ArgumentException(
                "an envelope id must be a plain object name inside the bucket", nameof(envelopeId));
        }

        return envelopeId + extension;
    }

    /// <summary>RFC 3986 unreserved characters only — the ids the pipeline generates always are.</summary>
    private static bool IsPlainName(string envelopeId) =>
        !string.IsNullOrWhiteSpace(envelopeId) && envelopeId.All(IsUnreserved);

    private static bool IsUnreserved(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '-' or '.' or '_' or '~';

    /// <summary>What one LIST page answered: keys, whether it truncated and the continuation token.</summary>
    private sealed record ListPage(IReadOnlyList<string> Keys, bool Truncated, string? NextToken);
}
