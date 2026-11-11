using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Squid.ReadModels.Quota;
using Squid.Tests.Feed;
using Squid.Tests.Media;
using Squid.Worker.Quota;

namespace Squid.Tests.Quota;

/// <summary>
/// AC-02 and AC-04 of TASK-0028 (SPEC-0009 section F): the blob sweep is the source of truth —
/// invoked as a FUNCTION it corrects a deliberately desynchronized <c>UsedBytes</c> and stamps
/// <c>UpdatedAt</c>; invoked through the HOSTED SERVICE it runs the same scan without touching the
/// upload write path (same blobs, same records) — the API host does not even register it.
/// </summary>
[Collection("ServiceHost")]
public sealed class QuotaReconcileTests : IClassFixture<QuotaHostFixture>
{
    private const string StaleStamp = "2000-01-01T00:00:00.000Z";

    private readonly QuotaHostFixture _host;

    public QuotaReconcileTests(QuotaHostFixture host) => _host = host;

    [Fact] // AC-02 — a função, invocada diretamente
    public async Task Ac02_the_blob_sweep_corrects_used_bytes_and_stamps_updated_at()
    {
        var (client, _) = await QuotaHarness.SignedInAsync(_host, "q02");
        var uploaded = await QuotaHarness.UploadAsync(
            client, MediaHarness.MinimalGif(), "blob para a varredura", "image/gif", "varredura.gif");
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);

        var real = QuotaHarness.BlobBytes(_host.BlobRoot);
        Assert.True(real > 0, "a varredura precisa de blob no volume para ter o que corrigir");

        // Dessincronizado artificialmente: bytes errados E um UpdatedAt antigo (o que a varredura corrige).
        await QuotaHarness.ExecuteAsync(_host.StoragePath,
            "UPDATE quota_ledger SET used_bytes = @Fake, updated_at = @Stamp",
            new { Fake = real + 4242, Stamp = StaleStamp });
        Assert.Equal(real + 4242, await QuotaHarness.UsedAsync(_host.StoragePath));

        var reconciler = new QuotaReconciler(
            _host.App.Services.GetRequiredService<IQuotaLedger>(), _host.BlobRoot);
        var found = await reconciler.ReconcileAsync();

        Assert.Equal(real, found);
        Assert.Equal(real, await QuotaHarness.UsedAsync(_host.StoragePath));

        var stamped = await QuotaHarness.UpdatedAtAsync(_host.StoragePath);
        Assert.NotNull(stamped); // the row exists: the sweep stamped it
        Assert.NotEqual(StaleStamp, stamped);
        Assert.True(
            DateTimeOffset.Parse(stamped!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            > DateTimeOffset.Parse(StaleStamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            $"UpdatedAt deve ser o instante da varredura, não o valor antigo: {stamped}");
    }

    [Fact] // AC-04 — o serviço hospedado, invocado pelo teste
    public async Task Ac04_the_hosted_service_runs_the_sweep_without_touching_the_upload_write_path()
    {
        var (client, _) = await QuotaHarness.SignedInAsync(_host, "q04");
        var uploaded = await QuotaHarness.UploadAsync(
            client, MediaHarness.MinimalGif(), "blob da AC-04", "image/gif", "ac04.gif");
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);

        var blobsBefore = QuotaHarness.BlobFiles(_host.BlobRoot);
        var recordsBefore = await QuotaHarness.MediaRecordsAsync(_host.StoragePath);
        await QuotaHarness.ExecuteAsync(_host.StoragePath,
            "UPDATE quota_ledger SET used_bytes = used_bytes + 777");

        var service = new QuotaReconcileService(
            new QuotaReconciler(_host.App.Services.GetRequiredService<IQuotaLedger>(), _host.BlobRoot),
            TimeSpan.FromHours(24),
            NullLogger<QuotaReconcileService>.Instance);

        var found = await service.RunOnceAsync();
        var real = QuotaHarness.BlobBytes(_host.BlobRoot);

        Assert.Equal(real, found); // the hosted service ran the scan
        Assert.Equal(blobsBefore, QuotaHarness.BlobFiles(_host.BlobRoot)); // nenhum blob tocado
        Assert.Equal(recordsBefore, await QuotaHarness.MediaRecordsAsync(_host.StoragePath)); // nenhum registro de upload
        Assert.Equal(real, await QuotaHarness.UsedAsync(_host.StoragePath)); // ledger corrigido

        // Off the write path: the API host never registers the sweep — it belongs to Squid.Worker.
        Assert.DoesNotContain(
            _host.App.Services.GetServices<IHostedService>(),
            hosted => hosted is QuotaReconcileService);
    }
}
