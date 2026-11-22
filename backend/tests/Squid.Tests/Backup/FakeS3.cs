using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// The in-process S3 of AC-02/GAP-52: a loopback Kestrel endpoint that re-derives SigV4 from the
/// incoming request with its OWN implementation — never the product's — so a request without an
/// <c>Authorization</c> header, with a wrong signature, replayed over another path or carrying a
/// body its payload hash does not cover is refused before any object is touched. It scripts
/// failures (500, hang) so AC-04 can simulate a dead off-site destination, and answers LIST in
/// small pages with <c>NextContinuationToken</c> so the client's continuation loop runs on every
/// test — a truncated listing that hid an envelope is exactly the silent divergence principle VI
/// forbids. No test ever opens a socket outside this loopback port (AC-02: sem rede externa).
/// </summary>
internal sealed class FakeS3 : IAsyncDisposable
{
    private const string Namespace = "http://s3.amazonaws.com/doc/2006-03-01/";

    private readonly WebApplication _app;
    private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    private readonly List<CapturedRequest> _requests = [];
    private readonly object _gate = new();
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _region;
    private readonly int _listPageSize;
    private int _failStatus;
    private int _failTimes;
    private TimeSpan _hang = TimeSpan.Zero;

    private FakeS3(WebApplication app, string bucket, string accessKey, string secretKey, string region, int listPageSize)
    {
        _app = app;
        Bucket = bucket;
        _accessKey = accessKey;
        _secretKey = secretKey;
        _region = region;
        _listPageSize = listPageSize;
    }

    /// <summary>The bucket every request is scoped to.</summary>
    public string Bucket { get; }

    /// <summary>The bound loopback endpoint — this is what <c>Backup:S3:Endpoint</c> names.</summary>
    public string Endpoint { get; private set; } = string.Empty;

    /// <summary>The configuration half the product reads (AC-01/AC-05): endpoint, bucket, credential.</summary>
    public BackupS3Options Options { get; private set; } = null!;

    /// <summary>Every request the fake answered, captured for the tamper tests of AC-02.</summary>
    public IReadOnlyList<CapturedRequest> Requests
    {
        get { lock (_gate) return _requests.ToList(); }
    }

    /// <summary>How many requests reached the fake — proof the destination was really used.</summary>
    public int RequestCount
    {
        get { lock (_gate) return _requests.Count; }
    }

    /// <summary>The object keys currently stored, sorted (the LIST order).</summary>
    public IReadOnlyList<string> Keys
    {
        get { lock (_gate) return _objects.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList(); }
    }

    /// <summary>The stored bytes of one object, or null when it is not there.</summary>
    public byte[]? Read(string key)
    {
        lock (_gate) return _objects.TryGetValue(key, out var bytes) ? bytes : null;
    }

    /// <summary>Answers <paramref name="status"/> to the next <paramref name="times"/> requests (after SigV4).</summary>
    public void FailNext(int status = StatusCodes.Status500InternalServerError, int times = 1)
    {
        lock (_gate)
        {
            _failStatus = status;
            _failTimes = times;
        }
    }

    /// <summary>Answers the next request only after <paramref name="delay"/> — the timeout of AC-04.</summary>
    public void HangNext(TimeSpan delay)
    {
        lock (_gate) _hang = delay;
    }

    /// <summary>Drops every scripted failure: the destination heals for the assertions that follow.</summary>
    public void Heal()
    {
        lock (_gate)
        {
            _failStatus = 0;
            _failTimes = 0;
            _hang = TimeSpan.Zero;
        }
    }

    /// <summary>Starts the fake on a dynamic loopback port — never an external address (AC-02).</summary>
    public static async Task<FakeS3> StartAsync(
        string bucket,
        string accessKey,
        string secretKey,
        string region = BackupS3Options.DefaultRegion,
        int listPageSize = 2)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var fake = new FakeS3(app, bucket, accessKey, secretKey, region, listPageSize);
        app.Run(fake.HandleAsync);
        await app.StartAsync();

