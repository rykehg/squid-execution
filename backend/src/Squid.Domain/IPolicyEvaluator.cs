using Squid.Domain.Containers;

namespace Squid.Domain;

/// <summary>The action under evaluation — SPEC-0003 inputs (FR-011, FR-012).</summary>
public enum PolicyAction
{
    Post,
    Comment,
    React,
    Share,
    Attach,
    Join,
    Moderate,
    Admin,
}

/// <summary>
/// Role inside a container, declared in ascending capability so a plain comparison reads the
/// strict hierarchy <c>Owner ⊃ Admin ⊃ Moderator ⊃ Member ⊃ Guest</c> (FR-012, data-model §3.2).
/// </summary>
public enum ContainerRole
{
    Guest,
    Member,
    Moderator,
    Admin,
    Owner,
}

/// <summary>
/// Outcome of one evaluation: permission, the reason behind it and the policy version it was
/// decided against — no decision is silent (AC-03; the version prepares the FR-019 audit trail).
/// </summary>
public sealed record PolicyDecision(bool Permitted, string Reason, int PolicyVersion);

/// <summary>
/// The actor behind one evaluation — the context GAP-11 puts in front of the policy (SPEC-0008
/// section F): the role the ladder grants (§3.2), the state the §3.3 machine currently allows,
/// and whether the policy itself names this actor as a publisher. A null state is an actor with
/// no membership row at all; <c>IsPublisher</c> is only ever true for an active member, so the
/// three answer together what a role alone never could.
/// </summary>
public sealed record PolicyActor(ContainerRole Role, MembershipState? State, bool IsPublisher);

/// <summary>The single path for every role check (data-model §3.2 hard rule).</summary>
public interface IPolicyEvaluator
{
    /// <summary>
    /// Evaluate <paramref name="action"/> for <paramref name="role"/> against
    /// <paramref name="policy"/>. Expected decisions never throw; invalid input does.
    /// This is the role-only contract of SPEC-0003: it answers as an active member claiming no
    /// publisher seat, so <c>SinglePublisher</c> keeps failing closed (the TASK-0007 matrix).
    /// </summary>
    PolicyDecision Evaluate(PolicyAction action, ContainerRole role, ContainerPolicy policy);

    /// <summary>
    /// Evaluate <paramref name="action"/> for <paramref name="actor"/> — role plus membership
    /// state plus publisher membership (GAP-11, SPEC-0008 section F). Every write needs the one
    /// state §3.3 lets write: <c>Silenced</c> reads but never writes, <c>Banned</c> answers
    /// nothing (FR-018), no row at all is no membership, and <c>SinglePublisher</c> is confirmed
    /// only for the named active publisher — the facet that blocks a plain member and admits the
    /// publisher. Expected decisions never throw; invalid input does.
    /// </summary>
    PolicyDecision Evaluate(PolicyAction action, PolicyActor actor, ContainerPolicy policy);
}

/// <summary>In-memory evaluator: pure, O(1) per decision, no I/O (SPEC-0003 non-functional).</summary>
public sealed class PolicyEvaluator : IPolicyEvaluator
{
    /// <summary>
    /// The role-only path is the pre-GAP-11 contract kept intact (SPEC-0003, TASK-0007 matrix):
    /// a bare role reads as an active member who claims no publisher seat, which is exactly why
    /// <c>SinglePublisher</c> still fails closed when only the role is known.
    /// </summary>
    public PolicyDecision Evaluate(PolicyAction action, ContainerRole role, ContainerPolicy policy) =>
        Evaluate(action, new PolicyActor(role, MembershipState.Active, false), policy);

