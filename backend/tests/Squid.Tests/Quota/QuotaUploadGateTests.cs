using System.Net;
using Squid.Tests.Feed;
using Squid.Tests.Media;

namespace Squid.Tests.Quota;

/// <summary>
/// AC-01 of TASK-0028 (SPEC-0009 section F): with <c>Quota:LimitBytes</c> small in the
/// configuration, an upload that breaks the instance quota answers <c>507 quota_exceeded</c> with
/// NOTHING written — the blob volume keeps exactly the files it had and the ledger row hashes
/// identically before and after — while the next upload inside the limit adds its size to
/// <c>UsedBytes</c>.
/// </summary>
[Collection("ServiceHost")]
public sealed class QuotaUploadGateTests : IClassFixture<QuotaHostFixture>
{
    private readonly QuotaHostFixture _host;

    public QuotaUploadGateTests(QuotaHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task Ac01_upload_above_the_quota_is_507_with_nothing_written_then_a_valid_upload_sums()
    {
        var (client, _) = await QuotaHarness.SignedInAsync(_host, "q01");

        var ledgerBefore = await QuotaHarness.LedgerHashAsync(_host.StoragePath);
        var usedBefore = await QuotaHarness.UsedAsync(_host.StoragePath);
        var blobsBefore = QuotaHarness.BlobFiles(_host.BlobRoot);
        var recordsBefore = await QuotaHarness.MediaRecordsAsync(_host.StoragePath);

        // 9000 bytes > the configured 8192: the gate must refuse before a byte is persisted.
        // The MP4 is padded to a KNOWN size (video carries no EXIF, so the stored bytes are these).
        var breaker = MediaHarness.Mp4WithDuration(1000, 7000, minSize: 9000);
        Assert.Equal(9000, breaker.Length);
        Assert.True(breaker.Length > QuotaHarness.LimitBytes, "o arquivo de teste precisa passar da cota");

        var rejected = await QuotaHarness.UploadAsync(client, breaker, "acima da cota", "video/mp4", "acima.mp4");
        Assert.Equal(HttpStatusCode.InsufficientStorage, rejected.StatusCode);
        var body = await FeedHarness.JsonOfAsync(rejected);
        Assert.Equal("quota_exceeded", body.GetProperty("error").GetProperty("code").GetString());

        // Hash antes/depois: the ledger row is byte-identical, no blob appeared, no record appeared.
        Assert.Equal(ledgerBefore, await QuotaHarness.LedgerHashAsync(_host.StoragePath));
        Assert.Equal(usedBefore, await QuotaHarness.UsedAsync(_host.StoragePath));
        Assert.Equal(blobsBefore, QuotaHarness.BlobFiles(_host.BlobRoot));
        Assert.Equal(recordsBefore, await QuotaHarness.MediaRecordsAsync(_host.StoragePath));

        // Inside the limit: the very same ledger now sums the size of what really landed.
        var gif = MediaHarness.MinimalGif();
        var accepted = await QuotaHarness.UploadAsync(client, gif, "dentro da cota", "image/gif", "dentro.gif");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var size = (await FeedHarness.JsonOfAsync(accepted)).GetProperty("sizeBytes").GetInt64();
        Assert.Equal(gif.Length, size); // a GIF carries no metadata: stored exactly as sent
        Assert.Equal(usedBefore + size, await QuotaHarness.UsedAsync(_host.StoragePath));
        Assert.Equal(blobsBefore.Length + 1, QuotaHarness.BlobFiles(_host.BlobRoot).Length);
    }
}
