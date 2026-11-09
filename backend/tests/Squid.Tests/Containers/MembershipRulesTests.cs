using Squid.Domain;
using Squid.Domain.Containers;

namespace Squid.Tests.Containers;

/// <summary>
/// The §3.3 state machine of SPEC-0008 section E as pure rules: every branch of
/// <see cref="MembershipRules.JoinTarget"/> (AC-01/AC-02/AC-03/AC-04), the gate an invitation
/// needs (AC-03) and the only state that leaves (AC-04). The routes add session, visibility and
/// the evaluator on top of exactly these.
/// </summary>
public sealed class MembershipRulesTests
{
    [Fact] // AC-01 / AC-02
    public void Join_lands_where_the_policy_and_the_current_state_say_it_may()
    {
        // Open enters as Active, Request parks as Requested — including a re-entry after Left (AC-04).
        Assert.Equal(MembershipState.Active, MembershipRules.JoinTarget(null, JoinPolicy.Open, true));
        Assert.Equal(MembershipState.Requested, MembershipRules.JoinTarget(null, JoinPolicy.Request, true));
        Assert.Equal(MembershipState.Active, MembershipRules.JoinTarget(MembershipState.Left, JoinPolicy.Open, true));
        Assert.Equal(MembershipState.Requested, MembershipRules.JoinTarget(MembershipState.Left, JoinPolicy.Request, true));

        // An actor already Active never moves downwards: the same state, no demotion (AC-04 re-join).
        Assert.Equal(MembershipState.Active, MembershipRules.JoinTarget(MembershipState.Active, JoinPolicy.Open, true));
        Assert.Equal(MembershipState.Active, MembershipRules.JoinTarget(MembershipState.Active, JoinPolicy.Request, true));

        // The evaluator denies invite-without-invitation and Closed: no target at all (AC-02).
        Assert.Null(MembershipRules.JoinTarget(null, JoinPolicy.Invite, false));
        Assert.Null(MembershipRules.JoinTarget(MembershipState.Left, JoinPolicy.Invite, false));
        Assert.Null(MembershipRules.JoinTarget(null, JoinPolicy.Closed, false));
        Assert.Null(MembershipRules.JoinTarget(MembershipState.Active, JoinPolicy.Closed, false));
    }

    [Fact] // AC-03
    public void An_invitation_in_force_is_the_entry_ticket_of_invite_and_only_closed_cancels_it()
    {
        Assert.Equal(MembershipState.Active, MembershipRules.JoinTarget(MembershipState.Invited, JoinPolicy.Invite, false));
        Assert.Equal(MembershipState.Active, MembershipRules.JoinTarget(MembershipState.Invited, JoinPolicy.Open, true));
        Assert.Null(MembershipRules.JoinTarget(MembershipState.Invited, JoinPolicy.Closed, false));
    }

    [Fact] // §3.3 — TASK-0020's states never transition by self-service
    public void Silenced_and_banned_never_move_by_joining()
    {
        Assert.Null(MembershipRules.JoinTarget(MembershipState.Silenced, JoinPolicy.Open, true));
        Assert.Null(MembershipRules.JoinTarget(MembershipState.Banned, JoinPolicy.Open, true));
    }

    [Fact] // AC-03 / AC-04
    public void Only_a_non_member_or_a_former_member_can_be_invited_and_only_an_active_member_leaves()
    {
        Assert.True(MembershipRules.CanInvite(null));
        Assert.True(MembershipRules.CanInvite(MembershipState.Left));
        Assert.False(MembershipRules.CanInvite(MembershipState.Invited));
        Assert.False(MembershipRules.CanInvite(MembershipState.Requested));
        Assert.False(MembershipRules.CanInvite(MembershipState.Active));
        Assert.False(MembershipRules.CanInvite(MembershipState.Silenced));
        Assert.False(MembershipRules.CanInvite(MembershipState.Banned));

        Assert.True(MembershipRules.CanLeave(MembershipState.Active));
        Assert.False(MembershipRules.CanLeave(null));
        foreach (var state in new[]
        {
            MembershipState.Invited, MembershipState.Requested,
            MembershipState.Silenced, MembershipState.Banned, MembershipState.Left,
        })
        {
            Assert.False(MembershipRules.CanLeave(state));
        }
    }

    [Fact] // TASK-0020 AC-02 — the §3.3 moderation verbs: mapped edges land, everything else refuses
    public void The_moderation_verbs_follow_the_machine_and_refuse_everything_else()
    {
        var approve = MembershipRules.Apply(MembershipState.Requested, MembershipChange.Approve);
        Assert.False(approve!.RemovesRow);
        Assert.Equal(MembershipState.Active, approve.State);

        // reject: §3.3's (removido) — the row goes, no state replaces it.
        var reject = MembershipRules.Apply(MembershipState.Requested, MembershipChange.Reject);
        Assert.True(reject!.RemovesRow);
        Assert.Null(reject.State);

        var silence = MembershipRules.Apply(MembershipState.Active, MembershipChange.Silence);
        Assert.False(silence!.RemovesRow);
        Assert.Equal(MembershipState.Silenced, silence.State);

        var unsilence = MembershipRules.Apply(MembershipState.Silenced, MembershipChange.Unsilence);
        Assert.Equal(MembershipState.Active, unsilence!.State);

        var ban = MembershipRules.Apply(MembershipState.Active, MembershipChange.Ban);
        Assert.Equal(MembershipState.Banned, ban!.State);

        var unban = MembershipRules.Apply(MembershipState.Banned, MembershipChange.Unban);
        Assert.Equal(MembershipState.Active, unban!.State);

        // Every edge outside the diagram is the refusal, never a guess (deny by default).
        Assert.Null(MembershipRules.Apply(MembershipState.Active, MembershipChange.Approve));
        Assert.Null(MembershipRules.Apply(MembershipState.Invited, MembershipChange.Approve));
        Assert.Null(MembershipRules.Apply(MembershipState.Invited, MembershipChange.Reject));
        Assert.Null(MembershipRules.Apply(MembershipState.Requested, MembershipChange.Ban));
        Assert.Null(MembershipRules.Apply(MembershipState.Silenced, MembershipChange.Ban));
        Assert.Null(MembershipRules.Apply(MembershipState.Banned, MembershipChange.Silence));
        Assert.Null(MembershipRules.Apply(MembershipState.Left, MembershipChange.Unsilence));
        Assert.Null(MembershipRules.Apply(MembershipState.Active, MembershipChange.Unban));
    }
}
