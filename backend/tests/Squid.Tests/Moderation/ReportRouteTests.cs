using System.Net;
using System.Text;
using Squid.Tests.Containers;

namespace Squid.Tests.Moderation;

/// <summary>
/// TASK-0031 / SPEC-0009 section I (AC-01): the denunciation route over HTTP against the host
/// <see cref="SquidServiceHost"/> builds — the first call 201 with the report, the repeat of the
/// same reporter on the same post THE same report with no duplicate row (GAP-28), no session 401,
/// an empty reason the 422 of §9.12's family, and an absent or invisible post the byte-identical
/// 404 the read route answers with (SC-005).
/// </summary>
[Collection("ServiceHost")]
public sealed class ReportRouteTests : IClassFixture<PolicyAuditHostFixture>
{
    private readonly PolicyAuditHostFixture _host;

    public ReportRouteTests(PolicyAuditHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task The_first_report_is_201_and_the_repeat_of_the_same_pair_answers_the_same_row()
    {
        var (owner, _) = await ReportRoutes.SignedInAsync(_host, "rp01dono");
        var (author, authorHandle) = await ReportRoutes.SignedInAsync(_host, "rp01autor");
        var (reporter, reporterHandle) = await ReportRoutes.SignedInAsync(_host, "rp01denun");
        var slug = await ReportRoutes.ContainerAsync(owner, "community", "Denuncia primeiro");
        await ReportRoutes.JoinAsync(author, slug);
        await ReportRoutes.JoinAsync(reporter, slug);
        var (id, key, _) = await ReportRoutes.PublishAsync(author, slug, "post que sera denunciado");

        var created = await ReportRoutes.SendAsync(reporter, HttpMethod.Post,
            $"/api/posts/{key}/report", new { reason = "conteudo ofensivo" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var report = await ReportRoutes.JsonAsync(created);
        var reportId = ReportRoutes.Text(report, "id");
        Assert.False(string.IsNullOrEmpty(reportId));
        Assert.Equal(id, ReportRoutes.Text(report, "postId"));
        Assert.Equal(ReportRoutes.ActorOf(_host, reporterHandle), ReportRoutes.Text(report, "reporter"));
        Assert.Equal(ReportRoutes.ActorOf(_host, authorHandle), ReportRoutes.Text(report, "author"));
        Assert.Equal("conteudo ofensivo", ReportRoutes.Text(report, "reason"));
        Assert.Equal("open", ReportRoutes.Text(report, "status"));
        Assert.Null(ReportRoutes.Text(report, "action"));
        Assert.Null(ReportRoutes.Text(report, "justification"));
        Assert.False(string.IsNullOrEmpty(report.GetProperty("createdAt").GetString()));

        // The repeat (GAP-28): THE report, its id and its first reason untouched, 200 not a 201
        // and never a second row — even when the second call words the reason differently.
        var again = await ReportRoutes.SendAsync(reporter, HttpMethod.Post,
            $"/api/posts/{key}/report", new { reason = "outro motivo qualquer" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var repeated = await ReportRoutes.JsonAsync(again);
        Assert.Equal(reportId, ReportRoutes.Text(repeated, "id"));
        Assert.Equal("conteudo ofensivo", ReportRoutes.Text(repeated, "reason"));

        // ...and the operator's queue holds exactly one row for the pair.
        var operatorClient = await ReportRoutes.OperatorAsync(_host);
        var query = $"?post={Uri.EscapeDataString(id)}";
        var queue = await ReportRoutes.QueueAsync(_host, operatorClient, query);
        var only = Assert.Single(queue);
        Assert.Equal(reportId, ReportRoutes.Text(only, "id"));

        // No session at all: 401, before the post or the reason are even looked at.
        var anonymous = _host.NewClient();
        var withoutSession = await anonymous.SendAsync(HttpMethod.Post,
            $"/api/posts/{key}/report", new { reason = "sem sessao" });
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Contains("not_authenticated", await withoutSession.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // The reason is required: empty and whitespace are both the 422, and neither writes a row.
        foreach (var reason in new[] { "", "   " })
        {
            var refused = await ReportRoutes.SendAsync(reporter, HttpMethod.Post,
                $"/api/posts/{key}/report", new { reason });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains("validation_failed", await refused.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        Assert.Single(await ReportRoutes.QueueAsync(_host, operatorClient, query));
    }

    [Fact] // AC-01
    public async Task An_absent_or_invisible_post_answers_the_byte_identical_404()
    {
        var (owner, _) = await ReportRoutes.SignedInAsync(_host, "rp02dono");
        var (stranger, _) = await ReportRoutes.SignedInAsync(_host, "rp02estran");
        var slug = await ReportRoutes.ContainerAsync(owner, "group", "Denuncia invisivel");
        var (_, key, _) = await ReportRoutes.PublishAsync(owner, slug, "post fechado");

        // The read route's own answer is the reference bytes (SC-005): no traceId, one envelope.
        var read = await stranger.SendAsync(HttpMethod.Get, $"/api/posts/{key}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        var visibleBytes = await read.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("traceId", Encoding.UTF8.GetString(visibleBytes), StringComparison.Ordinal);

        // A stranger cannot denounce what they may not see — the very same bytes back.
        var reported = await ReportRoutes.SendAsync(stranger, HttpMethod.Post,
            $"/api/posts/{key}/report", new { reason = "nao devo ver" });
        Assert.Equal(HttpStatusCode.NotFound, reported.StatusCode);
        Assert.Equal(visibleBytes, await reported.Content.ReadAsByteArrayAsync());

        // An id that does not exist answers those same bytes, absent and invisible agreeing.
        var absentRead = await stranger.SendAsync(HttpMethod.Get, "/api/posts/id-que-nao-existe");
        var absentBytes = await absentRead.Content.ReadAsByteArrayAsync();
        var absentReport = await ReportRoutes.SendAsync(stranger, HttpMethod.Post,
            "/api/posts/id-que-nao-existe/report", new { reason = "qualquer" });
        Assert.Equal(HttpStatusCode.NotFound, absentReport.StatusCode);
        Assert.Equal(absentBytes, await absentReport.Content.ReadAsByteArrayAsync());
        Assert.Equal(visibleBytes, absentBytes);
    }
}
