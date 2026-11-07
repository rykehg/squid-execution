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

/// <summary>The single path for every role check (data-model §3.2 hard rule).</summary>
public interface IPolicyEvaluator
{
    /// <summary>
    /// Evaluate <paramref name="action"/> for <paramref name="role"/> against
    /// <paramref name="policy"/>. Expected decisions never throw; invalid input does.
    /// </summary>
    PolicyDecision Evaluate(PolicyAction action, ContainerRole role, ContainerPolicy policy);
}

/// <summary>In-memory evaluator: pure, O(1) per decision, no I/O (SPEC-0003 non-functional).</summary>
public sealed class PolicyEvaluator : IPolicyEvaluator
{
    public PolicyDecision Evaluate(PolicyAction action, ContainerRole role, ContainerPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "unknown policy action");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "unknown container role");
        }

        return action switch
        {
            PolicyAction.Post => Content(action, role, policy.Posting, policy),
            PolicyAction.Comment => Content(action, role, policy.Interaction.CanComment, policy),
            PolicyAction.React => Content(action, role, policy.Interaction.CanReact, policy),
            PolicyAction.Share => Content(action, role, policy.Interaction.CanShare, policy),
            PolicyAction.Attach => Content(action, role, policy.Interaction.CanAttach, policy),
            PolicyAction.Join => Entry(policy),
            PolicyAction.Moderate => Capability(action, role, ContainerRole.Moderator, policy),
            PolicyAction.Admin => Capability(action, role, ContainerRole.Admin, policy),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "unknown policy action"),
        };
    }

    /// <summary>
    /// The policy never grants what the role does not hold: Guest is read-only whatever the policy
    /// says (data-model §2.4), and SinglePublisher cannot be confirmed from a role alone (FR-015;
    /// the Actor-aware path arrives with the grain in P-03, GAP-11), so it fails closed.
    /// </summary>
    private static PolicyDecision Content(PolicyAction action, ContainerRole role, PostingPolicy gate, ContainerPolicy policy)
    {
        if (role == ContainerRole.Guest)
        {
            return Deny(policy, $"Guest is read-only: {action} is never granted by policy");
        }

        if (gate == PostingPolicy.SinglePublisher)
        {
            return Deny(policy, $"{action} under SinglePublisher needs a named publisher; the role alone cannot confirm one (FR-015)");
        }

        var required = gate switch
        {
            PostingPolicy.Everyone or PostingPolicy.Members => ContainerRole.Member,
            PostingPolicy.Moderators => ContainerRole.Moderator,
            PostingPolicy.Admins => ContainerRole.Admin,
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "posting gate outside its domain"),
        };

        return role >= required
            ? Allow(policy, $"{role} holds the {gate} gate for {action}")
            : Deny(policy, $"{gate} gate for {action} requires {required} or above, got {role}");
    }

    /// <summary>
    /// Entry is decided by policy alone (FR-014): a joining outsider arrives as Guest, so Open and
    /// Request let them through. Invite needs membership state, which is P-03 (GAP-11) — fail closed.
    /// </summary>
    private static PolicyDecision Entry(ContainerPolicy policy) => policy.Join switch
    {
        JoinPolicy.Open => Allow(policy, "Join policy Open admits entry"),
        JoinPolicy.Request => Allow(policy, "Join policy Request admits a request for entry"),
        JoinPolicy.Invite => Deny(policy, "Join policy Invite needs an invitation the role alone cannot confirm"),
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
