using Squid.Api.Containers;
using Squid.Persistence;
using Squid.Service;

namespace Squid.Tests.Containers;

/// <summary>
/// The trail's own rules (SPEC-0008 section C, data-model §2.11): it only ever grows, the query
/// returns newest first with insertion order as the stable tiebreak so paging cannot skip nor
/// repeat, and the contract's three filters narrow it the way §7 says — proved against the
/// SQLite store with no HTTP in the way.
/// </summary>
public sealed class PolicyAuditStoreTests : IDisposable
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-08T12:00:00Z");

    private readonly string _database =
        Path.Combine(Path.GetTempPath(), $"squid-audit-store-{Guid.NewGuid():N}.db");

    private readonly SqlitePolicyAuditStore _store;

    public PolicyAuditStoreTests() =>
        _store = new SqlitePolicyAuditStore(new SqliteGrainStorage($"Data Source={_database};Pooling=False"));

    public void Dispose()
    {
        if (File.Exists(_database)) File.Delete(_database);
    }

    [Fact]
    public async Task Append_only_moves_forward_and_the_query_returns_newest_first_with_a_stable_tiebreak()
    {
        // Two decisions in the very same instant: the line they were written in is the tiebreak.
        await _store.AppendAsync(Entry(At, "alice", PolicyAudit.View, "um", PolicyAudit.Allow, 1, "first"));
        await _store.AppendAsync(Entry(At, "bob", PolicyAudit.View, "dois", PolicyAudit.NotFound, 1, "second"));
        await _store.AppendAsync(Entry(At.AddSeconds(1), "alice", PolicyAudit.ChangePolicy, "um", PolicyAudit.Allow, 2, "third"));

        var all = await _store.QueryAsync(new PolicyAuditFilter());
        Assert.Equal(new[] { "third", "second", "first" }, all.Select(entry => entry.Reason));

        // The same order, paged: no row is lost between pages and none shows up twice.
        var firstPage = await _store.QueryAsync(new PolicyAuditFilter(Limit: 2));
        var secondPage = await _store.QueryAsync(new PolicyAuditFilter(Limit: 2, Offset: 2));
        Assert.Equal(new[] { "third", "second" }, firstPage.Select(entry => entry.Reason));
        Assert.Equal(new[] { "first" }, secondPage.Select(entry => entry.Reason));

        // Every field of data-model §2.11 survived the round trip through the store.
        var row = all[0];
        Assert.Equal(At.AddSeconds(1), row.At);
        Assert.Equal("https://squid.test/users/alice", row.ActorId);
        Assert.Equal(PolicyAudit.ChangePolicy, row.Action);
        Assert.Equal("https://squid.test/c/um", row.ResourceId);
        Assert.Equal(PolicyAudit.Allow, row.Result);
        Assert.Equal(2, row.PolicyVersion);
    }

    [Fact]
    public async Task The_three_filters_of_the_contract_narrow_the_trail_inclusively()
    {
        await _store.AppendAsync(Entry(At, "alice", PolicyAudit.View, "um", PolicyAudit.Allow, 1, "alice-view"));
        await _store.AppendAsync(Entry(At.AddMinutes(1), "bob", PolicyAudit.ChangePolicy, "dois", PolicyAudit.Deny, 3, "bob-change"));
        await _store.AppendAsync(Entry(At.AddMinutes(2), "alice", PolicyAudit.ChangePolicy, "um", PolicyAudit.Allow, 4, "alice-change"));

        // actor: the canonical id, and the handle that stands behind it.
        var byActor = await _store.QueryAsync(new PolicyAuditFilter(Actor: "https://squid.test/users/alice"));
        Assert.Equal(new[] { "alice-change", "alice-view" }, byActor.Select(entry => entry.Reason));

        var byHandle = await _store.QueryAsync(new PolicyAuditFilter(Actor: "alice"));
        Assert.Equal(byActor.Count, byHandle.Count);

        // action: exact, and the newest of that action comes first.
        var byAction = await _store.QueryAsync(new PolicyAuditFilter(Action: PolicyAudit.ChangePolicy));
        Assert.Equal(new[] { "alice-change", "bob-change" }, byAction.Select(entry => entry.Reason));

        // from: inclusive instant — the row stamped At is kept, the older one is not.
        var byFrom = await _store.QueryAsync(new PolicyAuditFilter(From: At));
        Assert.Equal(3, byFrom.Count);
        var afterFirst = await _store.QueryAsync(new PolicyAuditFilter(From: At.AddTicks(1)));
        Assert.Equal(new[] { "alice-change", "bob-change" }, afterFirst.Select(entry => entry.Reason));

        // All three together narrow to exactly one row.
        var combined = await _store.QueryAsync(new PolicyAuditFilter(
            Actor: "bob", Action: PolicyAudit.ChangePolicy, From: At));
        var only = Assert.Single(combined);
        Assert.Equal("bob-change", only.Reason);
        Assert.Equal(PolicyAudit.Deny, only.Result);
    }

    private static PolicyAuditEntry Entry(
        DateTimeOffset at, string handle, string action, string resource, string result, int version, string reason) =>
        new(at, $"https://squid.test/users/{handle}", action, $"https://squid.test/c/{resource}", result, version, reason);
}