        // Kestrel rewrites the wildcard into the bound port — the seam BackupHost already relies on.
        fake.Endpoint = app.Urls.Single();
        fake.Options = new BackupS3Options(
            new Uri(fake.Endpoint), region, bucket, accessKey, secretKey);
        return fake;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private async Task HandleAsync(HttpContext context)
    {
        byte[] body;
        await using (var buffer = new MemoryStream())
        {
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
            body = buffer.ToArray();
        }

        var captured = Capture(context, body);
        lock (_gate) _requests.Add(captured);

        if (!ValidateSignature(captured, out var refusal))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, refusal);
            return;
        }

        // The payload hash the signature covered must be the body that arrived (S3's rule): a
        // tampered body is refused here, before it could touch an object.
        if (!string.Equals(
                HashHex(body),
                context.Request.Headers["x-amz-content-sha256"].ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "BadDigest");
            return;
        }

        // Authenticated first, exactly like the real endpoint: the scripted outcome only ever
        // applies to a request whose signature checked out.
        TimeSpan hang;
        int failure;
        lock (_gate)
        {
            hang = _hang;
            _hang = TimeSpan.Zero;
            failure = _failTimes > 0 ? _failStatus : 0;
            if (_failTimes > 0)
            {
                _failTimes--;
            }
        }

        if (hang > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(hang, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return; // the client gave up (the timeout AC-04 simulates)
            }
        }

        if (failure != 0)
        {
            await WriteErrorAsync(context, failure, "InternalError");
            return;
        }

        var path = context.Request.Path.Value ?? "/";
        var segments = path.Trim('/').Split('/', 2);
        if (!string.Equals(segments[0], Bucket, StringComparison.Ordinal))
        {
            await WriteErrorAsync(context, StatusCodes.Status404NotFound, "NoSuchBucket");
            return;
        }

        var key = segments.Length > 1 ? segments[1] : null;
        if (key is null)
        {
            if (!HttpMethods.IsGet(context.Request.Method))
            {
                await WriteErrorAsync(context, StatusCodes.Status405MethodNotAllowed, "MethodNotAllowed");
                return;
            }

            await WriteListAsync(context);
            return;
        }

        switch (context.Request.Method)
        {
            case "PUT":
                lock (_gate) _objects[key] = body;
                context.Response.StatusCode = StatusCodes.Status200OK;
                break;
            case "GET":
                var bytes = Read(key);
                if (bytes is null)
                {
                    await WriteErrorAsync(context, StatusCodes.Status404NotFound, "NoSuchKey");
                    break;
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
                break;
            case "HEAD":
                context.Response.StatusCode = Read(key) is null
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status200OK;
                break;
            case "DELETE":
                lock (_gate) _objects.Remove(key);
                context.Response.StatusCode = StatusCodes.Status204NoContent; // idempotent
                break;
            default:
                await WriteErrorAsync(context, StatusCodes.Status405MethodNotAllowed, "MethodNotAllowed");
                break;
        }
    }

    /// <summary>V2 LIST in small sorted pages — the client must follow every continuation token.</summary>
    private async Task WriteListAsync(HttpContext context)
    {
        var token = context.Request.Query["continuation-token"].ToString();
        var start = 0;
        if (!string.IsNullOrEmpty(token)
            && (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out start)
                || start < 0))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "InvalidArgument");
            return;
        }

        List<string> page;
        bool truncated;
        string? nextToken;
        lock (_gate)
        {
            var keys = _objects.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
            start = Math.Min(start, keys.Count);
            page = keys.Skip(start).Take(_listPageSize).ToList();
            truncated = start + page.Count < keys.Count;
            nextToken = truncated
                ? (start + page.Count).ToString(CultureInfo.InvariantCulture)
                : null;
        }

        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.Append("<ListBucketResult xmlns=\"").Append(Namespace).Append("\">");
        builder.Append("<Name>").Append(Bucket).Append("</Name>");
        builder.Append("<KeyCount>").Append(page.Count).Append("</KeyCount>");
        builder.Append("<IsTruncated>").Append(truncated ? "true" : "false").Append("</IsTruncated>");
        if (nextToken is not null)
        {
            builder.Append("<NextContinuationToken>").Append(nextToken).Append("</NextContinuationToken>");
        }

        foreach (var key in page)
        {
            int size;
            lock (_gate) size = _objects[key].Length;
            builder.Append("<Contents><Key>").Append(key)
                .Append("</Key><Size>").Append(size).Append("</Size></Contents>");
        }

        builder.Append("</ListBucketResult>");
        await RespondAsync(
            context, StatusCodes.Status200OK, "application/xml",
            Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static CapturedRequest Capture(HttpContext context, byte[] body)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in context.Request.Headers)
        {
            headers[header.Key] = header.Value.ToString();
        }

        return new CapturedRequest(
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Request.QueryString.Value ?? string.Empty,
            headers,
            body);
    }

    /// <summary>
    /// The SigV4 check, re-derived here from the algorithm (never by calling the product): parse
    /// the credential scope, rebuild the canonical request from what actually arrived, run the
    /// HMAC chain under the configured secret and compare in fixed time.
    /// </summary>
    private bool ValidateSignature(CapturedRequest request, out string refusal)
    {
        refusal = "SignatureDoesNotMatch";
        if (!request.Headers.TryGetValue("authorization", out var authorization)
            || !authorization.StartsWith("AWS4-HMAC-SHA256 ", StringComparison.Ordinal))
        {
            return false;
        }

        var credential = Field(authorization, "Credential");
        var signedHeaders = Field(authorization, "SignedHeaders");
        var signature = Field(authorization, "Signature");
        var scope = credential?.Split('/');
        if (scope is not { Length: 5 }
            || !string.Equals(scope[0], _accessKey, StringComparison.Ordinal)
            || !string.Equals(scope[3], "s3", StringComparison.Ordinal)
            || !string.Equals(scope[4], "aws4_request", StringComparison.Ordinal)
            || signedHeaders is null
            || signature is null
            || !request.Headers.TryGetValue("x-amz-date", out var amzDate)
            || !amzDate.StartsWith(scope[1], StringComparison.Ordinal))
        {
            return false;
        }

        var signed = signedHeaders.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (!signed.Contains("x-amz-date", StringComparer.Ordinal)
            || !signed.Contains("x-amz-content-sha256", StringComparer.Ordinal)
            || !signed.Contains("host", StringComparer.Ordinal)
            || !request.Headers.TryGetValue("x-amz-content-sha256", out var payloadHash))
        {
            return false;
        }

        var canonicalHeaders = string.Concat(signed.Select(name =>
            $"{name}:{(request.Headers.TryGetValue(name, out var value) ? value.Trim() : string.Empty)}\n"));
        var canonical = string.Join(
            '\n',
            request.Method,
            CanonicalPath(request.Path),
            CanonicalQuery(request.Query),
            canonicalHeaders,
            string.Join(';', signed),
            payloadHash);

        var toSign = string.Join(
            '\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            string.Join('/', scope[1], scope[2], scope[3], scope[4]),
            HashHex(Encoding.UTF8.GetBytes(canonical)));

        if (!TryHex(signature, out var provided))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(
            DeriveKey(_secretKey, scope[1], scope[2]), Encoding.UTF8.GetBytes(toSign));
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private static string? Field(string authorization, string name)
    {
        // "AWS4-HMAC-SHA256 Credential=…, SignedHeaders=…, Signature=…" — space after the
        // algorithm, commas between the fields.
        var match = Regex.Match(authorization, $@"(?:^|[\s,]){name}=([^,]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// The canonical URI: each path SEGMENT AWS-encoded, the separators kept — encoding the
    /// slashes too would sign a path the client never sent (the first red of TASK-0057).
    /// </summary>
    private static string CanonicalPath(string path) =>
        string.Join('/', path.Split('/').Select(AwsEncode));

    /// <summary>The query as it arrived, decoded and re-encoded per AWS rules, sorted by key.</summary>
    private static string CanonicalQuery(string rawQuery) =>
        string.Join('&', rawQuery.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var split = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(split[0]);
                var value = split.Length > 1 ? Uri.UnescapeDataString(split[1]) : string.Empty;
                return (Key: key, Value: value);
            })
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{AwsEncode(pair.Key)}={AwsEncode(pair.Value)}"));

    /// <summary>AWS RFC 3986 encoding — an independent copy of the rule, on purpose (AC-02).</summary>
    private static string AwsEncode(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in Encoding.UTF8.GetBytes(value))
        {
            var letter = (char)character;
            if (letter is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
                or '-' or '.' or '_' or '~')
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

    private static byte[] DeriveKey(string secretKey, string dateStamp, string region)
    {
        static byte[] Hmac(byte[] key, string text) =>
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text));

        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretKey), dateStamp);
        key = Hmac(key, region);
        key = Hmac(key, "s3");
        return Hmac(key, "aws4_request");
    }

    private static string HashHex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static bool TryHex(string text, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(text);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int status, string code)
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>"
            + code + "</Code><Message>the fake S3 refused the request</Message></Error>";
        await RespondAsync(context, status, "application/xml", Encoding.UTF8.GetBytes(xml));
    }

    private static async Task RespondAsync(HttpContext context, int status, string contentType, byte[] body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    }

    /// <summary>One request the fake answered: what arrived, before any validation touched it.</summary>
    internal sealed record CapturedRequest(
        string Method,
        string Path,
        string Query,
        IReadOnlyDictionary<string, string> Headers,
        byte[] Body);
}
