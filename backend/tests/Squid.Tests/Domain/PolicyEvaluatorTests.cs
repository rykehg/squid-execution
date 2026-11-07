using Squid.Domain;

namespace Squid.Tests.Domain;

/// <summary>
/// AC-03, AC-04 and AC-05 of TASK-0007: every decision carries permission, reason and policy
/// version; the full role × action matrix is asserted against the strict hierarchy (FR-012,
/// data-model §3.2); and a permissive policy never grants Guest what the role does not hold.
/// </summary>
public sealed class PolicyEvaluatorTests
{
    private readonly PolicyEvaluator _evaluator = new();

    /// <summary>Maximally permissive policy: the matrix measures the hierarchy, not the policy.</summary>
    private static ContainerPolicy Permissive(int version = 1) => new(
        PostingPolicy.Everyone,
        VisibilityPolicy.Public,
        JoinPolicy.Open,
        new InteractionPolicy(PostingPolicy.Everyone, PostingPolicy.Everyone, PostingPolicy.Everyone, PostingPolicy.Everyone),
        [],
        version);

    [Fact]
    public void DecisionCarriesPermissionReasonAndPolicyVersion()
    {
        var policy = Permissive(version: 7);

        var allowed = _evaluator.Evaluate(PolicyAction.Post, ContainerRole.Member, policy);
        var denied = _evaluator.Evaluate(PolicyAction.Post, ContainerRole.Guest, policy);

        Assert.True(allowed.Permitted);
        Assert.False(denied.Permitted);
        Assert.False(string.IsNullOrWhiteSpace(allowed.Reason));
        Assert.False(string.IsNullOrWhiteSpace(denied.Reason));
        Assert.Equal(7, allowed.PolicyVersion);
        Assert.Equal(7, denied.PolicyVersion);
    }

