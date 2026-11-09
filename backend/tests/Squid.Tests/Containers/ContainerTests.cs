using Squid.Domain;
using Squid.Domain.Containers;

namespace Squid.Tests.Containers;

/// <summary>
/// SPEC-0008 A over data-model §2.2, at the domain edge: the kind only picks the preset, and the
/// slug rule quoted by the route's 400 is the one proven here.
/// </summary>
public sealed class ContainerTests
{
    private const string Origin = "https://squid.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-08T11:00:00Z");

    [Fact] // AC-01 (domain half)
    public void Group_is_born_members_only_invite_unlisted_at_policy_version_one()
    {
        var container = Container.Create(
            "turma", ContainerKind.Group, "Turma", "o grupo",
            "https://squid.test/users/alice", Origin, Now);

        Assert.Equal("https://squid.test/c/turma", container.Id);
        Assert.Equal(ContainerKind.Group, container.Kind);
        Assert.False(container.Listed);
        Assert.Equal(1, container.MemberCount);
        Assert.Equal("https://squid.test/users/alice", container.OwnerActorId);
        Assert.Equal(Now, container.CreatedAt);

        Assert.Equal(VisibilityPolicy.MembersOnly, container.Policy.Visibility);
        Assert.Equal(JoinPolicy.Invite, container.Policy.Join);
        Assert.Equal(PostingPolicy.Members, container.Policy.Posting);
        Assert.Equal(PostingPolicy.Members, container.Policy.Interaction.CanComment);
        Assert.Equal(PostingPolicy.Members, container.Policy.Interaction.CanReact);
        Assert.Equal(PostingPolicy.Members, container.Policy.Interaction.CanShare);
        Assert.Equal(PostingPolicy.Members, container.Policy.Interaction.CanAttach);
        Assert.Equal(1, container.Policy.Version);
        Assert.Empty(container.Policy.PublisherIds);
    }

    [Fact] // AC-02 (domain half)
    public void Community_is_born_public_open_listed_at_policy_version_one()
    {
        var container = Container.Create(
            "rust", ContainerKind.Community, "Rust", "a comunidade",
            "https://squid.test/users/alice", Origin, Now);

        Assert.Equal("https://squid.test/c/rust", container.Id);
        Assert.True(container.Listed);
        Assert.Equal(1, container.MemberCount);

        Assert.Equal(VisibilityPolicy.Public, container.Policy.Visibility);
        Assert.Equal(JoinPolicy.Open, container.Policy.Join);
        Assert.Equal(PostingPolicy.Members, container.Policy.Posting);
        Assert.Equal(PostingPolicy.Members, container.Policy.Interaction.CanComment);
        Assert.Equal(PostingPolicy.Members, container.Policy.Interaction.CanReact);
        Assert.Equal(PostingPolicy.Members, container.Policy.Interaction.CanShare);
        Assert.Equal(PostingPolicy.Members, container.Policy.Interaction.CanAttach);
        Assert.Equal(1, container.Policy.Version);
    }

    [Fact]
    public void The_slug_rule_accepts_a_path_segment_and_rejects_the_rest()
    {
        Assert.True(ContainerSlug.IsValid("turma"));
        Assert.True(ContainerSlug.IsValid("grupo-01"));
        Assert.True(ContainerSlug.IsValid("A.B_c"));
        Assert.True(ContainerSlug.IsValid("a"));
        Assert.True(ContainerSlug.IsValid(new string('a', 64)));

        Assert.False(ContainerSlug.IsValid(null));
        Assert.False(ContainerSlug.IsValid(string.Empty));
        Assert.False(ContainerSlug.IsValid("tem espaco"));
        Assert.False(ContainerSlug.IsValid("com/barra"));
        Assert.False(ContainerSlug.IsValid("-comeca-assim"));
        Assert.False(ContainerSlug.IsValid("termina-assim-"));
        Assert.False(ContainerSlug.IsValid(new string('a', 65)));
    }

    [Fact]
    public void A_kind_outside_the_domain_is_refused_at_construction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Container.Create(
            "x", (ContainerKind)9, "nome", string.Empty,
            "https://squid.test/users/alice", Origin, Now));
    }
}