    public PolicyDecision Evaluate(PolicyAction action, PolicyActor actor, ContainerPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(policy);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "unknown policy action");
        }

        if (!Enum.IsDefined(actor.Role))
        {
            throw new ArgumentOutOfRangeException(nameof(actor), actor.Role, "unknown container role");
        }

        if (actor.State is { } stateValue && !Enum.IsDefined(stateValue))
        {
            throw new ArgumentOutOfRangeException(nameof(actor), stateValue, "unknown membership state");
        }

        if (action == PolicyAction.Join)
        {
            // Entry stays the policy's own verdict: the two shut states of §3.3 never rejoin and
            // everything else is judged by Join alone (an invitation in force is MembershipRules'
            // ticket, not something this context can see — fail closed there, never open).
            if (actor.State is MembershipState.Banned)
            {
                return Deny(policy, "a Banned membership never rejoins (§3.3)");
            }

            if (actor.State is MembershipState.Silenced)
            {
                return Deny(policy, "a Silenced membership never rejoins (§3.3)");
            }

            return Entry(policy);
        }

        if (actor.State != MembershipState.Active)
        {
            // Deny by default (SPEC-0008 non-functional): a write needs the one state §3.3
            // grants it — Silenced reads but never writes, Banned answers nothing at all
            // (FR-018), and no row or a pending one is no active member (data-model §2.4).
            return Deny(policy, actor.State switch
            {
                MembershipState.Silenced => $"{action} is denied: a Silenced member reads but never writes (§3.3)",
                MembershipState.Banned => $"{action} is denied: a Banned member neither reads nor writes (§3.3, FR-018)",
                null => $"{action} is denied: no membership row, and every write needs an active member (data-model §2.4)",
                var state => $"{action} is denied: membership state {state} is not active (§3.3)",
            });
        }

        return action switch
        {
            PolicyAction.Post => Content(action, actor, policy.Posting, policy),
            PolicyAction.Comment => Content(action, actor, policy.Interaction.CanComment, policy),
            PolicyAction.React => Content(action, actor, policy.Interaction.CanReact, policy),
            PolicyAction.Share => Content(action, actor, policy.Interaction.CanShare, policy),
            PolicyAction.Attach => Content(action, actor, policy.Interaction.CanAttach, policy),
            PolicyAction.Join => Entry(policy),
            PolicyAction.Moderate => Capability(action, actor.Role, ContainerRole.Moderator, policy),
            PolicyAction.Admin => Capability(action, actor.Role, ContainerRole.Admin, policy),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "unknown policy action"),
        };
    }

    /// <summary>
    /// The policy never grants what the role does not hold: Guest is read-only whatever the policy
    /// says (data-model §2.4), and <c>SinglePublisher</c> is the one gate the actor context can
    /// confirm — the policy's own named publisher (FR-015, GAP-11), never the role alone.
    /// </summary>
    private static PolicyDecision Content(PolicyAction action, PolicyActor actor, PostingPolicy gate, ContainerPolicy policy)
    {
        if (actor.Role == ContainerRole.Guest)
        {
            return Deny(policy, $"Guest is read-only: {action} is never granted by policy");
        }

        if (gate == PostingPolicy.SinglePublisher)
        {
            return actor.IsPublisher
                ? Allow(policy, $"{action} under SinglePublisher is granted to the publisher the policy names")
                : Deny(policy, $"{action} under SinglePublisher needs this actor to be the named publisher (FR-015)");
        }

        var required = gate switch
        {
            PostingPolicy.Everyone or PostingPolicy.Members => ContainerRole.Member,
            PostingPolicy.Moderators => ContainerRole.Moderator,
            PostingPolicy.Admins => ContainerRole.Admin,
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "posting gate outside its domain"),
        };

        return actor.Role >= required
            ? Allow(policy, $"{actor.Role} holds the {gate} gate for {action}")
            : Deny(policy, $"{gate} gate for {action} requires {required} or above, got {actor.Role}");
    }

    /// <summary>
    /// Entry is decided by policy alone (FR-014): a joining outsider arrives as Guest, so Open and
    /// Request let them through. Invite needs an invitation the context cannot carry (GAP-13) —
    /// fail closed; <see cref="MembershipRules"/> admits the invited row on top of this verdict.
    /// </summary>
    private static PolicyDecision Entry(ContainerPolicy policy) => policy.Join switch
    {
        JoinPolicy.Open => Allow(policy, "Join policy Open admits entry"),
        JoinPolicy.Request => Allow(policy, "Join policy Request admits a request for entry"),
        JoinPolicy.Invite => Deny(policy, "Join policy Invite needs an invitation the context cannot confirm"),
        JoinPolicy.Closed => Deny(policy, "Join policy Closed admits nobody"),
        _ => throw new ArgumentOutOfRangeException(nameof(policy.Join), policy.Join, "join policy outside its domain"),
    };

    /// <summary>Moderation and administration are role capabilities; no policy gates them (data-model §3.2).</summary>
    private static PolicyDecision Capability(PolicyAction action, ContainerRole role, ContainerRole required, ContainerPolicy policy) =>
        role >= required
            ? Allow(policy, $"{role} holds the {action} capability")
            : Deny(policy, $"{action} requires {required} or above, got {role}");

    private static PolicyDecision Allow(ContainerPolicy policy, string reason) => new(true, reason, policy.Version);

    private static PolicyDecision Deny(ContainerPolicy policy, string reason) => new(false, reason, policy.Version);
}
