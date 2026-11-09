using Squid.Domain.Containers;

namespace Squid.Api.Containers;

/// <summary>
/// A container row plus the store's own state version (the internal ETag of SPEC-0008's gaps
/// table): a distinct field from <c>ContainerPolicy.Version</c> — PATCH moves this one,
/// <c>PUT /policy</c> moves that one.
/// </summary>
public sealed record ContainerState(Container Container, int Version);

/// <summary>
/// The container store of SPEC-0008 section A (ADR-35: the edge declares it, <c>Squid.Service</c>
/// implements it over SQLite). Every write carries the version it expects and a divergent
/// version is refused, never overwritten — the optimistic-concurrency rule TASK-0003 proved.
/// </summary>
public interface IContainerStore
{
    /// <summary>The container of <paramref name="slug"/>, or null when there is none.</summary>
    Task<ContainerState?> FindAsync(string slug);

    /// <summary>
    /// Inserts a new container at state version 1; null means the slug is already taken and
    /// nothing was written (GAP-16: the catalog has no code of its own for that refusal).
    /// </summary>
    Task<ContainerState?> CreateAsync(Container container);

    /// <summary>
    /// Applies <paramref name="mutate"/> under the version guard: a null
    /// <paramref name="expectedVersion"/> writes unconditionally, a divergent one returns null
    /// with the row untouched. Null also means the row vanished under the caller's feet, which
    /// no route of this task can produce.
    /// </summary>
    Task<ContainerState?> UpdateAsync(
        string slug, int? expectedVersion, Func<Container, Container> mutate);

    /// <summary>The membership of <paramref name="handle"/> in <paramref name="slug"/>, or null.</summary>
    Task<ContainerMembership?> FindMembershipAsync(string slug, string handle);

    /// <summary>Appends one membership to the container's row (read-modify-write under the ETag).</summary>
    Task CreateMembershipAsync(ContainerMembership membership);

    /// <summary>
    /// Writes <paramref name="membership"/> as the row of its handle: appended when the container
    /// has none, replacing the row of that same handle otherwise — one link per (container,
    /// actor) pair (data-model §2.4), under the same ETag retry rule as the append above.
    /// Returns the row as written.
    /// </summary>
    Task<ContainerMembership> UpsertMembershipAsync(ContainerMembership membership);

    /// <summary>
    /// Removes the row of <paramref name="handle"/> — the §3.3 <c>(removido)</c> behind reject
    /// (SPEC-0008 section F). Returns true when a row was there to remove.
    /// </summary>
    Task<bool> DeleteMembershipAsync(string slug, string handle);

    /// <summary>Every membership of <paramref name="slug"/>, in insertion order.</summary>
    Task<IReadOnlyList<ContainerMembership>> ListMembershipsAsync(string slug);

    /// <summary>
    /// The discovery query of SPEC-0008 section H (FR-016, GAP-15): the containers whose
    /// <c>IsSearchable</c> rule holds (listed and never deleted) and whose name or slug holds
    /// <paramref name="query"/> as a case-insensitive substring — no FTS, no ranking — ordered
    /// by slug so one walk of <paramref name="offset"/>/<paramref name="limit"/> neither repeats
    /// nor loses a row.
    /// </summary>
    Task<IReadOnlyList<Container>> SearchAsync(string query, int limit, int offset);
}
