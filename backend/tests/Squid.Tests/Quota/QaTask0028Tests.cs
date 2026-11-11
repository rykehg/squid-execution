using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Squid.ReadModels.Quota;
using Squid.Tests.Feed;
using Squid.Tests.Media;
using Squid.Tests.Projections;
using Squid.Worker.Quota;
using Xunit.Abstractions;

namespace Squid.Tests.Quota;

/// <summary>
/// QA-0028 hard spots of TASK-0028 (SPEC-0009 section F): the three places a green suite can lie.
/// <c>Ac01a</c>/<c>Ac01b</c> — a refused upload leaves EVERYTHING byte-identical: the ledger row
/// including <c>limit_bytes</c>, the whole blob volume by digest (path + size + SHA-256 of every
/// file, not just the names) and the upload records, and with no row at all it does not born one.
/// <c>Ac01c</c> — the 507 gate counts the PROCESSED bytes (what would be written), never the
/// request: a JPEG whose EXIF padding pushes the upload past the quota still lands because the
/// stored bytes fit. <c>Ac02a</c> — the sweep corrects drift in BOTH directions and follows a
/// blob deleted behind the ledger's back (the scan is the source of truth). <c>Ac02b</c> — the
/// optimistic increment never moves <c>UpdatedAt</c>; only the sweep stamps it (data-model §2.13).
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0028Tests : IClassFixture<QuotaHostFixture>
{
    private const string StaleStamp = "2000-01-01T00:00:00.000Z";

    private readonly QuotaHostFixture _host;

    private readonly ITestOutputHelper _output;

    public QaTask0028Tests(QuotaHostFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    [Fact] // AC-01 hard spot: 507 with NOTHING written — row, volume and records byte-identical
    public async Task Ac01a_refusal_507_leaves_the_row_volume_and_records_byte_identical()
    {
        var (client, _) = await QuotaHarness.SignedInAsync(_host, "qa28a");

        // A row to hash: the first accepted upload births it (AC-03), this test needs its bytes.
        var warm = await QuotaHarness.UploadAsync(
            client, MediaHarness.MinimalGif(), "aquecimento da cota", "image/gif", "aquecimento.gif");
        Assert.Equal(HttpStatusCode.OK, warm.StatusCode);

        // A sentinel limit: a refusal must not even re-write THAT column (DEV decision verified).
        await QuotaHarness.ExecuteAsync(_host.StoragePath, "UPDATE quota_ledger SET limit_bytes = 777");
        Assert.Equal(777L, await QuotaHarness.LimitAsync(_host.StoragePath));

        var rowBefore = await QuotaHarness.LedgerHashAsync(_host.StoragePath);
        var usedBefore = await QuotaHarness.UsedAsync(_host.StoragePath);
        var volumeBefore = VolumeDigest(_host.BlobRoot);
        var recordsBefore = await QuotaHarness.MediaRecordsAsync(_host.StoragePath);

        // 9000 bytes > the configured 8192: the gate refuses before a byte is persisted.
        var breaker = MediaHarness.Mp4WithDuration(1000, 7000, minSize: 9000);
        Assert.Equal(9000, breaker.Length);
        Assert.True(breaker.Length > QuotaHarness.LimitBytes, "o arquivo de teste precisa passar da cota");

        var rejected = await QuotaHarness.UploadAsync(client, breaker, "acima da cota", "video/mp4", "acima.mp4");
        Assert.Equal(HttpStatusCode.InsufficientStorage, rejected.StatusCode);
        Assert.Equal("quota_exceeded", (await FeedHarness.JsonOfAsync(rejected))
            .GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(rowBefore, await QuotaHarness.LedgerHashAsync(_host.StoragePath));
        Assert.Equal(777L, await QuotaHarness.LimitAsync(_host.StoragePath));
        Assert.Equal(usedBefore, await QuotaHarness.UsedAsync(_host.StoragePath));
        Assert.Equal(volumeBefore, VolumeDigest(_host.BlobRoot));
        Assert.Equal(recordsBefore, await QuotaHarness.MediaRecordsAsync(_host.StoragePath));

        _output.WriteLine(
            "AC-01a observacao: status=507 code=quota_exceeded rowIgual={0} limitBytesAposRecusa={1} " +
            "volumeIgual={2} registrosIguais={3} usados={4}",
            rowBefore == await QuotaHarness.LedgerHashAsync(_host.StoragePath),
            await QuotaHarness.LimitAsync(_host.StoragePath),
            volumeBefore == VolumeDigest(_host.BlobRoot),
            recordsBefore == await QuotaHarness.MediaRecordsAsync(_host.StoragePath),
            await QuotaHarness.UsedAsync(_host.StoragePath));
    }

    [Fact] // AC-01 hard spot: with no ledger row the refusal must not born one either
    public async Task Ac01b_refusal_without_a_ledger_row_births_nothing()
    {
        var (client, _) = await QuotaHarness.SignedInAsync(_host, "qa28b");
        await QuotaHarness.ExecuteAsync(_host.StoragePath, "DELETE FROM quota_ledger");
        Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(
            _host.StoragePath, "SELECT COUNT(*) FROM quota_ledger"));

        var volumeBefore = VolumeDigest(_host.BlobRoot);
        var recordsBefore = await QuotaHarness.MediaRecordsAsync(_host.StoragePath);

        var breaker = MediaHarness.Mp4WithDuration(1000, 7000, minSize: 9000);
        var rejected = await QuotaHarness.UploadAsync(client, breaker, "recusa sem linha", "video/mp4", "sem-linha.mp4");
        Assert.Equal(HttpStatusCode.InsufficientStorage, rejected.StatusCode);

        Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(
            _host.StoragePath, "SELECT COUNT(*) FROM quota_ledger"));
        Assert.Equal(volumeBefore, VolumeDigest(_host.BlobRoot));
        Assert.Equal(recordsBefore, await QuotaHarness.MediaRecordsAsync(_host.StoragePath));

        _output.WriteLine(
            "AC-01b observacao: linhasDoLedger={0} volumeIgual={1} registrosIguais={2} status=507",
            await ProjectionDb.ScalarAsync<long>(_host.StoragePath, "SELECT COUNT(*) FROM quota_ledger"),
            volumeBefore == VolumeDigest(_host.BlobRoot),
            recordsBefore == await QuotaHarness.MediaRecordsAsync(_host.StoragePath));
    }

    [Fact] // AC-01 + DEV decision: the gate reserves the PROCESSED bytes, never the request bytes
    public async Task Ac01c_the_gate_counts_the_processed_bytes_not_the_original()
    {
        var (client, _) = await QuotaHarness.SignedInAsync(_host, "qa28c");
        if (await ProjectionDb.ScalarAsync<long>(_host.StoragePath, "SELECT COUNT(*) FROM quota_ledger") == 0)
        {
            var warm = await QuotaHarness.UploadAsync(
                client, MediaHarness.MinimalGif(), "linha para a cota", "image/gif", "linha.gif");
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        }

        // The sweep normalizes used_bytes to what the volume really holds: the gate then has room.
        await SweepAsync();
        var usedBefore = await QuotaHarness.UsedAsync(_host.StoragePath);

        // A 9000-byte request whose EXIF padding is most of it: the STORED bytes fit the 8192 quota.
        var padded = JpegWithExifPad(9000);
        Assert.Equal(9000, padded.Length);
        Assert.True(padded.Length > QuotaHarness.LimitBytes, "o original precisa passar da cota");

        var accepted = await QuotaHarness.UploadAsync(
            client, padded, "jpeg com exif grande", "image/jpeg", "exif-grande.jpg");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var stored = (await FeedHarness.JsonOfAsync(accepted)).GetProperty("sizeBytes").GetInt64();

        Assert.True(stored < QuotaHarness.LimitBytes, $"os bytes guardados cabem na cota: {stored}");
        Assert.True(stored < padded.Length, $"a remoção de EXIF encolhe o arquivo: {stored} < {padded.Length}");
        Assert.Equal(usedBefore + stored, await QuotaHarness.UsedAsync(_host.StoragePath));

        _output.WriteLine(
            "AC-01c observacao: tamanhoOriginal={0} tamanhoGuardado={1} limiteCota={2} " +
            "usadosAntes={3} usadosDepois={4} status=200 (a gate contou os bytes processados)",
            padded.Length, stored, QuotaHarness.LimitBytes,
            usedBefore, await QuotaHarness.UsedAsync(_host.StoragePath));
    }

    [Fact] // AC-02 hard spot: the scan is the source of truth in BOTH directions
    public async Task Ac02a_the_sweep_corrects_drift_both_ways_and_follows_a_deleted_blob()
    {
        var (client, _) = await QuotaHarness.SignedInAsync(_host, "qa28d");
        var uploaded = await QuotaHarness.UploadAsync(
            client, MediaHarness.MinimalGif(), "blob da varredura da qa", "image/gif", "varredura.gif");
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);

        var real = QuotaHarness.BlobBytes(_host.BlobRoot);
        Assert.True(real > 0, "a varredura precisa de blob no volume para ter o que corrigir");

        // Drift BELOW the truth: the sweep must raise used_bytes back to the real total.
        await QuotaHarness.ExecuteAsync(_host.StoragePath, "UPDATE quota_ledger SET used_bytes = 1");
        Assert.Equal(1, await QuotaHarness.UsedAsync(_host.StoragePath));

        var reconciler = new QuotaReconciler(
            _host.App.Services.GetRequiredService<IQuotaLedger>(), _host.BlobRoot);
        Assert.Equal(real, await reconciler.ReconcileAsync());
        Assert.Equal(real, await QuotaHarness.UsedAsync(_host.StoragePath));
        var corrected = await QuotaHarness.UsedAsync(_host.StoragePath);

        // A blob deleted behind the ledger's back: the NEXT sweep follows the volume down.
        var victim = QuotaHarness.BlobFiles(_host.BlobRoot).First();
        var victimSize = new FileInfo(victim).Length;
        File.Delete(victim);

        var after = await reconciler.ReconcileAsync();
        Assert.Equal(real - victimSize, after);
        Assert.Equal(after, await QuotaHarness.UsedAsync(_host.StoragePath));
        Assert.True(after < real, "a varredura é a fonte da verdade: segue o volume para baixo");

        _output.WriteLine(
            "AC-02a observacao: real={0} driftParaBaixo=1 corrigido={1} blobRemovido={2} " +
            "aposExclusao={3} usadosApos={4}",
            real, corrected, victimSize, after, await QuotaHarness.UsedAsync(_host.StoragePath));
    }

    [Fact] // AC-02 + data-model §2.13: UpdatedAt belongs to the sweep, never to the increment
    public async Task Ac02b_the_increment_never_moves_updated_at_only_the_sweep_stamps_it()
    {
        var (client, _) = await QuotaHarness.SignedInAsync(_host, "qa28e");
        if (await ProjectionDb.ScalarAsync<long>(_host.StoragePath, "SELECT COUNT(*) FROM quota_ledger") == 0)
        {
            var warm = await QuotaHarness.UploadAsync(
                client, MediaHarness.MinimalGif(), "linha para o stamp", "image/gif", "stamp.gif");
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        }

        await SweepAsync(); // normalize used_bytes so the next upload fits the quota
        await QuotaHarness.ExecuteAsync(_host.StoragePath,
            "UPDATE quota_ledger SET updated_at = @Stamp", new { Stamp = StaleStamp });
        Assert.Equal(StaleStamp, await QuotaHarness.UpdatedAtAsync(_host.StoragePath));

        var accepted = await QuotaHarness.UploadAsync(
            client, MediaHarness.MinimalGif(), "incremento otimista", "image/gif", "incremento.gif");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // DEV decision verified: the optimistic increment does NOT move the reconcile stamp.
        Assert.Equal(StaleStamp, await QuotaHarness.UpdatedAtAsync(_host.StoragePath));
        var afterIncrement = await QuotaHarness.UpdatedAtAsync(_host.StoragePath);

        var found = await SweepAsync();
        Assert.True(found >= 0, "a varredura rodou");
        var stamped = await QuotaHarness.UpdatedAtAsync(_host.StoragePath);
        Assert.NotNull(stamped); // the row exists: the sweep stamped it
        Assert.True(
            DateTimeOffset.Parse(stamped!, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind)
            > DateTimeOffset.Parse(StaleStamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
            $"UpdatedAt é o instante da varredura, não o valor antigo: {stamped}");

        _output.WriteLine(
            "AC-02b observacao: stampAntesDoIncremento={0} stampAposIncremento={1} " +
            "stampAposVarredura={2} incrementoNaoMoveu={3}",
            StaleStamp, afterIncrement, stamped, StaleStamp == afterIncrement);
    }

    /// <summary>One pass of the sweep function — AC-02's directly invocable reconcile.</summary>
    private Task<long> SweepAsync() =>
        new QuotaReconciler(_host.App.Services.GetRequiredService<IQuotaLedger>(), _host.BlobRoot)
            .ReconcileAsync();

    /// <summary>
    /// One digest over the WHOLE volume: every path, size and SHA-256 of every file. The DEV test
    /// compares the file NAMES only, which a rewrite in place would satisfy; this cannot.
    /// </summary>
    private static string VolumeDigest(string blobRoot)
    {
        var files = QuotaHarness.BlobFiles(blobRoot);
        if (files.Length == 0) return "<vazio>";

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            digest.AppendData(Encoding.UTF8.GetBytes($"{file}|{new FileInfo(file).Length}"));
            digest.AppendData(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    /// <summary>
    /// A real JPEG carrying one big APP1/EXIF segment so the REQUEST is <paramref name="fileLength"/>
    /// bytes while the stored file (EXIF stripped first, §2.6) is only the encoded image — the
    /// fixture that tells "counts the processed bytes" from "counts the request".
    /// </summary>
    private static byte[] JpegWithExifPad(int fileLength)
    {
        using var bitmap = new SKBitmap(8, 4);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 23 + 40), (byte)(y * 51 + 20), 0xC0));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = encoded.ToArray();

        // [SOI][APP1][jpeg minus SOI] totals fileLength: the segment carries the padding.
        // The 16-bit length field counts ITSELF (2) + the payload — not the marker; a wrong value
        // here makes the stripper bail out ("structure does not parse → keep the file"), which is
        // exactly how this fixture failed the first run (507 with the padding still stored).
        var segmentBytes = fileLength - jpeg.Length;
        var payloadLength = segmentBytes - 4; // marker (2) + length field (2) + payload
        Assert.True(payloadLength >= 6, "o segmento APP1 precisa caber o canário Exif");

        using var segment = new MemoryStream();
        segment.WriteByte(0xFF); // APP1 marker
        segment.WriteByte(0xE1);
        var lengthField = segmentBytes - 2;
        segment.WriteByte((byte)(lengthField >> 8));
        segment.WriteByte((byte)(lengthField & 0xFF));
        segment.Write(Encoding.ASCII.GetBytes("Exif\0\0"));
        segment.Write(new byte[payloadLength - 6]);
        var app1 = segment.ToArray();

        var padded = new byte[2 + app1.Length + jpeg.Length - 2];
        padded[0] = jpeg[0]; // SOI
        padded[1] = jpeg[1];
        app1.CopyTo(padded, 2);
        jpeg.AsSpan(2).CopyTo(padded.AsSpan(2 + app1.Length));
        return padded;
    }
}
