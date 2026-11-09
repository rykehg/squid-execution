using Squid.Domain;
using Squid.Domain.Containers;

namespace Squid.Tests.Domain;

/// <summary>
/// GAP-11 (SPEC-0008 section F, AC-04 of TASK-0020): the evaluator answers with the full actor —
/// role, membership state and publisher seat. The Sprint 2 DoD facet lives here: <c>SinglePublisher</c>
/// blocks a plain member and admits the named active publisher; <c>Silenced</c> reads but never
/// writes; <c>Banned</c> answers nothing; <c>Guest</c> stays denied under any policy. The role-only
/// path keeps the TASK-0007 matrix exactly as it was (<see cref="PolicyEvaluatorTests"/>).
/// </summary>
public sealed class PolicyActorContextTests
{
    private readonly PolicyEvaluator _evaluator = new();

    /// <summary>Maximally permissive policy: the context, not the gates, is what is measured.</summary>
    private static ContainerPolicy Permissive(int version = 1) => new(
        PostingPolicy.Everyone,
        VisibilityPolicy.Public,
        JoinPolicy.Open,
        new InteractionPolicy(PostingPolicy.Everyone, PostingPolicy.Everyone, PostingPolicy.Everyone, PostingPolicy.Everyone),
        [],
        version);

    private static ContainerPolicy SinglePublisher(int version = 4) => new(
        PostingPolicy.SinglePublisher,
        VisibilityPolicy.Public,
        JoinPolicy.Open,
        new InteractionPolicy(),
        ["https://squid.test/users/news"],
        version);

    [Fact] // AC-04 — the DoD facet: blocks the plain member, admits the named active publisher
    public void SinglePublisher_admits_the_named_publisher_and_denies_a_plain_member()
    {
        var policy = SinglePublisher();

        var publisher = _evaluator.Evaluate(
            PolicyAction.Post, new PolicyActor(ContainerRole.Member, MembershipState.Active, true), policy);
        var plain = _evaluator.Evaluate(
            PolicyAction.Post, new PolicyActor(ContainerRole.Member, MembershipState.Active, false), policy);

        Assert.True(publisher.Permitted);
        Assert.Equal(4, publisher.PolicyVersion); // no decision is silent about its version either
        Assert.False(plain.Permitted);
        Assert.Contains("SinglePublisher", plain.Reason, StringComparison.Ordinal);

        // The role-only path of TASK-0007 keeps failing closed for the very same policy.
        Assert.False(_evaluator.Evaluate(PolicyAction.Post, ContainerRole.Member, policy).Permitted);
        Assert.False(_evaluator.Evaluate(PolicyAction.Post, ContainerRole.Owner, policy).Permitted);
    }

    [Fact] // AC-04 — the publisher seat only counts on the one state §3.3 lets write
    public void The_publisher_seat_never_survives_a_state_that_cannot_write()
    {
        var policy = SinglePublisher();
        var publisher = new PolicyActor(ContainerRole.Member, MembershipState.Active, true);

        Assert.False(_evaluator.Evaluate(
            PolicyAction.Post, publisher with { State = MembershipState.Silenced }, policy).Permitted);
        Assert.False(_evaluator.Evaluate(
            PolicyAction.Post, publisher with { State = MembershipState.Banned }, policy).Permitted);
        Assert.False(_evaluator.Evaluate(
            PolicyAction.Post, publisher with { State = null }, policy).Permitted);
    }

    [Fact] // AC-03 — Silenced reads but never writes, whatever the role and the policy say
    public void A_silenced_member_is_denied_every_write_even_as_admin_on_a_permissive_policy()
    {
        var policy = Permissive();
        var silenced = new PolicyActor(ContainerRole.Admin, MembershipState.Silenced, false);

        foreach (var action in new[]
        {
            PolicyAction.Post, PolicyAction.Comment, PolicyAction.React,
            PolicyAction.Share, PolicyAction.Attach, PolicyAction.Moderate, PolicyAction.Admin,
        })
        {
            var decision = _evaluator.Evaluate(action, silenced, policy);
            Assert.False(decision.Permitted);
            Assert.Contains("Silenced", decision.Reason, StringComparison.Ordinal);
        }
    }

    [Fact] // FR-018 — Banned answers nothing at all, not even entry
    public void A_banned_member_is_denied_every_action_including_join()
    {
        var policy = Permissive();
        var banned = new PolicyActor(ContainerRole.Owner, MembershipState.Banned, false);

        foreach (var action in Enum.GetValues<PolicyAction>())
        {
            Assert.False(_evaluator.Evaluate(action, banned, policy).Permitted);
        }
    }

    [Fact] // data-model §2.4 — the policy never grants Guest what the role does not hold
    public void Guest_is_denied_every_write_under_a_permissive_policy_but_still_joins()
    {
        var policy = Permissive();
        var guest = new PolicyActor(ContainerRole.Guest, MembershipState.Active, false);

        foreach (var action in new[]
        {
            PolicyAction.Post, PolicyAction.Comment, PolicyAction.React,
            PolicyAction.Share, PolicyAction.Attach, PolicyAction.Moderate, PolicyAction.Admin,
        })
        {
            Assert.False(_evaluator.Evaluate(action, guest, policy).Permitted);
        }

        Assert.True(_evaluator.Evaluate(PolicyAction.Join, guest, policy).Permitted); // entry is not a write
    }

    [Fact] // deny by default — no row is no membership; a closed policy still closes the door
    public void A_write_without_a_membership_row_is_denied_and_entry_follows_the_policy()
    {
        var outsider = new PolicyActor(ContainerRole.Guest, null, false);

        Assert.False(_evaluator.Evaluate(PolicyAction.Post, outsider, Permissive()).Permitted);
        Assert.False(_evaluator.Evaluate(PolicyAction.Moderate, outsider, Permissive()).Permitted);
        Assert.True(_evaluator.Evaluate(PolicyAction.Join, outsider, Permissive()).Permitted);

        var closed = new ContainerPolicy(
            PostingPolicy.Everyone, VisibilityPolicy.Public, JoinPolicy.Closed,
            new InteractionPolicy(), [], 1);
        Assert.False(_evaluator.Evaluate(PolicyAction.Join, outsider, closed).Permitted);
    }

    [Fact] // the expected decision never throws; invalid input does
    public void Invalid_context_input_throws_instead_of_deciding()
    {
        var policy = Permissive();
        var valid = new PolicyActor(ContainerRole.Member, MembershipState.Active, false);

        Assert.Throws<ArgumentNullException>(
            () => _evaluator.Evaluate(PolicyAction.Post, (PolicyActor)null!, policy));
        Assert.Throws<ArgumentNullException>(
            () => _evaluator.Evaluate(PolicyAction.Post, valid, null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _evaluator.Evaluate((PolicyAction)99, valid, policy));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _evaluator.Evaluate(PolicyAction.Post, valid with { Role = (ContainerRole)99 }, policy));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _evaluator.Evaluate(PolicyAction.Post, valid with { State = (MembershipState)99 }, policy));
    }
}
