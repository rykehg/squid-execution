using System.Net;
using System.Text.Json;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Domain.Containers;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>
/// AC-02 and GAP-17 of TASK-0021 on the host whose configuration carries every timer and the
/// operator list: the operator's override forces the ownership move with its justification (422
/// empty, 403 non-operator, 404 absent), and the two configured keys replace the defaults — claim
/// opens at day 2 instead of 90 and the grace window closes at day 3 instead of 30, which the
/// defaults suite alone could never tell.
/// </summary>
[Collection("ServiceHost")]
public sealed class AdminOverrideAndConfiguredTimersTests : IClassFixture<TimersHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly TimersHostFixture _host;

    public AdminOverrideAndConfiguredTimersTests(TimersHostFixture host) => _host = host;

    [Fact] // AC-02
    public async Task The_operator_override_transfers_with_a_justification_and_is_refused_without_one()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac02");
        var slug = Slug("override");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Override", slug }, owner.Csrf)).StatusCode);

        var (member, memberHandle) = await SignedInAsync("ac02m");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(member, HttpMethod.Post,
            $"/api/containers/{slug}/join", null, member.Csrf)).StatusCode);

        var operatorClient = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await operatorClient.RegisterAsync(
            SuccessionHostFixture.OperatorHandle, Password)).StatusCode);

        // The gate answers before the payload: a full body from a handle outside Instance:Operators.
        var (outsider, _) = await SignedInAsync("ac02x");
        var denied = await SendAsync(outsider, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = memberHandle, justification = "quero assumir" }, outsider.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(MembershipRole.Member, (await Store().FindMembershipAsync(slug, memberHandle))!.Role);

        // §9.12: empty justification is 422 — and validation evaluates no policy, so no row.
        var blank = await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = memberHandle, justification = "   " }, operatorClient.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        var missing = await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = memberHandle }, operatorClient.Csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.DoesNotContain(await RowsAsync(slug), entry => entry.Action == PolicyAudit.Transfer);

        // The operator gate also answers for a slug that is not there — same 404, byte for byte.
        var absent = await SendAsync(operatorClient, HttpMethod.Post,
            "/api/admin/containers/que-nao-existe/transfer",
            new { handle = memberHandle, justification = "sumiu" }, operatorClient.Csrf);
        var absentRead = await SendAsync(operatorClient, HttpMethod.Get, "/api/containers/que-nao-existe");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await absentRead.Content.ReadAsStringAsync(), await absent.Content.ReadAsStringAsync());

        // The override itself: the move lands and the row carries author and justification (§7).
        var moved = await SendAsync(operatorClient, HttpMethod.Post,
            $"/api/admin/containers/{slug}/transfer",
            new { handle = memberHandle, justification = "dono sumiu ha meses" }, operatorClient.Csrf);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var members = await Store().ListMembershipsAsync(slug);
        var newOwnerRow = members.Single(row => row.Handle == memberHandle);
        Assert.Equal(MembershipRole.Owner, newOwnerRow.Role);
        Assert.Equal(MembershipState.Active, newOwnerRow.State);
        var oldOwnerRow = members.Single(row => row.Handle == ownerHandle);
        Assert.Equal(MembershipRole.Admin, oldOwnerRow.Role);
        Assert.Equal(ActorOf(memberHandle), (await Store().FindAsync(slug))!.Container.OwnerActorId);

        var allow = (await RowsAsync(slug)).Single(entry =>
            entry.Action == PolicyAudit.Transfer && entry.Result == PolicyAudit.Allow);
        Assert.Equal(ActorOf(SuccessionHostFixture.OperatorHandle), allow.ActorId);
        Assert.Equal(1, allow.PolicyVersion);
        Assert.Contains("dono sumiu ha meses", allow.Reason, StringComparison.Ordinal);
    }

    [Fact] // GAP-17 — the two keys are read; the defaults suite alone could not tell
    public async Task The_configured_timers_replace_the_defaults_for_claim_and_grace()
    {
        var (owner, _) = await SignedInAsync("gap17");
        var slug = Slug("relogio");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = "Relogio", slug }, owner.Csrf)).StatusCode);

        var (heir, heirHandle) = await SignedInAsync("gap17h");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Put,
            $"/api/containers/{slug}/successor", new { handle = heirHandle }, owner.Csrf)).StatusCode);

        // Claim window: 2 days here — day 0 and day 1 refused, day 3 open (the default is 90).
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/claim", null, heir.Csrf)).StatusCode);
        _host.Clock.AdvanceDays(1);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/claim", null, heir.Csrf)).StatusCode);
        _host.Clock.AdvanceDays(2);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(heir, HttpMethod.Post,
            $"/api/containers/{slug}/claim", null, heir.Csrf)).StatusCode);

        // Grace window: 3 days here (the default is 30), reported by the delete itself.
        var deleted = await SendAsync(heir, HttpMethod.Delete,
            $"/api/containers/{slug}?confirm={slug}", null, heir.Csrf);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using (var json = JsonDocument.Parse(await deleted.Content.ReadAsStringAsync()))
        {
            var deletedAt = json.RootElement.GetProperty("deletedAt").GetDateTimeOffset();
            Assert.Equal(deletedAt.AddDays(3),
                json.RootElement.GetProperty("recoverableUntil").GetDateTimeOffset());
        }
    }

    /// <summary>Signs a fresh account in: the cookie jar of this test's own client.</summary>
    private async Task<(AuthClient Client, string Handle)> SignedInAsync(string prefix)
    {
        var handle = Handle(prefix);
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);
        return (client, handle);
    }

    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client,
        HttpMethod method,
        string path,
        object? body = null,
        string? csrf = null) =>
        client.SendAsync(method, path, body, request =>
        {
            if (csrf is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }
        });

    /// <summary>Every trail row of one container resource (the audit store of the slice).</summary>
    private async Task<List<PolicyAuditEntry>> RowsAsync(string slug)
    {
        var trail = new SqlitePolicyAuditStore(
            new SqliteGrainStorage($"Data Source={_host.StoragePath};Pooling=False"));
        var rows = await trail.QueryAsync(new PolicyAuditFilter(Limit: 1000));
        return rows
            .Where(entry => entry.ResourceId.EndsWith($"/c/{slug}", StringComparison.Ordinal))
            .ToList();
    }

    private SqliteContainerStore Store()
    {
        var connection = $"Data Source={_host.StoragePath};Pooling=False";
        return new(new SqliteGrainStorage(connection), connection);
    }

    private string ActorOf(string handle) =>
        $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress!.Authority}/users/{handle}";

    private static string Handle(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    private static string Slug(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}
