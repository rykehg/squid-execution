namespace Squid.Domain.Containers;

/// <summary>The two presets of data-model §2.2 — the kind only chooses the initial values.</summary>
public enum ContainerKind
{
    Group,
    Community,
}

/// <summary>
/// The social aggregate of SPEC-0008 section A: plain state guarded by optimistic concurrency,
/// never event-sourced (human decision of the P-03 opening, 2026-11-08). It carries no member
/// list — data-model §2.2 keeps members outside the aggregate — and it never stores the feed.
/// </summary>
public sealed record Container
{
    /// <summary>data-model §2.2: name is at most 120 characters.</summary>
    public const int MaxNameLength = 120;

    /// <summary>data-model §2.2: summary is at most 1000 characters.</summary>
    public const int MaxSummaryLength = 1000;

    /// <summary>
    /// Canonical id: <c>{scheme}://{host}/c/{slug}</c> with the origin taken from the request
    /// that created it (data-model §2.2, SPEC-0008 A — the scheme + Host rule of the discovery).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Unique among the instance's containers and immutable after creation.</summary>
    public required string Slug { get; init; }

    public required ContainerKind Kind { get; init; }

    public required string Name { get; init; }

    public required string Summary { get; init; }

    /// <summary>The actor that owns this container; exactly one in the MVP (data-model §2.2).</summary>
    public required string OwnerActorId { get; init; }

    /// <summary>
    /// The actor the Owner named as successor (data-model §2.2, SPEC-0008 G): null until one is
    /// named; the claim only opens after the configured inactivity of the current owner.
    /// </summary>
    public string? SuccessorActorId { get; init; }

    /// <summary>
    /// The claim's inactivity clock (SPEC-0008 G, task gap: an "inactivity" timestamp created
    /// here): when the current owner last wrote the aggregate — born with the container (a row
    /// that predates the field falls back to <see cref="CreatedAt"/>), restarted by the owner's
    /// own writes and by every ownership move, never by another member's.
    /// </summary>
    public DateTimeOffset? OwnerLastActiveAt { get; init; }

    /// <summary>Soft delete of SPEC-0008 G: set at the deletion, null while the container lives (data-model §2.2).</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>The policy in force, born at version 1 by the preset of <see cref="Kind"/>.</summary>
    public required ContainerPolicy Policy { get; init; }

    /// <summary>Discovery flag: group preset false, community preset true (data-model §2.2).</summary>
    public required bool Listed { get; init; }

    /// <summary>Derived counter maintained on write, never recalculated on read (data-model §2.2).</summary>
    public required long MemberCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Builds a new container with the preset of its kind (SPEC-0008 A): group is
    /// <c>MembersOnly</c> + <c>Invite</c> + unlisted, community is <c>Public</c> + <c>Open</c> +
    /// listed, both post by <c>Members</c> with every interaction gate on <c>Members</c> and start
    /// at policy version 1. The creator owns it and the count starts at them (FR-010).
    /// </summary>
    public static Container Create(
        string slug,
        ContainerKind kind,
        string name,
        string summary,
        string ownerActorId,
        string origin,
        DateTimeOffset now)
    {
        var policy = kind switch
        {
            ContainerKind.Group => ContainerPolicy.Group(),
            ContainerKind.Community => ContainerPolicy.Community(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "kind outside its domain"),
        };

        return new Container
        {
            Id = $"{origin}/c/{slug}",
            Slug = slug,
            Kind = kind,
            Name = name,
            Summary = summary,
            OwnerActorId = ownerActorId,
            OwnerLastActiveAt = now, // the claim's clock starts with the owner that was born here (SPEC-0008 G)
            Policy = policy,
            Listed = kind == ContainerKind.Community,
            MemberCount = 1, // the owner, born with the container — no orphan ever (data-model §2.2)
            CreatedAt = now,
        };
    }

    /// <summary>The claim's clock: the owner's last activity, or the creation of a row without one (SPEC-0008 G).</summary>
    public DateTimeOffset OwnerActivityAt => OwnerLastActiveAt ?? CreatedAt;

    /// <summary>True once the soft delete happened — the row stays for the whole grace window (SPEC-0008 G).</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>The search rule of SPEC-0008 H: only listed containers that were never deleted leave the search.</summary>
    public bool IsSearchable => Listed && DeletedAt is null;

    /// <summary>
    /// The instant the soft-deleted state stops being recoverable: <c>DeletedAt + grace days</c>
    /// (SPEC-0008 G, GAP-17 default 30); null while the container is alive.
    /// </summary>
    public DateTimeOffset? RecoverableUntil(int graceDays) =>
        DeletedAt is null ? null : DeletedAt.Value.AddDays(graceDays);
}
