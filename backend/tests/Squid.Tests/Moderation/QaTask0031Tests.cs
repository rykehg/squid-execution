using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.ReadModels.Projections;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Containers;

namespace Squid.Tests.Moderation;

/// <summary>
/// QA-0031 / TASK-0031 — the two hard spots the dispatch named and DEV's own suite does not
/// compare head to head: (1) <c>resolve action=remove</c> must leave the VERY same tombstone
/// effects as the author's own <c>DELETE /api/posts/{id}</c> — same 404 bytes as the ghost id,
/// same tombstone shape, same <c>RemoveContent</c>/<c>Allow</c> trail row, same <c>Activity: Delete</c>
/// on the global sequence, same projection drop (the single extracted
/// <see cref="PostEndpoints.RemoveContentAsync"/> path, never a fork); and (2) GAP-28's
/// idempotency — the repeat answers 200 with the SAME report id and the queue count does not
/// move, while a different reporter on the same post is a second row (the key is the pair).
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0031Tests : IClassFixture<PolicyAuditHostFixture>
{
    private readonly PolicyAuditHostFixture _host;

    public QaTask0031Tests(PolicyAuditHostFixture host) => _host = host;

    private IPolicyAuditStore Audit =>
        _host.App.Services.GetRequiredService<IPolicyAuditStore>();

    /// <summary>The read side of the log — the source the projector consumes from.</summary>
    private SqliteActivityLog Log =>
        _host.App.Services.GetRequiredService<SqliteActivityLog>();

    [Fact] // AC-03 (hard spot: remove == the author's DELETE, byte for byte where bytes are the claim)
    public async Task Resolve_remove_leaves_the_very_same_tombstone_effects_as_the_authors_own_delete()
    {
        var (owner, _) = await ReportRoutes.SignedInAsync(_host, "qadono");
        var (author, authorHandle) = await ReportRoutes.SignedInAsync(_host, "qaautor");
        var (reporter, _) = await ReportRoutes.SignedInAsync(_host, "qadenun");
        var slug = await ReportRoutes.ContainerAsync(owner, "community", "Paridade da remocao");
        await ReportRoutes.JoinAsync(author, slug);
        await ReportRoutes.JoinAsync(reporter, slug);

        const string byQueue = "post removido pela fila";
        const string byAuthor = "post removido pelo autor";
        const string queueJustification = "denuncia confirmada pelo operador";
        const string authorJustification = "apagado pelo proprio autor";

        var (queuedId, queuedKey, policyVersion) = await ReportRoutes.PublishAsync(author, slug, byQueue);
        var (authorId, authorKey, _) = await ReportRoutes.PublishAsync(author, slug, byAuthor);

        // Both posts are projected FIRST, so the feed serves them and the removal has to take
        // them back out — the projection half of the parity claim.
        await ProjectPendingAsync();
        var served = await ContentsAsync(slug, owner);
        Assert.Contains(byQueue, served);
        Assert.Contains(byAuthor, served);

        // Path A: the operator's resolution through the queue.
        var created = await ReportRoutes.SendAsync(reporter, HttpMethod.Post,
            $"/api/posts/{queuedKey}/report", new { reason = "conteudo ruim" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reportId = ReportRoutes.Text(await ReportRoutes.JsonAsync(created), "id")!;
        var operatorClient = await ReportRoutes.OperatorAsync(_host);
        var resolved = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/reports/{reportId}/resolve",
            new { action = "remove", justification = queueJustification });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        // Path B: the author's own DELETE of the sibling post (section H).
        var removed = await ReportRoutes.SendAsync(author, HttpMethod.Delete, $"/api/posts/{authorKey}",
            new { takedownReason = authorJustification });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        // 1. 404: both removed posts answer the byte-identical body of an id that never existed.
        var ghost = await ReportRoutes.SendAsync(owner, HttpMethod.Get, "/api/posts/id-que-nao-existe");
        Assert.Equal(HttpStatusCode.NotFound, ghost.StatusCode);
        var ghostBytes = await ghost.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("traceId", Encoding.UTF8.GetString(ghostBytes), StringComparison.Ordinal);

        foreach (var key in new[] { queuedKey, authorKey })
        {
            var gone = await ReportRoutes.SendAsync(owner, HttpMethod.Get, $"/api/posts/{key}");
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
            Assert.Equal(ghostBytes, await gone.Content.ReadAsByteArrayAsync());
        }

        // 2. The tombstone rows: same document, same property names, each carrying its own reason.
        foreach (var (key, reason) in new[]
        {
            (queuedKey, queueJustification),
            (authorKey, authorJustification),
        })
        {
            var row = await AuthDb.ReadAsync(_host.StoragePath, SqliteObjectStore.TombstoneStoreName,
                GrainId.Create("posttomb", key).ToString());
            Assert.NotNull(row);
            using var tombstone = JsonDocument.Parse(row!);
            // The stored row's own casing is the artifact (default serializer, SqliteGrainStorage.cs:94).
            Assert.Equal(reason, PropertyOf(tombstone.RootElement, "TakedownReason"));
            Assert.False(string.IsNullOrEmpty(PropertyOf(tombstone.RootElement, "DeletedAt")));
        }

        // 3. The Activity: Delete on the global sequence, strictly after each Create.
        var entries = await Log.ReadAsync();
        foreach (var (id, _) in new[] { (queuedId, queuedKey), (authorId, authorKey) })
        {
            var create = Assert.Single(entries, entry => entry.ObjectId == id && entry.Type == "Create");
            var delete = Assert.Single(entries, entry => entry.ObjectId == id && entry.Type == "Delete");
            Assert.True(delete.Seq > create.Seq, $"Delete.Seq {delete.Seq} must follow Create.Seq {create.Seq}");
        }

        // 4. The trail: one RemoveContent/Allow row per post — same action, result, policy version
        //    and tombstone-shaped reason; the ONLY intended difference is WHO took it (operator vs author).
        var removals = await Audit.QueryAsync(new PolicyAuditFilter(Action: "RemoveContent", Limit: 500));
        var queuedRow = Assert.Single(removals, entry => entry.ResourceId == queuedId);
        var authorRow = Assert.Single(removals, entry => entry.ResourceId == authorId);
        Assert.Equal(PolicyAudit.Allow, queuedRow.Result);
        Assert.Equal(PolicyAudit.Allow, authorRow.Result);
        Assert.Equal(policyVersion, queuedRow.PolicyVersion);
        Assert.Equal(policyVersion, authorRow.PolicyVersion);
        Assert.Equal(ReportRoutes.ActorOf(_host, _host.OperatorHandle), queuedRow.ActorId);
        Assert.Equal(ReportRoutes.ActorOf(_host, authorHandle), authorRow.ActorId);
        foreach (var (row, reason) in new[] { (queuedRow, queueJustification), (authorRow, authorJustification) })
        {
            Assert.Contains(reason, row.Reason, StringComparison.Ordinal);
            using var trail = JsonDocument.Parse(row.Reason);
            Assert.False(string.IsNullOrEmpty(PropertyOf(trail.RootElement, "deletedAt")));
            Assert.Equal(reason, PropertyOf(trail.RootElement, "takedownReason"));
        }

        // 5. The projection: both posts left every read after the pump — neither removal may
        //    leave its row in the feed, and the pump's second pass changes nothing.
        await ProjectPendingAsync();
        var afterContents = await ContentsAsync(slug, owner);
        Assert.DoesNotContain(byQueue, afterContents);
        Assert.DoesNotContain(byAuthor, afterContents);
        await ProjectPendingAsync();
        Assert.Equal(afterContents.Count, (await ContentsAsync(slug, owner)).Count);
    }

    [Fact] // AC-01 (hard spot: 201 then 200 with the SAME report id, queue count unchanged)
    public async Task The_repeat_of_a_denunciation_is_the_same_report_and_the_pair_key_stops_a_duplicate()
    {
        var (owner, _) = await ReportRoutes.SignedInAsync(_host, "qaidono");
        var (author, _) = await ReportRoutes.SignedInAsync(_host, "qaidautor");
        var (reporterOne, reporterOneHandle) = await ReportRoutes.SignedInAsync(_host, "qaidenun1");
        var (reporterTwo, _) = await ReportRoutes.SignedInAsync(_host, "qaidenun2");
        var slug = await ReportRoutes.ContainerAsync(owner, "community", "Idempotencia da denuncia");
        await ReportRoutes.JoinAsync(author, slug);
        await ReportRoutes.JoinAsync(reporterOne, slug);
        await ReportRoutes.JoinAsync(reporterTwo, slug);
        var (postId, key, _) = await ReportRoutes.PublishAsync(author, slug, "post denunciado duas vezes");
        var postQuery = $"?post={Uri.EscapeDataString(postId)}";

        var operatorClient = await ReportRoutes.OperatorAsync(_host);

        // First call: 201 with the new report.
        var first = await ReportRoutes.SendAsync(reporterOne, HttpMethod.Post,
            $"/api/posts/{key}/report", new { reason = "motivo original" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var report = await ReportRoutes.JsonAsync(first);
        var reportId = ReportRoutes.Text(report, "id")!;
        Assert.Equal(postId, ReportRoutes.Text(report, "postId"));
        Assert.Equal(ReportRoutes.ActorOf(_host, reporterOneHandle), ReportRoutes.Text(report, "reporter"));
        Assert.Equal("open", ReportRoutes.Text(report, "status"));
        var afterFirst = await ReportRoutes.QueueAsync(_host, operatorClient, postQuery);
        Assert.Equal(reportId, ReportRoutes.Text(Assert.Single(afterFirst), "id"));

        // The repeat: 200, THE same id, the first reason untouched — and the count does not move.
        var again = await ReportRoutes.SendAsync(reporterOne, HttpMethod.Post,
            $"/api/posts/{key}/report", new { reason = "segundo texto, mesmo par" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var repeated = await ReportRoutes.JsonAsync(again);
        Assert.Equal(reportId, ReportRoutes.Text(repeated, "id"));
        Assert.Equal("motivo original", ReportRoutes.Text(repeated, "reason"));
        Assert.Equal(postId, ReportRoutes.Text(repeated, "postId"));
        var afterRepeat = await ReportRoutes.QueueAsync(_host, operatorClient, postQuery);
        Assert.Equal(reportId, ReportRoutes.Text(Assert.Single(afterRepeat), "id"));

        // The key is the PAIR (post, reporter): another reporter on the same post is a new row.
        var other = await ReportRoutes.SendAsync(reporterTwo, HttpMethod.Post,
            $"/api/posts/{key}/report", new { reason = "outra pessoa denuncia o mesmo post" });
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        var secondId = ReportRoutes.Text(await ReportRoutes.JsonAsync(other), "id")!;
        Assert.NotEqual(reportId, secondId);
        var afterOther = await ReportRoutes.QueueAsync(_host, operatorClient, postQuery);
        Assert.Equal(2, afterOther.Count);
        Assert.Contains(afterOther, item => ReportRoutes.Text(item, "id") == reportId);
        Assert.Contains(afterOther, item => ReportRoutes.Text(item, "id") == secondId);
    }

    /// <summary>
    /// One property of a stored row, matched case-insensitively: the casing the artifact itself
    /// carries (the store's default serializer, the trail's Web defaults) is what the assertion
    /// meets — never a name remembered from another session.
    /// </summary>
    private static string? PropertyOf(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.GetString();
            }
        }

        return null;
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

    /// <summary>The content of every feed item of one container, in the order the page answered.</summary>
    private async Task<List<string>> ContentsAsync(string slug, AuthClient reader)
    {
        var page = await ReportRoutes.JsonAsync(await ReportRoutes.SendAsync(reader, HttpMethod.Get,
            $"/api/containers/{slug}/feed?since=0"));
        return page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("content").GetString()!).ToList();
    }
}
