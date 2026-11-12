using System.Net;
using System.Text.Json;
using Squid.Tests.Auth;
using Squid.Tests.Feed;
using Squid.Tests.Media;
using Squid.Tests.Posts;
using Squid.Tests.Projections;
using Squid.Tests.Quota;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-03 of TASK-0039 (SPEC-0010 section G, GAP-35): the P-04 pipeline stays the only place that
/// charges — an accepted <c>POST /api/media</c> is what moves <c>quota_ledger</c>, a send that
/// only REFERENCES hashes moves nothing at all (same row, same bytes, same stamp — proven by the
/// row's own hash), and the <c>507 quota_exceeded</c> still comes from the upload route while the
/// message with the very same reference keeps landing for free, before and after the refusal.
/// Own fixture on purpose: this suite saturates the small instance quota of <see cref="QuotaHostFixture"/>
/// to prove where the 507 lives, and a shared ledger would make that state bleed into other tests.
/// </summary>
[Collection("ServiceHost")]
public sealed class ThreadAttachmentQuotaTests : IClassFixture<QuotaHostFixture>
{
    private readonly QuotaHostFixture _host;

    public ThreadAttachmentQuotaTests(QuotaHostFixture host) => _host = host;

    [Fact] // AC-03
    public async Task A_send_with_hashes_creates_no_ledger_charge_and_the_507_stays_in_the_upload_route()
    {
        var (sender, _) = await QuotaHarness.SignedInAsync(_host, "cotanex");

        // O ÚNICO lugar que cobra: o upload da P-04 — ele nasce a linha e soma os bytes guardados.
        var upload = await QuotaHarness.UploadAsync(
            sender, MediaHarness.MinimalGif(), "gif da mensagem", "image/gif", "anexo.gif");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var hash = uploaded.RootElement.GetProperty("contentHash").GetString()!;

        var threadId = await DirectThreadAsync(sender, "parcotanex");
        var rowsBefore = await LedgerRowsAsync();
        var ledgerBefore = await QuotaHarness.LedgerHashAsync(_host.StoragePath);
        var usedBefore = await QuotaHarness.UsedAsync(_host.StoragePath);
        Assert.True(usedBefore > 0, "o upload aceito é o que cria a cobrança (AC-03)");

        // Enviar a mensagem com o hash não cobra de novo: a linha sai byte-idêntica.
        var sent = await PostRoutes.SendAsync(sender, HttpMethod.Post, MessagesPath(threadId),
            new { content = "com anexo", contentHashes = new[] { hash } }, sender.Csrf);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal(ledgerBefore, await QuotaHarness.LedgerHashAsync(_host.StoragePath));
        Assert.Equal(rowsBefore, await LedgerRowsAsync());
        Assert.Equal(usedBefore, await QuotaHarness.UsedAsync(_host.StoragePath));

        // O 507 continua vindo de POST /api/media: 9000 bytes passam da cota de 8192 e a rota
        // recusa antes de persistir — a linha nem por isso muda.
        var breaker = MediaHarness.Mp4WithDuration(1000, 7000, minSize: 9000);
        Assert.True(breaker.Length > QuotaHarness.LimitBytes, "o arquivo de teste precisa passar da cota");
        var rejected = await QuotaHarness.UploadAsync(
            sender, breaker, "acima da cota", "video/mp4", "acima.mp4");
        Assert.Equal(HttpStatusCode.InsufficientStorage, rejected.StatusCode);
        Assert.Equal("quota_exceeded", (await FeedHarness.JsonOfAsync(rejected))
            .GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ledgerBefore, await QuotaHarness.LedgerHashAsync(_host.StoragePath));

        // Depois da recusa, o MESMO hash segue referenciável por outra mensagem — e ainda assim
        // nada é cobrado: a mensagem só aponta para o blob que já existe.
        var second = await PostRoutes.SendAsync(sender, HttpMethod.Post, MessagesPath(threadId),
            new { content = "de novo", contentHashes = new[] { hash } }, sender.Csrf);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(ledgerBefore, await QuotaHarness.LedgerHashAsync(_host.StoragePath));
        Assert.Equal(rowsBefore, await LedgerRowsAsync());
        Assert.Equal(usedBefore, await QuotaHarness.UsedAsync(_host.StoragePath));
    }

    /// <summary>The row count of <c>quota_ledger</c> — a second charge would at least be a row.</summary>
    private Task<long> LedgerRowsAsync() =>
        ProjectionDb.ScalarAsync<long>(_host.StoragePath, "SELECT COUNT(*) FROM quota_ledger");

    /// <summary>Creates the direct thread with the FR-032 acceptance recorded (the §9.6 gate of every send).</summary>
    private async Task<string> DirectThreadAsync(AuthClient owner, string participantHandle)
    {
        var created = await PostRoutes.SendAsync(owner, HttpMethod.Post, "/api/threads",
            new { kind = "direct", participantActorIds = new[] { participantHandle } }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var threadId = json.RootElement.GetProperty("id").GetString()!;

        var acked = await PostRoutes.SendAsync(owner, HttpMethod.Post,
            $"/api/threads/{Uri.EscapeDataString(threadId)}/ack-no-e2ee", null, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, acked.StatusCode);
        return threadId;
    }

    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";
}
