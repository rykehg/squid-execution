using Squid.Domain.Containers;

namespace Squid.Api.Containers;

/// <summary>
/// The ownership move of SPEC-0008 section G as the one operation the three routes that can
/// perform it share — the owner's <c>POST /transfer</c>, the successor's <c>POST /claim</c> and
/// the operator's override: <see cref="SuccessionRules.Transfer"/> writes the rows, then the
/// container row follows with the new <c>OwnerActorId</c>, the restarted inactivity clock and the
/// derived counter, so every move ends with exactly one active Owner (data-model §2.2, DoD).
/// </summary>
internal static class Ownership
{
    /// <summary>Applies the move and returns the container row as written.</summary>
    public static async Task<Container> MoveAsync(
        IContainerStore store,
        Container container,
        string newOwnerHandle,
        string newOwnerActorId,
        DateTimeOffset now)
    {
        var members = await store.ListMembershipsAsync(container.Slug);
        foreach (var row in SuccessionRules.Transfer(members, container.Slug, newOwnerHandle, newOwnerActorId, now))
        {
            await store.UpsertMembershipAsync(row);
        }

        // shortcut: the membership rows and the container row are two writes with no transaction —
        // a crash between them leaves OwnerActorId one step behind the rows; upgrade to one atomic
        // write when the storage grows a transaction (the same limit as the create shortcut).
        var active = (await store.ListMembershipsAsync(container.Slug))
            .Count(member => member.State == MembershipState.Active);
        var updated = await store.UpdateAsync(container.Slug, null, row => row with
        {
            OwnerActorId = newOwnerActorId,
            OwnerLastActiveAt = now, // the new owner's claim clock starts at the move (SPEC-0008 G)
            MemberCount = active,
        });

        return updated?.Container ?? container;
    }
}
