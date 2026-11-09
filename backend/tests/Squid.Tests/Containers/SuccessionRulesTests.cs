using Squid.Domain;
using Squid.Domain.Containers;

namespace Squid.Tests.Containers;

/// <summary>
/// The pure half of TASK-0021 at the domain edge: the claim's deadline boundary (contract §9.12),
/// the ownership swap that can never orphan the container (data-model §2.2, DoD), and the soft
/// delete's own rules — the grace window (GAP-17) and the search filter of SPEC-0008 H.
/// </summary>
public sealed class SuccessionRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-08T11:00:00Z");

    [Fact] // AC-01 — the deadline boundary: inside the window is refused, the instant itself opens
    public void The_claim_opens_exactly_at_the_deadline_and_not_one_moment_before()
    {
        Assert.False(SuccessionRules.ClaimAllowed(Now.AddDays(89.99), Now, 90));
        Assert.True(SuccessionRules.ClaimAllowed(Now.AddDays(90), Now, 90));
        Assert.True(SuccessionRules.ClaimAllowed(Now.AddDays(90.01), Now, 90));
    }

    [Fact] // AC-03/AC-05 — the swap leaves one active Owner, whoever moved onto whom
    public void Every_ownership_move_ends_with_exactly_one_active_owner_row()
    {
        var owner = Row("alice", MembershipRole.Owner, MembershipState.Active);
        var member = Row("bob", MembershipRole.Member, MembershipState.Active);
        var members = new[] { owner, member };

        // Onto another row: the old owner demotes, the target lands Owner + Active.
        var swapped = SuccessionRules.Transfer(members, "grupo", "bob", Actor("bob"), Now);
        var swappedOwner = swapped.Single(row => row.Handle == "bob");
        Assert.Equal(MembershipRole.Owner, swappedOwner.Role);
        Assert.Equal(MembershipState.Active, swappedOwner.State);
        Assert.Equal(MembershipRole.Admin, swapped.Single(row => row.Handle == "alice").Role);

        // Onto a row nobody has yet: it is created as the active Owner, nobody is demoted twice.
        var created = SuccessionRules.Transfer(members, "grupo", "carol", Actor("carol"), Now);
        Assert.Equal(2, created.Count);
        Assert.Equal(MembershipRole.Admin, created.Single(row => row.Handle == "alice").Role);
        var fresh = created.Single(row => row.Handle == "carol");
        Assert.Equal(MembershipRole.Owner, fresh.Role);
        Assert.Equal(MembershipState.Active, fresh.State);
        Assert.Equal(Now, fresh.JoinedAt);

        // Onto the owner itself: no demotion, so the container keeps its one active Owner.
        var same = SuccessionRules.Transfer(members, "grupo", "alice", Actor("alice"), Now);
        Assert.Single(same);
        Assert.Equal(MembershipRole.Owner, same[0].Role);
        Assert.Equal(MembershipState.Active, same[0].State);
    }

    [Fact] // AC-04 — grace window from DeletedAt, search rule of SPEC-0008 H
    public void The_soft_delete_stays_recoverable_for_the_grace_and_leaves_the_search()
    {
        var container = Container.Create(
            "comunidade", ContainerKind.Community, "Comunidade", string.Empty,
            Actor("alice"), "https://squid.test", Now);

        // Alive: no window, and a listed container is searchable.
        Assert.Null(container.RecoverableUntil(30));
        Assert.False(container.IsDeleted);
        Assert.True(container.IsSearchable);

        // Deleted: the window is DeletedAt + grace, the row is flagged and out of the search.
        var deleted = container with { DeletedAt = Now };
        Assert.Equal(Now.AddDays(30), deleted.RecoverableUntil(30));
        Assert.Equal(Now.AddDays(3), deleted.RecoverableUntil(3));
        Assert.True(deleted.IsDeleted);
        Assert.False(deleted.IsSearchable);

        // An unlisted container was never searchable, deleted or not (FR-016: Listed only).
        var group = Container.Create(
            "grupo", ContainerKind.Group, "Grupo", string.Empty,
            Actor("alice"), "https://squid.test", Now);
        Assert.False(group.IsSearchable);
        Assert.False((group with { DeletedAt = Now }).IsSearchable);
    }

    [Fact] // GAP-17 — the claim clock is the owner's activity, born with the container
    public void The_claim_clock_is_the_owner_activity_and_falls_back_to_the_creation()
    {
        var container = Container.Create(
            "turma", ContainerKind.Group, "Turma", string.Empty,
            Actor("alice"), "https://squid.test", Now);

        Assert.Equal(Now, container.OwnerActivityAt); // born with the owner
        Assert.Equal(Now.AddDays(5), (container with { OwnerLastActiveAt = Now.AddDays(5) }).OwnerActivityAt);

        // A row persisted before the field existed deserializes without it: creation is the clock.
        var legacy = container with { OwnerLastActiveAt = null };
        Assert.Equal(Now, legacy.OwnerActivityAt);
    }

    private static ContainerMembership Row(string handle, MembershipRole role, MembershipState state) => new()
    {
        Slug = "grupo",
        ActorId = Actor(handle),
        Handle = handle,
        Role = role,
        State = state,
        JoinedAt = Now,
    };

    private static string Actor(string handle) => $"https://squid.test/users/{handle}";
}
