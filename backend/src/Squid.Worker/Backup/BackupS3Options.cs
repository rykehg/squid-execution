using Microsoft.Extensions.Configuration;

namespace Squid.Worker.Backup;

/// <summary>
/// The off-site destination settings SPEC-0012 section B's input table fixes — read ONLY from
/// <c>Backup:S3:*</c> (contract backup-recovery section 7: the credential comes from
/// configuration/external secret and never from grain state, a log line or a route — AC-05).
/// Any missing or malformed piece means "no off-site target": the composition root then wires
/// the local destination exactly as before, byte-identical to a host without S3.
/// </summary>
public sealed class BackupS3Options
{
    /// <summary>The SigV4 region a MinIO-style deployment ships with when none is named.</summary>
    public const string DefaultRegion = "us-east-1";

    /// <summary>Binds one destination: endpoint, bucket, region and the credential pair.</summary>
    public BackupS3Options(Uri endpoint, string region, string bucket, string accessKey, string secretKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("the S3 endpoint must be an absolute http(s) URI", nameof(endpoint));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);
        Endpoint = endpoint;
        Region = region;
        Bucket = bucket;
        AccessKey = accessKey;
        SecretKey = secretKey;
    }

    /// <summary>The base URI every request is signed against (path-style: <c>/bucket/key</c>).</summary>
    public Uri Endpoint { get; }

    /// <summary>The SigV4 scope region.</summary>
    public string Region { get; }

    /// <summary>The bucket that holds <c>&lt;id&gt;.bin</c> + <c>&lt;id&gt;.json</c> objects.</summary>
    public string Bucket { get; }

    /// <summary>The access key id — half of the credential, never persisted beyond this process.</summary>
    public string AccessKey { get; }

    /// <summary>The secret key — the other half; signs every request and appears nowhere else (AC-05).</summary>
    public string SecretKey { get; }

    /// <summary>
    /// Reads <c>Backup:S3:Endpoint/Region/Bucket/AccessKey/SecretKey</c>; blank or malformed
    /// configuration answers null (no off-site target) instead of failing the boot — the same
    /// leniency <see cref="BackupOptions"/> gives a malformed interval.
    /// shortcut: a typo'd Backup:S3:* disables off-site silently; upgrade to a boot warning when
    /// the operator panel of TASK-0060 exists to show the destination as absent.
    /// </summary>
    public static BackupS3Options? From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var endpoint = configuration["Backup:S3:Endpoint"];
        var bucket = configuration["Backup:S3:Bucket"];
        var accessKey = configuration["Backup:S3:AccessKey"];
        var secretKey = configuration["Backup:S3:SecretKey"];
        if (string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(bucket)
            || string.IsNullOrWhiteSpace(accessKey)
            || string.IsNullOrWhiteSpace(secretKey)
            || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var region = configuration["Backup:S3:Region"];
        return new BackupS3Options(
            uri,
            string.IsNullOrWhiteSpace(region) ? DefaultRegion : region.Trim(),
            bucket.Trim(),
            accessKey.Trim(),
            secretKey.Trim());
    }

    /// <summary>
    /// The destination, never the credential (contract section 7): a ToString that reaches a log
    /// line can only ever name the bucket and endpoint.
    /// </summary>
    public override string ToString() => $"s3://{Bucket} at {Endpoint} ({Region})";
}
