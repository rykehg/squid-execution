namespace Squid.Domain.Containers;

/// <summary>
/// The <c>Membership.State</c> machine of data-model §3.3 as pure rules (SPEC-0008 section E):
/// no I/O, no session and no policy of its own — <see cref="IPolicyEvaluator"/> already answers
/// whether the join policy admits this actor, and the states decide what the outcome of a join,
/// an invite or a leave may be on top of that verdict.
/// </summary>
public static class MembershipRules
{
    /// <summary>
    /// The state a successful join lands on, or null when it is refused:
    /// <c>Silenced</c> and <c>Banned</c> only move by an admin action (§3.3); an invitation in
    /// force is the entry ticket of <c>Invite</c> for <b>this</b> actor (GAP-13, SC-006) and only
    /// <c>Closed</c> cancels it; everything else follows the evaluator's verdict — <c>Open</c>
    /// enters as <c>Member</c>, <c>Request</c> lands as <c>Requested</c>, <c>Invite</c> without an
    /// invitation and <c>Closed</c> deny (FR-014), and an actor already <c>Active</c> never moves
    /// downwards (a re-join is the same state, not a demotion).
    /// </summary>
    public static MembershipState? JoinTarget(MembershipState? current, JoinPolicy policy, bool entryAllowed)
    {
        if (current is MembershipState.Silenced or MembershipState.Banned) return null;
        if (current is MembershipState.Invited)
        {
            return policy == JoinPolicy.Closed ? null : MembershipState.Active;
        }

        if (!entryAllowed) return null;
        if (current is MembershipState.Active) return MembershipState.Active;
        return policy == JoinPolicy.Open ? MembershipState.Active : MembershipState.Requested;
    }

    /// <summary>
    /// An invite only lands where there is no link or the link is over: no row, or a former member
    /// re-invited after <c>Left</c> (§3.3 re-entry). Every other state answers with the simplest
    /// refusal of the gaps table — 400 — never a second invitation on top of one already there.
    /// </summary>
    public static bool CanInvite(MembershipState? current) => current is null or MembershipState.Left;

    /// <summary>Only an active member leaves; §3.3 gives every other state no leave edge.</summary>
    public static bool CanLeave(MembershipState? current) => current is MembershipState.Active;

    /// <summary>
    /// Applies <paramref name="change"/> to <paramref name="current"/> — the §3.3 machine as a
    /// pure function: null when the machine has no such edge (approve only from <c>Requested</c>,
    /// silence only from <c>Active</c>, ban only from <c>Active</c>, unban only from <c>Banned</c>),
    /// never a guess — the unmapped path is the refusal (SPEC-0008 non-functional).
    /// </summary>
    public static MembershipTransition? Apply(MembershipState current, MembershipChange change) =>
        (current, change) switch
        {
            (MembershipState.Requested, MembershipChange.Approve) => new MembershipTransition(false, MembershipState.Active),
            (MembershipState.Requested, MembershipChange.Reject) => new MembershipTransition(true, null),
            (MembershipState.Active, MembershipChange.Silence) => new MembershipTransition(false, MembershipState.Silenced),
            (MembershipState.Silenced, MembershipChange.Unsilence) => new MembershipTransition(false, MembershipState.Active),
            (MembershipState.Active, MembershipChange.Ban) => new MembershipTransition(false, MembershipState.Banned),
            (MembershipState.Banned, MembershipChange.Unban) => new MembershipTransition(false, MembershipState.Active),
            _ => null,
        };
}

/// <summary>
/// The moderation verbs of SPEC-0008 section F — the contract's own words for the §3.3 edges
/// behind <c>PATCH /api/containers/{slug}/members/{actorId}</c>.
/// </summary>
public enum MembershipChange
{
    Approve,
    Reject,
    Silence,
    Unsilence,
    Ban,
    Unban,
}

/// <summary>
/// What one §3.3 edge produces: <see cref="RemovesRow"/> for reject's <c>(removido)</c>, the
/// next state for every other verb — never both at once (data-model §3.3).
/// </summary>
public sealed record MembershipTransition(bool RemovesRow, MembershipState? State);
