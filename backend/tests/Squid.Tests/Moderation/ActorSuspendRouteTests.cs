using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Containers;
using Squid.Api.Posts;
using Squid.Domain.Objects;
using Squid.Tests.Containers;

namespace Squid.Tests.Moderation;

/// <summary>
/// TASK-0031 / SPEC-0009 section I: <c>POST /api/admin/actors/{id}/suspend</c>, the direct route
/// of contract §7 the <c>ban</c> resolution was born from — the same operator gate (401/403),
/// the §9.12 justification (empty → 422), the actor addressed by handle OR canonical id (the
/// id rides the catch-all, plain or percent-encoded), the unknown actor 404, and the write being
/// <c>IActorStatusStore.SuspendAsync</c> itself: the suspended actor's publication is refused
/// and audited while reading keeps working (§3.1, AC-04), and the trail records author, target
/// and justification under <c>ChangeRole</c> (FR-008).
/// </summary>
[Collection("ServiceHost")]
public sealed class ActorSuspendRouteTests : IClassFixture<PolicyAuditHostFixture>
{
    private readonly PolicyAuditHostFixture _host;

    public ActorSuspendRouteTests(PolicyAuditHostFixture host) => _host = host;

    private IPolicyAuditStore Audit =>
        _host.App.Services.GetRequiredService<IPolicyAuditStore>();

    private IActorStatusStore Statuses =>
        _host.App.Services.GetRequiredService<IActorStatusStore>();

    [Fact]
    public async Task The_direct_suspension_is_operator_only_addressable_both_ways_and_leaves_the_actor_suspended()
    {
        var (target, handle) = await ReportRoutes.SignedInAsync(_host, "su01ator");
        var (stranger, _) = await ReportRoutes.SignedInAsync(_host, "su01nop");
        var slug = await ReportRoutes.ContainerAsync(target, "community", "Suspensao direta");
        var (id, key, _) = await ReportRoutes.PublishAsync(target, slug, "antes da suspensao");

        var actorId = ReportRoutes.ActorOf(_host, handle);
        var path = $"/api/admin/actors/{Uri.EscapeDataString(actorId)}/suspend";

        // The gate: no session 401, a non-operator 403 — before the actor or the payload is read.
        var anonymous = _host.NewClient();
        var withoutSession = await anonymous.SendAsync(HttpMethod.Post, path,
            new { justification = "qualquer" });
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Contains("not_authenticated", await withoutSession.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        var notOperator = await ReportRoutes.SendAsync(stranger, HttpMethod.Post, path,
            new { justification = "qualquer" });
        Assert.Equal(HttpStatusCode.Forbidden, notOperator.StatusCode);
        Assert.Contains("policy_denied", await notOperator.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var operatorClient = await ReportRoutes.OperatorAsync(_host);

        // An actor that does not exist: 404 of §8 — and no row is written for a ghost.
        var unknown = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post,
            "/api/admin/actors/naoexiste123/suspend", new { justification = "qualquer" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // A malformed identifier: 400 — neither a handle nor the tail of a local actor id.
        var malformed = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post,
            "/api/admin/actors/abc-def/suspend", new { justification = "qualquer" });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        // §9.12's family: the justification is owed, empty and whitespace are the 422.
        foreach (var justification in new[] { "", "   " })
        {
            var refused = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post, path,
                new { justification });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains("validation_failed", await refused.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        Assert.Equal(ActorStatus.Active, await Statuses.GetAsync(handle));

        // The suspension itself: 200 with the state, and the §3.1 row under the handle.
        const string why = "violacao reiterada das regras";
        var suspended = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post, path,
            new { justification = why });
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        var answer = await ReportRoutes.JsonAsync(suspended);
        Assert.Equal(actorId, ReportRoutes.Text(answer, "id"));
        Assert.Equal("suspended", ReportRoutes.Text(answer, "status"));
        Assert.Equal(ActorStatus.Suspended, await Statuses.GetAsync(handle));

        // The same state by handle spelling — the store's idempotency, one row either way.
        var again = await ReportRoutes.SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/actors/{handle}/suspend", new { justification = why });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(ActorStatus.Suspended, await Statuses.GetAsync(handle));

        // §3.1 in force: publication refused and audited, reading untouched.
        var denied = await ReportRoutes.SendAsync(target, HttpMethod.Post,
            $"/api/containers/{slug}/posts", new { content = "depois da suspensao" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        var denies = await Audit.QueryAsync(
            new PolicyAuditFilter(Actor: handle, Action: "Post", Limit: 500));
        Assert.Contains(denies, entry => entry.Result == PolicyAudit.Deny);
        Assert.Equal(HttpStatusCode.OK,
            (await ReportRoutes.SendAsync(target, HttpMethod.Get, $"/api/posts/{key}")).StatusCode);

        // FR-008: every administrative action on the trail with author, target and justification —
        // one row per call of the route (both spellings suspended the same actor).
        var rows = await Audit.QueryAsync(new PolicyAuditFilter(Action: "ChangeRole", Limit: 500));
        var suspensions = rows.Where(entry => entry.ResourceId == actorId).ToList();
        Assert.Equal(2, suspensions.Count);
        Assert.All(suspensions, row =>
        {
            Assert.Equal(PolicyAudit.Allow, row.Result);
            Assert.Equal(ReportRoutes.ActorOf(_host, _host.OperatorHandle), row.ActorId);
            Assert.Contains(why, row.Reason, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"suspended\"", row.Reason, StringComparison.Ordinal);
        });
    }
}
