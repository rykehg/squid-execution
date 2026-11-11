using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Squid.Api.Auth;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Squid.Tests.Projections;

namespace Squid.Tests.Quota;

/// <summary>
/// The host of the TASK-0028 suites: <see cref="SquidServiceHost"/> with its own temp storage file,
/// its own <c>Media:BlobRoot</c> volume and a small <c>Quota:LimitBytes</c>, so one padded file can
/// break the instance quota of SPEC-0009 section F. Hosts never overlap, which is why every suite
/// belongs to the <c>ServiceHost</c> collection (localhost clustering fixes the silo ports).
/// </summary>
public sealed class QuotaHostFixture : IAsyncDisposable
{
    /// <summary>The application under test — the same code <c>Program</c> runs.</summary>
    public WebApplication App { get; }

    /// <summary>Base client the per-test cookie jars share.</summary>
    public HttpClient Http { get; }

    /// <summary>SQLite file behind the host, so a test can read the ledger rows themselves.</summary>
    public string StoragePath { get; }

    /// <summary>The configured blob volume of this host — every stored file lives under it.</summary>
    public string BlobRoot { get; }

    /// <summary>Starts the host on <c>http://127.0.0.1:0</c> with a private volume and a small quota.</summary>
    public QuotaHostFixture()
    {
        StoragePath = Path.Combine(Path.GetTempPath(), $"squid-quota-{Guid.NewGuid():N}.db");
        BlobRoot = Path.Combine(Path.GetTempPath(), $"squid-quota-blobs-{Guid.NewGuid():N}");
        App = SquidServiceHost.Build([
            "--Storage:Path=" + StoragePath,
            "--Logging:LogLevel:Default=Warning",
            "--Media:BlobRoot=" + BlobRoot,
            "--Quota:LimitBytes=" + QuotaHarness.LimitBytes,
        ]);
        App.Urls.Add("http://127.0.0.1:0");
        App.StartAsync().GetAwaiter().GetResult();

        Http = new HttpClient { BaseAddress = new Uri(App.Urls.Single()) };
    }

    /// <summary>A fresh jar: cookies are never shared between tests.</summary>
    public AuthClient NewClient() => new(Http);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        try
        {
            File.Delete(StoragePath);
        }
        catch (IOException)
        {
            // A leftover temp file is not a product failure; the OS cleans it up.
        }

        try
        {
            if (Directory.Exists(BlobRoot)) Directory.Delete(BlobRoot, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// House helpers of the TASK-0028 suites: the multipart upload (with the CSRF header the session
/// pipeline demands of every mutation) and direct reads of what ACTUALLY landed — the ledger row
/// and the blob volume, never what an object under test reports about itself.
/// </summary>
internal static class QuotaHarness
{
    /// <summary>The configured instance quota of these suites: small, and between the two files the gate test uses.</summary>
    public const long LimitBytes = 8192;

    /// <summary>Registers a fresh account and returns its cookie jar with the handle.</summary>
    public static async Task<(AuthClient Client, string Handle)> SignedInAsync(QuotaHostFixture host, string prefix)
    {
        var handle = PostRoutes.Handle(prefix);
        var client = host.NewClient();
        var registered = await client.RegisterAsync(handle, PostRoutes.Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return (client, handle);
    }

    /// <summary>POST /api/media with the multipart fields the contract §5 names: file + alt.</summary>
    public static Task<HttpResponseMessage> UploadAsync(
        AuthClient client, byte[] bytes, string alt,
        string? declaredType = null, string fileName = "arquivo.bin")
    {
        var file = new ByteArrayContent(bytes);
        if (declaredType is not null) file.Headers.ContentType = MediaTypeHeaderValue.Parse(declaredType);

        var multipart = new MultipartFormDataContent();
        multipart.Add(file, "file", fileName);
        multipart.Add(new StringContent(alt), "alt");

        return client.SendAsync(HttpMethod.Post, "/api/media", null, request =>
        {
            request.Content = multipart;
            if (client.Csrf is { Length: > 0 } csrf)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }
        });
    }

    /// <summary>The stored <c>used_bytes</c> of the instance ledger (the row the migration seeds at id = 1).</summary>
    public static Task<long> UsedAsync(string storagePath) =>
        ProjectionDb.ScalarAsync<long>(storagePath, "SELECT used_bytes FROM quota_ledger WHERE id = 1");

    /// <summary>The stored <c>limit_bytes</c> — the configured quota the row carries.</summary>
    public static Task<long> LimitAsync(string storagePath) =>
        ProjectionDb.ScalarAsync<long>(storagePath, "SELECT limit_bytes FROM quota_ledger WHERE id = 1");

    /// <summary>The stored <c>updated_at</c> — the reconciliation stamp of data-model §2.13.</summary>
    public static Task<string?> UpdatedAtAsync(string storagePath) =>
        ProjectionDb.ScalarAsync<string>(storagePath, "SELECT updated_at FROM quota_ledger WHERE id = 1");

    /// <summary>Runs one statement straight against the host's file (tests desync/delete rows themselves).</summary>
    public static async Task ExecuteAsync(string storagePath, string sql, object? param = null)
    {
        await using var connection = new SqliteConnection(ProjectionDb.ConnectionOf(storagePath));
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, param);
    }

    /// <summary>
    /// SHA-256 over the ledger row as AC-01 compares it: a refused upload must leave the row
    /// byte-identical, and a missing row is a value of its own ("<c>sem linha</c>").
    /// </summary>
    public static async Task<string> LedgerHashAsync(string storagePath)
    {
        var row = await ProjectionDb.ScalarAsync<string>(storagePath,
            "SELECT used_bytes || '|' || limit_bytes || '|' || updated_at FROM quota_ledger WHERE id = 1");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(row ?? "<sem linha>")));
    }

    /// <summary>Every blob of the volume, sorted — the file set a refused upload must not change.</summary>
    public static string[] BlobFiles(string blobRoot) =>
        Directory.Exists(blobRoot)
            ? Directory.EnumerateFiles(blobRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : [];

    /// <summary>What the volume really holds right now — the truth the reconcile sweep compares against.</summary>
    public static long BlobBytes(string blobRoot) =>
        BlobFiles(blobRoot).Sum(file => new FileInfo(file).Length);

    /// <summary>The upload records of the store — a refused upload must not add one.</summary>
    public static Task<long> MediaRecordsAsync(string storagePath) =>
        ProjectionDb.ScalarAsync<long>(storagePath,
            "SELECT COUNT(*) FROM GrainState WHERE StoreName = @StoreName",
            new { StoreName = SqliteMediaStore.StoreName });
}
