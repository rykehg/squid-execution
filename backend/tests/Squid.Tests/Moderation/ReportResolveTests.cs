using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.Domain.Objects;
using Squid.ReadModels.Projections;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Containers;

namespace Squid.Tests.Moderation;

/// <summary>
/// TASK-0031 / SPEC-0009 section I (AC-03 and AC-04): the resolution ladder — empty
/// <c>justification</c> 422 and unknown <c>action</c> 400 write nothing, <c>keep</c> closes the
/// report audit-only, <c>remove</c> executes TASK-0030's own removal in the OPERATOR's name
/// (tombstone + <c>RemoveContent</c> + the <c>Activity: Delete</c> that leaves the projection),
/// <c>ban</c> suspends the author through <c>IActorStatusStore</c> — every one of them exactly
/// one <c>Resolve</c> trail row with author, target and justification (FR-008, GAP-27) — and a
/// suspended actor who may not publish keeps reading (§3.1, AC-04).
/// </summary>
[Collection("ServiceHost")]
public sealed class ReportResolveTests : IClassFixture<PolicyAuditHostFixture>
{
    private readonly PolicyAuditHostFixture _host;

    public ReportResolveTests(PolicyAuditHostFixture host) => _host = host;

    private IPolicyAuditStore Audit =>
        _host.App.Services.GetRequiredService<IPolicyAuditStore>();

    /// <summary>The read side of the log — the very source the pump consumes from.</summary>
    private SqliteActivityLog Log =>
        _host.App.Services.GetRequiredService<SqliteActivityLog>();

    private IActorStatusStore Statuses =>
        _host.App.Services.GetRequiredService<IActorStatusStore>();

