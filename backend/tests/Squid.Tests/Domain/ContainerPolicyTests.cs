using System.Reflection;
using Squid.Domain;

namespace Squid.Tests.Domain;

/// <summary>
/// AC-01 and AC-02 of TASK-0007: the record carries every field of data-model §2.3, the presets
/// follow doc 05 §2.3, and an invalid SinglePublisher policy never gets built (FR-015).
/// </summary>
public sealed class ContainerPolicyTests
{
    [Fact]
    public void PolicyIsAnImmutableRecordCarryingEveryField()
    {
        var publisherIds = new[] { "https://peer.test/actors/news" };
        var policy = new ContainerPolicy(
            PostingPolicy.SinglePublisher,
            VisibilityPolicy.Public,
            JoinPolicy.Open,
            new InteractionPolicy(PostingPolicy.Everyone),
            publisherIds,
            3);

        Assert.Equal(PostingPolicy.SinglePublisher, policy.Posting);
        Assert.Equal(VisibilityPolicy.Public, policy.Visibility);
        Assert.Equal(JoinPolicy.Open, policy.Join);
        Assert.Equal(PostingPolicy.Everyone, policy.Interaction.CanComment);
        Assert.Equal(publisherIds, policy.PublisherIds);
        Assert.Equal(3, policy.Version);

        // immutable record: no setter reaches a field, and EqualityContract marks the record type
        foreach (var property in typeof(ContainerPolicy).GetProperties())
        {
            Assert.Null(property.SetMethod);
        }

        Assert.NotNull(typeof(ContainerPolicy).GetProperty(
            "EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void GroupPresetFollowsDoc05Defaults()
    {
        var group = ContainerPolicy.Group();

        Assert.Equal(PostingPolicy.Members, group.Posting);           // postagem padrão membersOnly
        Assert.Equal(VisibilityPolicy.MembersOnly, group.Visibility); // privacidade padrão membersOnly
        Assert.Equal(JoinPolicy.Invite, group.Join);                  // entrada padrão invite
        Assert.Equal(new InteractionPolicy(), group.Interaction);     // interação padrão members
        Assert.Empty(group.PublisherIds);
        Assert.Equal(1, group.Version);
    }

    [Fact]
    public void CommunityPresetFollowsDoc05Defaults()
    {
        var community = ContainerPolicy.Community();

        Assert.Equal(PostingPolicy.Members, community.Posting);      // postagem padrão membersOnly
        Assert.Equal(VisibilityPolicy.Public, community.Visibility); // privacidade padrão public
        Assert.Equal(JoinPolicy.Open, community.Join);               // entrada padrão open|request

        var requested = ContainerPolicy.Community(JoinPolicy.Request);
        Assert.Equal(JoinPolicy.Request, requested.Join);
        Assert.Equal(VisibilityPolicy.Public, requested.Visibility);
        Assert.Equal(PostingPolicy.Members, requested.Posting);
    }

    [Fact]
    public void SinglePublisherWithoutPublisherIdsIsRejectedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new ContainerPolicy(
            PostingPolicy.SinglePublisher, VisibilityPolicy.Public, JoinPolicy.Open,
            new InteractionPolicy(), [], 1));

        Assert.Throws<ArgumentNullException>(() => new ContainerPolicy(
            PostingPolicy.SinglePublisher, VisibilityPolicy.Public, JoinPolicy.Open,
            new InteractionPolicy(), null!, 1));

        var valid = new ContainerPolicy(
            PostingPolicy.SinglePublisher, VisibilityPolicy.Public, JoinPolicy.Open,
            new InteractionPolicy(), ["https://peer.test/actors/news"], 1);
        Assert.Single(valid.PublisherIds);
    }

    [Fact]
    public void UndefinedPolicyValuesAreRejectedAtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerPolicy(
            (PostingPolicy)99, VisibilityPolicy.Public, JoinPolicy.Open, new InteractionPolicy(), [], 1));

        Assert.Throws<ArgumentException>(() => new ContainerPolicy(
            PostingPolicy.Members, VisibilityPolicy.Public, JoinPolicy.Open,
            new InteractionPolicy((PostingPolicy)99), [], 1));
    }

    [Fact]
    public void NextVersionGrowsAndKeepsEverythingElse()
    {
        var policy = ContainerPolicy.Community(JoinPolicy.Request);
        var next = policy.NextVersion();

        Assert.Equal(policy.Version + 1, next.Version);
        Assert.Equal(policy.Posting, next.Posting);
        Assert.Equal(policy.Visibility, next.Visibility);
        Assert.Equal(policy.Join, next.Join);
        Assert.Equal(policy.Interaction, next.Interaction);
        Assert.Equal(policy.PublisherIds, next.PublisherIds);
    }
}
