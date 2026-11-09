using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Squid.Api.Auth;
using Squid.Api.Containers;
using Squid.Domain.Containers;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>
/// AC-01..AC-05 of TASK-0018, proved over HTTP against the host <see cref="SquidServiceHost"/>
/// builds: the versioned policy write and its audit row (AC-01), the two refusals that change
/// nothing (AC-02), the three results the trail must carry (AC-03), the operator gate plus the
/// filters of <c>GET /api/admin/audit</c> (AC-04) and the two authorizations of <c>PUT /policy</c>
/// (AC-05). The host is this class's own because <c>Instance:Operators</c> is configuration: one
/// handle is born with the host and every other handle is a non-operator by construction.
/// </summary>
[Collection("ServiceHost")]
public sealed class PolicyAuditRoutesTests : IClassFixture<PolicyAuditHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly PolicyAuditHostFixture _host;

    public PolicyAuditRoutesTests(PolicyAuditHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task A_policy_put_replaces_it_and_the_trail_records_author_before_after_and_new_version()
    {
        var (owner, handle) = await SignedInAsync("ac01");
        var slug = Unique("politica");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Politica", slug }, owner.Csrf)).StatusCode);

        var put = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy", new
        {
            posting = "admins",
            visibility = "public",
            join = "open",
            interaction = new
            {
                canComment = "everyone",
                canReact = "members",
                canShare = "members",
                canAttach = "members",
            },
        }, owner.Csrf, ifMatch: "1");

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using (var putJson = JsonDocument.Parse(await put.Content.ReadAsStringAsync()))
        {
            var policy = putJson.RootElement.GetProperty("policy");
            Assert.Equal(2, policy.GetProperty("version").GetInt32()); // n + 1
            Assert.Equal("admins", policy.GetProperty("posting").GetString());
            Assert.Equal("public", policy.GetProperty("visibility").GetString());
            Assert.Equal("open", policy.GetProperty("join").GetString());
            Assert.Equal("everyone", policy.GetProperty("interaction").GetProperty("canComment").GetString());
        }

        // The GET shows the new policy — the replacement is in force, not only in the answer.
        var read = await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using (var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync()))
        {
            var policy = readJson.RootElement.GetProperty("policy");
            Assert.Equal(2, policy.GetProperty("version").GetInt32());
            Assert.Equal("admins", policy.GetProperty("posting").GetString());
            Assert.Equal("public", policy.GetProperty("visibility").GetString());
        }

        // The audit row: author, before, after and the new version (AC-01, FR-013). The trail is
        // the operator's to read — the owner is a member of the instance, not an operator.
        var rows = await AuditAsync(await OperatorAsync(), $"?action={PolicyAudit.ChangePolicy}");
        var row = rows.Single(candidate =>
            candidate.GetProperty("resourceId").GetString()!.EndsWith("/c/" + slug, StringComparison.Ordinal));

        Assert.Equal(PolicyAudit.Allow, row.GetProperty("result").GetString());
        Assert.Equal(2, row.GetProperty("policyVersion").GetInt32());
        Assert.Equal(ActorOf(handle), row.GetProperty("actorId").GetString());

        using var reason = JsonDocument.Parse(row.GetProperty("reason").GetString()!);
        var before = reason.RootElement.GetProperty("before");
        var after = reason.RootElement.GetProperty("after");
        Assert.Equal("members", before.GetProperty("posting").GetString());
        Assert.Equal(1, before.GetProperty("version").GetInt32());
        Assert.Equal("admins", after.GetProperty("posting").GetString());
        Assert.Equal(2, after.GetProperty("version").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(reason.RootElement.GetProperty("reason").GetString()));
    }

    [Fact] // AC-02
    public async Task A_stale_if_match_conflicts_with_the_current_version_and_a_missing_one_is_428()
    {
        var (owner, _) = await SignedInAsync("ac02");
        var slug = Unique("precondicao");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Precondicao", slug }, owner.Csrf)).StatusCode);

        var changed = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { posting = "admins", visibility = "public", join = "open" }, owner.Csrf, ifMatch: "1");
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var before = await BodyAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");

        // The same If-Match again, now stale: 409 with the current version in the body (AC-02).
        var stale = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { posting = "everyone", visibility = "private", join = "closed" }, owner.Csrf, ifMatch: "1");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var conflict = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
        {
            Assert.Equal("version_conflict", conflict.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(2, conflict.RootElement.GetProperty("version").GetInt32());
        }

        // Nothing changed: the state byte for byte (hash before / hash after).
        Assert.Equal(before, await BodyAsync(owner, HttpMethod.Get, $"/api/containers/{slug}"));

        // No If-Match at all is the precondition GAP-16 asks for: 428, still nothing changed.
        var missing = await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { posting = "everyone", visibility = "private", join = "closed" }, owner.Csrf);
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        Assert.Contains("precondition_required", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await BodyAsync(owner, HttpMethod.Get, $"/api/containers/{slug}"));
    }

    [Fact] // AC-03
    public async Task The_trail_carries_allow_deny_and_not_found_of_the_decisions_the_routes_execute()
    {
        var (owner, ownerHandle) = await SignedInAsync("ac03");
        var slug = Unique("trilha");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Trilha", slug }, owner.Csrf)).StatusCode);

        // Allow: a granted read — the owner reads the container they own.
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}")).StatusCode);

        // NotFound: a refused read answers the protected 404 and the trail says which one it was.
        var stranger = await SignedInAsync("ac03x");
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(stranger.Client, HttpMethod.Get, $"/api/containers/{slug}")).StatusCode);

        // Deny: an active Member, no Admin, tries to change the policy (AC-05's authorization).
        var (member, memberHandle) = await SignedInAsync("ac03m");
        await AddMembershipAsync(slug, memberHandle, MembershipRole.Member);
        var denied = await SendAsync(member, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { posting = "admins", visibility = "public", join = "open" }, member.Csrf, ifMatch: "1");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var rows = await AuditAsync(await OperatorAsync(), "?limit=100");
        var trail = rows.Where(candidate =>
            candidate.GetProperty("resourceId").GetString()!.EndsWith("/c/" + slug, StringComparison.Ordinal)).ToList();

        // The granted read of the owner — the member's own PUT reads as a granted visibility
        // row too, so the actor is part of which row this assertion means.
        var allow = trail.Single(candidate =>
            candidate.GetProperty("action").GetString() == PolicyAudit.View
            && candidate.GetProperty("result").GetString() == PolicyAudit.Allow
            && candidate.GetProperty("actorId").GetString() == ActorOf(ownerHandle));
        Assert.Equal(ActorOf(ownerHandle), allow.GetProperty("actorId").GetString());
        Assert.Equal(1, allow.GetProperty("policyVersion").GetInt32());

        var notFound = trail.Single(candidate =>
            candidate.GetProperty("action").GetString() == PolicyAudit.View
            && candidate.GetProperty("result").GetString() == PolicyAudit.NotFound);
        Assert.Equal(1, notFound.GetProperty("policyVersion").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(notFound.GetProperty("reason").GetString()));

        var deny = trail.Single(candidate =>
            candidate.GetProperty("action").GetString() == PolicyAudit.ChangePolicy
            && candidate.GetProperty("result").GetString() == PolicyAudit.Deny);
        Assert.Equal(ActorOf(memberHandle), deny.GetProperty("actorId").GetString());
        Assert.Equal(1, deny.GetProperty("policyVersion").GetInt32()); // the version it was decided against
        Assert.False(string.IsNullOrWhiteSpace(deny.GetProperty("reason").GetString()));
    }

    [Fact] // AC-04
    public async Task The_audit_route_demands_an_operator_and_its_three_filters_work()
    {
        // No session at all: 401, before any filter is looked at.
        var anonymous = _host.NewClient();
        var withoutSession = await anonymous.SendAsync(HttpMethod.Get, "/api/admin/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Contains("not_authenticated", await withoutSession.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A session of a handle outside Instance:Operators: 403 policy_denied (SPEC-0008 D).
        var (stranger, _) = await SignedInAsync("ac04x");
        var notOperator = await SendAsync(stranger, HttpMethod.Get, "/api/admin/audit");
        Assert.Equal(HttpStatusCode.Forbidden, notOperator.StatusCode);
        Assert.Contains("policy_denied", await notOperator.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var operatorClient = await OperatorAsync();
        var (owner, ownerHandle) = await SignedInAsync("ac04");
        var slug = Unique("filtros");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Filtros", slug }, owner.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { posting = "admins", visibility = "public", join = "open" }, owner.Csrf, ifMatch: "1")).StatusCode);

        // The operator reads the trail (200), newest first and never reordered.
        var all = await AuditAsync(operatorClient, "?limit=100");
        Assert.NotEmpty(all);
        var timestamps = all.Select(row => DateTimeOffset.Parse(row.GetProperty("at").GetString()!)).ToList();
        Assert.Equal(timestamps.OrderByDescending(at => at), timestamps);

        // actor: the canonical id and the handle behind it select the same rows.
        var byActor = await AuditAsync(operatorClient, $"?actor={Uri.EscapeDataString(ActorOf(ownerHandle))}");
        Assert.NotEmpty(byActor);
        Assert.All(byActor, row => Assert.Equal(ActorOf(ownerHandle), row.GetProperty("actorId").GetString()));

        var byHandle = await AuditAsync(operatorClient, $"?actor={Uri.EscapeDataString(ownerHandle)}");
        Assert.Equal(byActor.Count, byHandle.Count);

        // action: only the rows that carry it.
        var byAction = await AuditAsync(operatorClient, $"?action={PolicyAudit.ChangePolicy}");
        Assert.NotEmpty(byAction);
        Assert.All(byAction, row => Assert.Equal(PolicyAudit.ChangePolicy, row.GetProperty("action").GetString()));

        // from: an inclusive instant — everything after the past, nothing after the future.
        var past = await AuditAsync(operatorClient, $"?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}");
        Assert.NotEmpty(past);
        var future = await AuditAsync(operatorClient, $"?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}");
        Assert.Empty(future);

        // Pagination: one row a page, and the second page is a different row, not a repeat.
        var firstPage = await AuditAsync(operatorClient, "?limit=1");
        var secondPage = await AuditAsync(operatorClient, "?limit=1&offset=1");
        Assert.Single(firstPage);
        Assert.Single(secondPage);
        Assert.NotEqual(firstPage[0].GetRawText(), secondPage[0].GetRawText());
    }

    [Fact] // AC-05
    public async Task Put_policy_without_session_is_401_and_a_member_is_denied_with_the_policy_untouched()
    {
        var (owner, _) = await SignedInAsync("ac05");
        var slug = Unique("autorizacao");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Autorizacao", slug }, owner.Csrf)).StatusCode);

        var before = await BodyAsync(owner, HttpMethod.Get, $"/api/containers/{slug}");

        // No session: 401, before the policy or the version are even looked at.
        var anonymous = _host.NewClient();
        var withoutSession = await anonymous.SendAsync(HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { posting = "admins", visibility = "public", join = "open" });
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Contains("not_authenticated", await withoutSession.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A Member that is not Admin: denied by the evaluator, and the policy does not move.
        var (member, memberHandle) = await SignedInAsync("ac05m");
        await AddMembershipAsync(slug, memberHandle, MembershipRole.Member);
        var denied = await SendAsync(member, HttpMethod.Put, $"/api/containers/{slug}/policy",
            new { posting = "admins", visibility = "public", join = "open" }, member.Csrf, ifMatch: "1");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("policy_denied", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The hash of the state: byte-identical before and after the refusal.
        Assert.Equal(before, await BodyAsync(owner, HttpMethod.Get, $"/api/containers/{slug}"));
    }

    /// <summary>The rows of <c>GET /api/admin/audit</c>, detached from the response document.</summary>
    private async Task<List<JsonElement>> AuditAsync(AuthClient client, string query)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/api/admin/audit" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    /// <summary>An operator session: the handle the host was built with, logged in for this test.</summary>
    private async Task<AuthClient> OperatorAsync()
    {
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.LoginAsync(_host.OperatorHandle, Password)).StatusCode);
        return client;
    }

    /// <summary>
    /// The membership row a Member needs (TASK-0019 owns the route that will write it): written
    /// straight into the host's own SQLite file, through the store of the slice — the HTTP surface
    /// has no join yet, and AC-05 needs a role the creator never has.
    /// </summary>
    private async Task AddMembershipAsync(string slug, string handle, MembershipRole role)
    {
        var connection = $"Data Source={_host.StoragePath};Pooling=False";
        await new SqliteContainerStore(new SqliteGrainStorage(connection), connection)
            .CreateMembershipAsync(new ContainerMembership
        {
            Slug = slug,
            ActorId = ActorOf(handle),
            Handle = handle,
            Role = role,
            State = MembershipState.Active,
            JoinedAt = DateTimeOffset.UtcNow,
        });
    }

    private async Task<string> BodyAsync(AuthClient client, HttpMethod method, string path, string? csrf = null)
    {
        var response = await SendAsync(client, method, path, null, csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<(AuthClient Client, string Handle)> SignedInAsync(string prefix)
    {
        var handle = prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);
        return (client, handle);
    }

    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client,
        HttpMethod method,
        string path,
        object? body = null,
        string? csrf = null,
        string? ifMatch = null) =>
        client.SendAsync(method, path, body, request =>
        {
            if (csrf is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }

            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }
        });

    private string ActorOf(string handle) =>
        $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress!.Authority}/users/{handle}";

    private static string Unique(string prefix) =>
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
}

