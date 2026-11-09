namespace Squid.Domain.Containers;

/// <summary>
/// Succession and transfer as pure rules of SPEC-0008 section G (data-model §2.2): no I/O, no
/// session and no policy of its own — the routes evaluate the capability, these functions decide
/// what the ownership move itself produces, and every one of them ends with the container's single
/// Owner row Active: no sequence of this phase leaves the container orphaned (DoD).
/// </summary>
public static class SuccessionRules
{
    /// <summary>
    /// The claim of <c>POST /claim</c> opens only once the owner has been inactive for the whole
    /// configured window (contract §9.12: before the deadline the answer is a refusal); the
    /// deadline instant itself is already inside the window — "after N days", never "in more than N".
    /// </summary>
    public static bool ClaimAllowed(DateTimeOffset now, DateTimeOffset ownerLastActiveAt, int inactivityDays) =>
        now >= ownerLastActiveAt.AddDays(inactivityDays);

    /// <summary>
    /// The membership rows one ownership move writes: the new owner lands <c>Owner</c> +
    /// <c>Active</c> — created when the actor has no row yet — and the previous owner row moves to
    /// <c>Admin</c>, its state untouched. A move onto the owner itself writes only the target row,
    /// so the container can never end without exactly one active Owner (data-model §2.2).
    /// </summary>
    public static IReadOnlyList<ContainerMembership> Transfer(
        IReadOnlyList<ContainerMembership> members,
        string slug,
        string newOwnerHandle,
        string newOwnerActorId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(members);

        var target = members.FirstOrDefault(member => member.Handle == newOwnerHandle);
        var previous = members.FirstOrDefault(member => member.Role == MembershipRole.Owner);
        var rows = new List<ContainerMembership>(2);

        if (previous is not null && previous.Handle != newOwnerHandle)
        {
            rows.Add(previous with { Role = MembershipRole.Admin });
        }

        rows.Add(target is null
            ? new ContainerMembership
            {
                Slug = slug,
                ActorId = newOwnerActorId,
                Handle = newOwnerHandle,
                Role = MembershipRole.Owner,
                State = MembershipState.Active,
                JoinedAt = now,
            }
            : target with { Role = MembershipRole.Owner, State = MembershipState.Active });

        return rows;
    }
}