    // AC-04: every role × every action, expected by the strict hierarchy under a permissive policy.
    [Theory]
    [InlineData(ContainerRole.Owner, PolicyAction.Post, true)]
    [InlineData(ContainerRole.Owner, PolicyAction.Comment, true)]
    [InlineData(ContainerRole.Owner, PolicyAction.React, true)]
    [InlineData(ContainerRole.Owner, PolicyAction.Share, true)]
    [InlineData(ContainerRole.Owner, PolicyAction.Attach, true)]
    [InlineData(ContainerRole.Owner, PolicyAction.Join, true)]
    [InlineData(ContainerRole.Owner, PolicyAction.Moderate, true)]
    [InlineData(ContainerRole.Owner, PolicyAction.Admin, true)]
    [InlineData(ContainerRole.Admin, PolicyAction.Post, true)]
    [InlineData(ContainerRole.Admin, PolicyAction.Comment, true)]
    [InlineData(ContainerRole.Admin, PolicyAction.React, true)]
    [InlineData(ContainerRole.Admin, PolicyAction.Share, true)]
    [InlineData(ContainerRole.Admin, PolicyAction.Attach, true)]
    [InlineData(ContainerRole.Admin, PolicyAction.Join, true)]
    [InlineData(ContainerRole.Admin, PolicyAction.Moderate, true)]
    [InlineData(ContainerRole.Admin, PolicyAction.Admin, true)]
    [InlineData(ContainerRole.Moderator, PolicyAction.Post, true)]
    [InlineData(ContainerRole.Moderator, PolicyAction.Comment, true)]
    [InlineData(ContainerRole.Moderator, PolicyAction.React, true)]
    [InlineData(ContainerRole.Moderator, PolicyAction.Share, true)]
    [InlineData(ContainerRole.Moderator, PolicyAction.Attach, true)]
    [InlineData(ContainerRole.Moderator, PolicyAction.Join, true)]
    [InlineData(ContainerRole.Moderator, PolicyAction.Moderate, true)]
    [InlineData(ContainerRole.Moderator, PolicyAction.Admin, false)]
    [InlineData(ContainerRole.Member, PolicyAction.Post, true)]
    [InlineData(ContainerRole.Member, PolicyAction.Comment, true)]
    [InlineData(ContainerRole.Member, PolicyAction.React, true)]
    [InlineData(ContainerRole.Member, PolicyAction.Share, true)]
    [InlineData(ContainerRole.Member, PolicyAction.Attach, true)]
    [InlineData(ContainerRole.Member, PolicyAction.Join, true)]
    [InlineData(ContainerRole.Member, PolicyAction.Moderate, false)]
    [InlineData(ContainerRole.Member, PolicyAction.Admin, false)]
    [InlineData(ContainerRole.Guest, PolicyAction.Post, false)]
    [InlineData(ContainerRole.Guest, PolicyAction.Comment, false)]
    [InlineData(ContainerRole.Guest, PolicyAction.React, false)]
    [InlineData(ContainerRole.Guest, PolicyAction.Share, false)]
    [InlineData(ContainerRole.Guest, PolicyAction.Attach, false)]
    [InlineData(ContainerRole.Guest, PolicyAction.Join, true)]
    [InlineData(ContainerRole.Guest, PolicyAction.Moderate, false)]
    [InlineData(ContainerRole.Guest, PolicyAction.Admin, false)]
    public void CapabilityMatrixMatchesStrictHierarchy(ContainerRole role, PolicyAction action, bool expected)
    {
        var decision = _evaluator.Evaluate(action, role, Permissive());

        Assert.Equal(expected, decision.Permitted);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason)); // no silent decision (AC-03)
    }

    [Fact]
    public void GuestStaysDeniedWhenPostingIsEveryone()
    {
        var decision = _evaluator.Evaluate(PolicyAction.Post, ContainerRole.Guest, Permissive());

        Assert.False(decision.Permitted);
        Assert.Contains("Guest", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SinglePublisherFailsClosedWhenOnlyTheRoleIsKnown()
    {
        var policy = new ContainerPolicy(
            PostingPolicy.SinglePublisher, VisibilityPolicy.Public, JoinPolicy.Open,
            new InteractionPolicy(), ["https://peer.test/actors/news"], 1);

        var decision = _evaluator.Evaluate(PolicyAction.Post, ContainerRole.Owner, policy);

        Assert.False(decision.Permitted);
        Assert.Contains("SinglePublisher", decision.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JoinPolicy.Open, true)]
    [InlineData(JoinPolicy.Request, true)]
    [InlineData(JoinPolicy.Invite, false)]
    [InlineData(JoinPolicy.Closed, false)]
    public void JoinPolicyDecidesEntry(JoinPolicy join, bool expected)
    {
        var policy = new ContainerPolicy(
            PostingPolicy.Members, VisibilityPolicy.Public, join, new InteractionPolicy(), [], 1);

        var decision = _evaluator.Evaluate(PolicyAction.Join, ContainerRole.Guest, policy);

        Assert.Equal(expected, decision.Permitted);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
    }

    [Fact]
    public void EachInteractionGateIsEvaluatedOnItsOwn()
    {
        var policy = new ContainerPolicy(
            PostingPolicy.Everyone, VisibilityPolicy.Public, JoinPolicy.Open,
            new InteractionPolicy(PostingPolicy.Moderators, PostingPolicy.Everyone, PostingPolicy.Everyone, PostingPolicy.Everyone),
            [],
            1);

        Assert.False(_evaluator.Evaluate(PolicyAction.Comment, ContainerRole.Member, policy).Permitted);
        Assert.True(_evaluator.Evaluate(PolicyAction.React, ContainerRole.Member, policy).Permitted);
        Assert.True(_evaluator.Evaluate(PolicyAction.Comment, ContainerRole.Moderator, policy).Permitted);
    }

    [Fact]
    public void InvalidEvaluationInputThrowsInsteadOfDeciding()
    {
        var policy = Permissive();

        Assert.Throws<ArgumentNullException>(() => _evaluator.Evaluate(PolicyAction.Post, ContainerRole.Member, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => _evaluator.Evaluate((PolicyAction)99, ContainerRole.Member, policy));
        Assert.Throws<ArgumentOutOfRangeException>(() => _evaluator.Evaluate(PolicyAction.Post, (ContainerRole)99, policy));
    }
}