/// <summary>
/// The host of the audit suite: one <see cref="SquidServiceHost"/> on a dynamic port with its own
/// temp storage file and its own <c>Instance:Operators</c> list, started once for the class —
/// configuration is the only way the operator gate has an operator to admit.
/// </summary>
public sealed class PolicyAuditHostFixture : IAsyncDisposable
{
    private const string Password = "senha-forte-1234";

    /// <summary>The application under test — the same code <c>Program</c> runs.</summary>
    public WebApplication App { get; }

    /// <summary>Base client the per-test cookie jars share.</summary>
    public HttpClient Http { get; }

    /// <summary>SQLite file behind the host, so a test can write the membership rows it needs.</summary>
    public string StoragePath { get; }

    /// <summary>The one handle <c>Instance:Operators</c> names for this host.</summary>
    public string OperatorHandle { get; }

    /// <summary>Starts the host on <c>http://127.0.0.1:0</c> with the operator seeded on it.</summary>
    public PolicyAuditHostFixture()
    {
        OperatorHandle = "op" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();
        StoragePath = Path.Combine(Path.GetTempPath(), $"squid-audit-{Guid.NewGuid():N}.db");
        App = SquidServiceHost.Build([
            "--Storage:Path=" + StoragePath,
            "--Logging:LogLevel:Default=Warning",
            "--Instance:Operators=" + OperatorHandle,
        ]);
        App.Urls.Add("http://127.0.0.1:0");
        App.StartAsync().GetAwaiter().GetResult();

        Http = new HttpClient { BaseAddress = new Uri(App.Urls.Single()) };

        // The operator account exists before any test logs in on it; every other handle that
        // registers during the suite is a non-operator by construction.
        var seed = new AuthClient(Http);
        var created = seed.RegisterAsync(OperatorHandle, Password).GetAwaiter().GetResult();
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"the operator account of this host could not be seeded: {(int)created.StatusCode}");
        }
    }

    /// <summary>A fresh jar: cookies are never shared between tests.</summary>
    public AuthClient NewClient() => new(Http);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        try
        {
            File.Delete(StoragePath);
        }
        catch (IOException)
        {
            // A leftover temp file is not a product failure; the OS cleans it up.
        }
    }
}
