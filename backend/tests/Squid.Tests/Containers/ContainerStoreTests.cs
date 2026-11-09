using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Squid.Domain.Containers;
using Squid.Persistence;
using Squid.Service;

namespace Squid.Tests.Containers;

/// <summary>
/// AC-05's unit half plus the store's own rules: a stale version refuses the write and leaves the
/// row byte-identical, a duplicated slug inserts nothing, and the owner membership lives in its
/// own row outside the aggregate (data-model §2.2/§2.4).
/// </summary>
public sealed class ContainerStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-08T11:00:00Z");

    private readonly string _database =
        Path.Combine(Path.GetTempPath(), $"squid-containers-{Guid.NewGuid():N}.db");

    private readonly SqliteContainerStore _store;

    public ContainerStoreTests()
    {
        var connection = $"Data Source={_database};Pooling=False";
        _store = new SqliteContainerStore(new SqliteGrainStorage(connection), connection);
    }

    public void Dispose()
    {
        if (File.Exists(_database)) File.Delete(_database);
    }

    [Fact] // AC-05 (store half)
    public async Task A_stale_version_write_is_refused_and_the_state_stays_byte_identical()
    {
        Assert.NotNull(await _store.CreateAsync(NewContainer("turma")));
        Assert.Equal(1, (await _store.FindAsync("turma"))!.Version);

        var updated = await _store.UpdateAsync("turma", 1, row => row with { Name = "Turma editada" });
        Assert.NotNull(updated);
        Assert.Equal(2, updated!.Version);
        Assert.Equal("Turma editada", updated.Container.Name);
        // The gaps table: PATCH moved the state version, never the policy version.
        Assert.Equal(1, updated.Container.Policy.Version);

        var before = Hash((await _store.FindAsync("turma"))!.Container);
        var conflict = await _store.UpdateAsync("turma", 1, row => row with { Name = "Ninguem vê isto" });
        var after = Hash((await _store.FindAsync("turma"))!.Container);

        Assert.Null(conflict);
        Assert.Equal(before, after);
        Assert.Equal("Turma editada", (await _store.FindAsync("turma"))!.Container.Name);
    }

    [Fact]
    public async Task A_duplicated_slug_refuses_the_insert_and_writes_nothing()
    {
        Assert.NotNull(await _store.CreateAsync(NewContainer("rust")));
        Assert.Null(await _store.CreateAsync(NewContainer("rust", "Outro nome")));

        var state = await _store.FindAsync("rust");
        Assert.Equal("Turma", state!.Container.Name); // the first write survived
        Assert.Equal(1, state.Version);               // the refused insert bumped nothing
    }

    [Fact]
    public async Task An_unconditional_write_converges_on_the_newest_state()
    {
        Assert.NotNull(await _store.CreateAsync(NewContainer("aberto")));

        var first = await _store.UpdateAsync("aberto", null, row => row with { Summary = "um" });
        var second = await _store.UpdateAsync("aberto", null, row => row with { Summary = "dois" });

        Assert.Equal("um", first!.Container.Summary);
        Assert.Equal(2, first.Version);
        Assert.Equal("dois", second!.Container.Summary);
        Assert.Equal(3, second.Version);
    }

    [Fact] // AC-03 (store half)
    public async Task The_owner_membership_is_written_found_and_listed_for_the_container()
    {
        await _store.CreateMembershipAsync(Owner("turma"));

        var found = await _store.FindMembershipAsync("turma", "alice");
        Assert.NotNull(found);
        Assert.Equal(MembershipRole.Owner, found!.Role);
        Assert.Equal(MembershipState.Active, found.State);

        Assert.Null(await _store.FindMembershipAsync("turma", "bob"));
        Assert.Null(await _store.FindMembershipAsync("outra", "alice"));
        Assert.Single(await _store.ListMembershipsAsync("turma"));
        Assert.Empty(await _store.ListMembershipsAsync("outra"));
    }

    [Fact] // AC-03/AC-04 (store half)
    public async Task Upsert_replaces_the_row_of_one_handle_and_appends_a_second_without_duplicating()
    {
        await _store.CreateMembershipAsync(Owner("turma"));

        var invited = Owner("turma") with
        {
            State = MembershipState.Invited,
            InvitedBy = "https://squid.test/users/carol",
        };
        var written = await _store.UpsertMembershipAsync(invited);
        Assert.Equal(MembershipState.Invited, written.State);

        // One link per (container, actor) pair: the replace took, the row count did not move.
        var replaced = Assert.Single(await _store.ListMembershipsAsync("turma"));
        Assert.Equal("alice", replaced.Handle);
        Assert.Equal(MembershipState.Invited, replaced.State);
        Assert.Equal("https://squid.test/users/carol", replaced.InvitedBy);

        await _store.UpsertMembershipAsync(new ContainerMembership
        {
            Slug = "turma",
            ActorId = "https://squid.test/users/bob",
            Handle = "bob",
            Role = MembershipRole.Member,
            State = MembershipState.Active,
            JoinedAt = Now,
        });

        Assert.Equal(2, (await _store.ListMembershipsAsync("turma")).Count);
        Assert.Equal(MembershipState.Active, (await _store.FindMembershipAsync("turma", "bob"))!.State);
        Assert.Equal(MembershipState.Invited, (await _store.FindMembershipAsync("turma", "alice"))!.State);
    }

    [Fact] // AC-01/AC-02 (store half)
    public async Task The_search_returns_only_listed_and_living_rows_matching_name_or_slug_ignore_case()
    {
        await _store.CreateAsync(NewContainer("beaga", "Beaga Cultural", ContainerKind.Community));
        await _store.CreateAsync(NewContainer("quarta", "Grupo Quarta")); // group preset: unlisted
        await _store.CreateAsync(NewContainer(
            "biblioteca-sao-jorge", "Biblioteca São Jorge", ContainerKind.Community));
        await _store.UpdateAsync(
            "biblioteca-sao-jorge", null, row => row with { DeletedAt = Now });
        await _store.CreateAsync(NewContainer(
            "sao-jorge-cinema", "São Jorge Cinema", ContainerKind.Community));

        // Substring of the slug and of the name, case-insensitive both ways (GAP-15, no FTS).
        Assert.Equal(new[] { "beaga" }, await SlugsAsync("beaga"));
        Assert.Equal(new[] { "beaga" }, await SlugsAsync("BEAGA"));
        Assert.Equal(new[] { "beaga" }, await SlugsAsync("cultural"));
        Assert.Equal(new[] { "sao-jorge-cinema" }, await SlugsAsync("SÃO"));

        // The unlisted group and the soft-deleted community stay out whatever the term is —
        // "quarta" is the exact name of one and the exact slug of the other never comes back.
        Assert.Empty(await SlugsAsync("quarta"));
        Assert.Empty(await SlugsAsync("biblioteca"));

        // Ordered by slug so the page walk is stable: limit/offset cut the filtered list.
        Assert.Equal(new[] { "beaga", "sao-jorge-cinema" }, await SlugsAsync("a"));
        Assert.Equal(new[] { "sao-jorge-cinema" }, await SlugsAsync("a", limit: 1, offset: 1));
        Assert.Empty(await SlugsAsync("a", limit: 1, offset: 2));
    }

    private async Task<string[]> SlugsAsync(string query, int limit = 100, int offset = 0) =>
        (await _store.SearchAsync(query, limit, offset)).Select(container => container.Slug).ToArray();

    private static Container NewContainer(
        string slug, string name = "Turma", ContainerKind kind = ContainerKind.Group) =>
        Container.Create(slug, kind, name, string.Empty,
            "https://squid.test/users/alice", "https://squid.test", Now);

    private static ContainerMembership Owner(string slug) => new()
    {
        Slug = slug,
        ActorId = "https://squid.test/users/alice",
        Handle = "alice",
        Role = MembershipRole.Owner,
        State = MembershipState.Active,
        JoinedAt = Now,
    };

    private static string Hash(Container container) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(container))));
}
