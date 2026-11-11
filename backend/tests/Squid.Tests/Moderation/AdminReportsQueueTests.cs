using System.Net;
using Squid.Tests.Containers;

namespace Squid.Tests.Moderation;

/// <summary>
/// TASK-0031 / SPEC-0009 section I (AC-02): the operator's queue behind the P-03 gate — no
/// session 401, a session outside <c>Instance:Operators</c> 403 <c>policy_denied</c>, the
/// operator 200 with the two filters contract §7 names working on their own spellings and the
/// paging never repeating a row.
/// </summary>
[Collection("ServiceHost")]
public sealed class AdminReportsQueueTests : IClassFixture<PolicyAuditHostFixture>
{
    private readonly PolicyAuditHostFixture _host;

    public AdminReportsQueueTests(PolicyAuditHostFixture host) => _host = host;

    [Fact] // AC-02
    public async Task The_queue_is_operator_only_and_its_filters_and_paging_answer()
    {
        // No session at all: 401, before any filter is looked at.
        var anonymous = _host.NewClient();
        var withoutSession = await anonymous.SendAsync(HttpMethod.Get, "/api/admin/reports");
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Contains("not_authenticated", await withoutSession.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // A session of a handle outside Instance:Operators: 403 policy_denied (SPEC-0008 D).
        var (stranger, _) = await ReportRoutes.SignedInAsync(_host, "qanop");
        var notOperator = await ReportRoutes.SendAsync(stranger, HttpMethod.Get, "/api/admin/reports");
        Assert.Equal(HttpStatusCode.Forbidden, notOperator.StatusCode);
        Assert.Contains("policy_denied", await notOperator.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // Three denunciations over two posts — the raw material every filter pages through.
        var (owner, _) = await ReportRoutes.SignedInAsync(_host, "qadono");
        var (authorA, _) = await ReportRoutes.SignedInAsync(_host, "qautora");
        var (authorB, _) = await ReportRoutes.SignedInAsync(_host, "qautorb");
        var (reporterOne, _) = await ReportRoutes.SignedInAsync(_host, "qdenun1");
        var (reporterTwo, _) = await ReportRoutes.SignedInAsync(_host, "qdenun2");
        var slug = await ReportRoutes.ContainerAsync(owner, "community", "Fila de denuncias");
        await ReportRoutes.JoinAsync(authorA, slug);
        await ReportRoutes.JoinAsync(authorB, slug);
        await ReportRoutes.JoinAsync(reporterOne, slug);
        await ReportRoutes.JoinAsync(reporterTwo, slug);
        var (idA, keyA, _) = await ReportRoutes.PublishAsync(authorA, slug, "post A denunciado");
        var (idB, _, _) = await ReportRoutes.PublishAsync(authorB, slug, "post B denunciado");

        var first = await ReportRoutes.SendAsync(reporterOne, HttpMethod.Post,
            $"/api/posts/{keyA}/report", new { reason = "spam no A" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var reportA = ReportRoutes.Text(await ReportRoutes.JsonAsync(first), "id");
        Assert.Equal(HttpStatusCode.Created, (await ReportRoutes.SendAsync(reporterTwo, HttpMethod.Post,
            $"/api/posts/{keyA}/report", new { reason = "spam no A de novo" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReportRoutes.SendAsync(reporterTwo, HttpMethod.Post,
            $"/api/posts/{Uri.EscapeDataString(idB)}/report", new { reason = "off-topic no B" })).StatusCode);

        var operatorClient = await ReportRoutes.OperatorAsync(_host);

        // The operator reads the whole queue (200) — this class created exactly these three.
        var all = await ReportRoutes.QueueAsync(_host, operatorClient, "?limit=100");
        Assert.Equal(3, all.Count);

        // post: the canonical id and the opaque tail select the same two rows — and only them.
        var byId = await ReportRoutes.QueueAsync(_host, operatorClient,
            $"?post={Uri.EscapeDataString(idA)}");
        Assert.Equal(2, byId.Count);
        Assert.All(byId, item => Assert.Equal(idA, ReportRoutes.Text(item, "postId")));
        Assert.Contains(byId, item => ReportRoutes.Text(item, "id") == reportA);
        var byKey = await ReportRoutes.QueueAsync(_host, operatorClient, $"?post={keyA}");
        Assert.Equal(
            byId.Select(item => ReportRoutes.Text(item, "id")).Order(),
            byKey.Select(item => ReportRoutes.Text(item, "id")).Order());
        var byOther = await ReportRoutes.QueueAsync(_host, operatorClient,
            $"?post={Uri.EscapeDataString(idB)}");
        Assert.Equal(idB, ReportRoutes.Text(Assert.Single(byOther), "postId"));

        // status: nothing is resolved yet, so open is the queue and resolved is empty.
        Assert.Equal(3, (await ReportRoutes.QueueAsync(_host, operatorClient, "?status=open")).Count);
        Assert.Empty(await ReportRoutes.QueueAsync(_host, operatorClient, "?status=resolved"));

        // Paging: one row a page, and the second page is a different row, not a repeat.
        var firstPage = await ReportRoutes.QueueAsync(_host, operatorClient, "?limit=1");
        var secondPage = await ReportRoutes.QueueAsync(_host, operatorClient, "?limit=1&offset=1");
        Assert.NotEqual(
            ReportRoutes.Text(Assert.Single(firstPage), "id"),
            ReportRoutes.Text(Assert.Single(secondPage), "id"));

        // Invalid input answers 400 — never a silent match-all over the queue.
        foreach (var query in new[] { "?status=bogus", "?limit=0", "?offset=-1" })
        {
            var invalid = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Get,
                "/api/admin/reports" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("validation_failed", await invalid.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }
}
