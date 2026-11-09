namespace Squid.Domain.Containers;

/// <summary>Role ladder of data-model §2.4 — strict inheritance, checked by the evaluator (§3.2).</summary>
public enum MembershipRole
{
    Owner,
    Admin,
    Moderator,
    Member,
    Guest,
}

/// <summary>State machine of data-model §3.3. This task only ever writes <c>Active</c>.</summary>
public enum MembershipState
{
    Invited,
    Requested,
    Active,
    Silenced,
    Banned,
    Left,
}

/// <summary>
/// One actor's link to one container — data-model §2.4, keyed by the pair (slug, handle) at the
/// storage edge. It lives in its own row, never inside <see cref="Container"/>: data-model §2.2
/// forbids a member list in the aggregate. SPEC-0008 E grows the machine; this task only births
/// the owner (<c>Owner</c> + <c>Active</c>).
/// </summary>
public sealed record ContainerMembership
{
    /// <summary>The container this membership belongs to (the composite key's first half).</summary>
    public required string Slug { get; init; }

    /// <summary>Canonical actor URI of the member (the composite key's second half).</summary>
    public required string ActorId { get; init; }

    /// <summary>Local handle, carried so a reader can name the member without resolving the URI.</summary>
    public required string Handle { get; init; }

    public required MembershipRole Role { get; init; }

    public required MembershipState State { get; init; }

    /// <summary>
    /// Canonical actor id of the inviter when the state is <c>Invited</c> (data-model §2.4,
    /// GAP-13: the invitation belongs to this target actor, no shared code, no expiry); null for
    /// every membership that was not born from an invitation.
    /// </summary>
    public string? InvitedBy { get; init; }

    public required DateTimeOffset JoinedAt { get; init; }
}