    [Fact] // AC-03 (keep)
    public async Task Keep_closes_the_report_with_one_resolve_trail_row_and_wins_over_a_repeat()
    {
        var (owner, _) = await ReportRoutes.SignedInAsync(_host, "kp01dono");
        var (author, authorHandle) = await ReportRoutes.SignedInAsync(_host, "kp01autor");
        var (reporter, reporterHandle) = await ReportRoutes.SignedInAsync(_host, "kp01denun");
        var (stranger, _) = await ReportRoutes.SignedInAsync(_host, "kp01nop");
        var slug = await ReportRoutes.ContainerAsync(owner, "community", "Resolve keep");
        await ReportRoutes.JoinAsync(author, slug);
        await ReportRoutes.JoinAsync(reporter, slug);
        var (id, key, policyVersion) = await ReportRoutes.PublishAsync(author, slug, "post do keep");
        var reportId = await ReportAsync(reporter, key, "denuncia do keep");

        var operatorClient = await ReportRoutes.OperatorAsync(_host);
        var resolvePath = $"/api/admin/reports/{reportId}/resolve";

        // The gate: no session 401, a non-operator 403 — before the payload is even read.
        var anonymous = _host.NewClient();
        var withoutSession = await anonymous.SendAsync(HttpMethod.Post, resolvePath,
            new { action = "keep", justification = "qualquer" });
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Contains("not_authenticated", await withoutSession.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        var notOperator = await ReportRoutes.SendAsync(stranger, HttpMethod.Post, resolvePath,
            new { action = "keep", justification = "qualquer" });
        Assert.Equal(HttpStatusCode.Forbidden, notOperator.StatusCode);
        Assert.Contains("policy_denied", await notOperator.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // A report the queue does not have: 404 of §8, no trail row, nothing to resolve.
        var unknown = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post,
            "/api/admin/reports/naoexiste123/resolve", new { action = "keep", justification = "x" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // The justification is owed by every action: absent, empty and whitespace are the 422 of
        // §9.12's family, and none of them writes a row or moves the report.
        foreach (var body in new object[]
        {
            new { action = "keep" },
            new { action = "keep", justification = "" },
            new { action = "keep", justification = "   " },
        })
        {
            var refused = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post, resolvePath, body);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains("validation_failed", await refused.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        // An action outside keep|remove|ban: 400, still nothing written.
        var badAction = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post, resolvePath,
            new { action = "deletar", justification = "qualquer" });
        Assert.Equal(HttpStatusCode.BadRequest, badAction.StatusCode);
        Assert.Contains("validation_failed", await badAction.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var openQueue = await ReportRoutes.QueueAsync(_host, operatorClient,
            $"?post={Uri.EscapeDataString(id)}");
        Assert.Equal("open", ReportRoutes.Text(Assert.Single(openQueue), "status"));

        // keep closes it: the report comes back resolved with the words the operator sent.
        var resolved = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post, resolvePath,
            new { action = "keep", justification = "manter o post" });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var closed = await ReportRoutes.JsonAsync(resolved);
        Assert.Equal(reportId, ReportRoutes.Text(closed, "id"));
        Assert.Equal("resolved", ReportRoutes.Text(closed, "status"));
        Assert.Equal("keep", ReportRoutes.Text(closed, "action"));
        Assert.Equal("manter o post", ReportRoutes.Text(closed, "justification"));
        Assert.False(string.IsNullOrEmpty(closed.GetProperty("resolvedAt").GetString()));

        var closedQueue = await ReportRoutes.QueueAsync(_host, operatorClient,
            $"?post={Uri.EscapeDataString(id)}");
        Assert.Equal("resolved", ReportRoutes.Text(Assert.Single(closedQueue), "status"));

        // FR-008/GAP-27: ONE Resolve row carrying author, target and justification.
        var row = Assert.Single(await ResolveRowsAsync(reportId));
        Assert.Equal(PolicyAudit.Allow, row.Result);
        Assert.Equal(ReportRoutes.ActorOf(_host, _host.OperatorHandle), row.ActorId);
        Assert.Equal(policyVersion, row.PolicyVersion);
        using (var reason = JsonDocument.Parse(row.Reason))
        {
            Assert.Equal("keep", reason.RootElement.GetProperty("action").GetString());
            Assert.Equal("manter o post", reason.RootElement.GetProperty("justification").GetString());
            Assert.Equal(id, reason.RootElement.GetProperty("postId").GetString());
            Assert.Equal(ReportRoutes.ActorOf(_host, reporterHandle),
                reason.RootElement.GetProperty("reporter").GetString());
            Assert.Equal(ReportRoutes.ActorOf(_host, authorHandle),
                reason.RootElement.GetProperty("author").GetString());
        }

        // The first resolution wins: a repeat answers the row as it stands — no second Resolve
        // line, and the post itself untouched (nothing was removed behind the report's back).
        var repeat = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post, resolvePath,
            new { action = "remove", justification = "mudou de ideia" });
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        var unchanged = await ReportRoutes.JsonAsync(repeat);
        Assert.Equal("keep", ReportRoutes.Text(unchanged, "action"));
        Assert.Equal("manter o post", ReportRoutes.Text(unchanged, "justification"));
        Assert.Single(await ResolveRowsAsync(reportId));
        Assert.Equal(HttpStatusCode.OK,
            (await ReportRoutes.SendAsync(author, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);
    }

    [Fact] // AC-03 (remove)
    public async Task Remove_resolution_executes_the_operators_removal_with_tombstone_event_and_projection()
    {
        var (owner, _) = await ReportRoutes.SignedInAsync(_host, "rm01dono");
        var (author, _) = await ReportRoutes.SignedInAsync(_host, "rm01autor");
        var (reporter, _) = await ReportRoutes.SignedInAsync(_host, "rm01denun");
        var slug = await ReportRoutes.ContainerAsync(owner, "community", "Resolve remove");
        await ReportRoutes.JoinAsync(author, slug);
        await ReportRoutes.JoinAsync(reporter, slug);
        const string content = "post que a fila manda remover";
        var (id, key, _) = await ReportRoutes.PublishAsync(author, slug, content);
        var reportId = await ReportAsync(reporter, key, "denuncia do remove");

        // Before: the projection serves it — the removal has to take it out of the feed too.
        await ProjectPendingAsync();
        var feedBefore = await ReportRoutes.JsonAsync(await ReportRoutes.SendAsync(owner,
            HttpMethod.Get, $"/api/containers/{slug}/feed?since=0"));
        Assert.Contains(content, Contents(feedBefore));

        var operatorClient = await ReportRoutes.OperatorAsync(_host);
        const string justification = "denuncia confirmada pela fila";
        var resolved = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/reports/{reportId}/resolve", new { action = "remove", justification });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var closed = await ReportRoutes.JsonAsync(resolved);
        Assert.Equal("resolved", ReportRoutes.Text(closed, "status"));
        Assert.Equal("remove", ReportRoutes.Text(closed, "action"));

        // The very removal of section H: the post leaves every read and the row gains its tombstone.
        Assert.Equal(HttpStatusCode.NotFound,
            (await ReportRoutes.SendAsync(author, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);
        var row = await AuthDb.ReadAsync(_host.StoragePath, SqliteObjectStore.StoreName,
            GrainId.Create("post", key).ToString());
        Assert.NotNull(row);
        Assert.Contains(content, row, StringComparison.Ordinal);
        var tombstone = await AuthDb.ReadAsync(_host.StoragePath, SqliteObjectStore.TombstoneStoreName,
            GrainId.Create("posttomb", key).ToString());
        Assert.NotNull(tombstone);
        Assert.Contains(justification, tombstone, StringComparison.Ordinal);

        // ...the Activity: Delete on the global sequence, and the projection drops the post.
        var entries = await Log.ReadAsync();
        var create = Assert.Single(entries, entry => entry.ObjectId == id && entry.Type == "Create");
        var delete = Assert.Single(entries, entry => entry.ObjectId == id && entry.Type == "Delete");
        Assert.True(delete.Seq > create.Seq, $"Delete.Seq {delete.Seq} must follow Create.Seq {create.Seq}");

        await ProjectPendingAsync();
        var feedAfter = await ReportRoutes.JsonAsync(await ReportRoutes.SendAsync(owner,
            HttpMethod.Get, $"/api/containers/{slug}/feed?since=0"));
        Assert.DoesNotContain(content, Contents(feedAfter));

        // RemoveContent in the OPERATOR's name (not the author's) with the justification inside...
        var removals = await Audit.QueryAsync(
            new PolicyAuditFilter(Action: "RemoveContent", Limit: 500));
        var removal = Assert.Single(removals, entry => entry.ResourceId == id);
        Assert.Equal(PolicyAudit.Allow, removal.Result);
        Assert.Equal(ReportRoutes.ActorOf(_host, _host.OperatorHandle), removal.ActorId);
        Assert.Contains(justification, removal.Reason, StringComparison.Ordinal);

        // ...beside the ONE Resolve row of FR-008 with the same words.
        var resolution = Assert.Single(await ResolveRowsAsync(reportId));
        Assert.Equal(ReportRoutes.ActorOf(_host, _host.OperatorHandle), resolution.ActorId);
        using (var reason = JsonDocument.Parse(resolution.Reason))
        {
            Assert.Equal("remove", reason.RootElement.GetProperty("action").GetString());
            Assert.Equal(justification, reason.RootElement.GetProperty("justification").GetString());
            Assert.Equal(id, reason.RootElement.GetProperty("postId").GetString());
        }
    }

    [Fact] // AC-03 (ban) + AC-04
    public async Task Ban_suspends_the_author_who_cannot_publish_but_keeps_reading()
    {
        var (owner, _) = await ReportRoutes.SignedInAsync(_host, "bn01dono");
        var (author, authorHandle) = await ReportRoutes.SignedInAsync(_host, "bn01autor");
        var (reporter, _) = await ReportRoutes.SignedInAsync(_host, "bn01denun");
        var slug = await ReportRoutes.ContainerAsync(owner, "community", "Resolve ban");
        await ReportRoutes.JoinAsync(author, slug);
        await ReportRoutes.JoinAsync(reporter, slug);
        var (id, key, _) = await ReportRoutes.PublishAsync(author, slug, "post do ban");
        var reportId = await ReportAsync(reporter, key, "denuncia do ban");

        var operatorClient = await ReportRoutes.OperatorAsync(_host);
        const string justification = "autor reincidente";
        var resolved = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/reports/{reportId}/resolve", new { action = "ban", justification });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var closed = await ReportRoutes.JsonAsync(resolved);
        Assert.Equal("resolved", ReportRoutes.Text(closed, "status"));
        Assert.Equal("ban", ReportRoutes.Text(closed, "action"));

        // The state AC-04 asks for: the very same §3.1 Suspended the publish ladder refuses.
        Assert.Equal(ActorStatus.Suspended, await Statuses.GetAsync(authorHandle));

        // Publishing stops: 403 with its Deny row on the trail (AC-04)...
        var denied = await ReportRoutes.SendAsync(author, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "nao pode mais" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var denies = await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: authorHandle, Action: "Post", Limit: 500));
        Assert.Contains(denies, entry => entry.Result == PolicyAudit.Deny);

        // ...while reading keeps working (§3.1): the very post that was denounced still answers.
        Assert.Equal(HttpStatusCode.OK,
            (await ReportRoutes.SendAsync(author, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // And the resolution that did it is the one Resolve row FR-008 owes.
        var resolution = Assert.Single(await ResolveRowsAsync(reportId));
        Assert.Equal(ReportRoutes.ActorOf(_host, _host.OperatorHandle), resolution.ActorId);
        using (var reason = JsonDocument.Parse(resolution.Reason))
        {
            Assert.Equal("ban", reason.RootElement.GetProperty("action").GetString());
            Assert.Equal(justification, reason.RootElement.GetProperty("justification").GetString());
            Assert.Equal(ReportRoutes.ActorOf(_host, authorHandle),
                reason.RootElement.GetProperty("author").GetString());
        }

        Assert.Equal(id, ReportRoutes.Text(closed, "postId"));
    }

    /// <summary>Reports the post through the route and answers the id the queue will page by.</summary>
    private async Task<string> ReportAsync(AuthClient reporter, string key, string reason)
    {
        var created = await ReportRoutes.SendAsync(reporter, HttpMethod.Post,
            $"/api/posts/{key}/report", new { reason });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return ReportRoutes.Text(await ReportRoutes.JsonAsync(created), "id")!;
    }

    /// <summary>Every <c>Resolve</c> row of the trail for one report — the FR-008 evidence.</summary>
    private async Task<List<PolicyAuditEntry>> ResolveRowsAsync(string reportId)
    {
        var rows = await Audit.QueryAsync(new PolicyAuditFilter(Action: "Resolve", Limit: 500));
        return rows.Where(entry => entry.ResourceId == reportId).ToList();
    }

    /// <summary>The projector's tick, by hand: the test host runs no pump (R-18).</summary>
    private async Task ProjectPendingAsync()
    {
        var projector = _host.App.Services.GetRequiredService<IActivityProjector>();
        var log = _host.App.Services.GetRequiredService<SqliteActivityLog>();
        foreach (var entry in await log.ReadAsync(await projector.LastProjectedSeqAsync()))
        {
            await projector.ProjectAsync(entry);
        }
    }

    /// <summary>The content of every feed item, in the order the page answered them.</summary>
    private static List<string> Contents(JsonElement page)
    {
        var items = page.GetProperty("items");
        return items.EnumerateArray().Select(item => item.GetProperty("content").GetString()!).ToList();
    }
}
