namespace Squid.Api.Containers;

/// <summary>
/// The <c>Result</c> domain of data-model §2.11: <c>Allow | Deny | NotFound</c>. The words travel
/// as strings on purpose — they are the contract's own spelling, and <c>Results.Json</c> would
/// otherwise emit the enum's ordinals.
/// </summary>
public static class PolicyAudit
{
    /// <summary>The access was granted (FR-019 records grants as well as refusals).</summary>
    public const string Allow = "Allow";

    /// <summary>The policy refused an action the actor was authenticated for.</summary>
    public const string Deny = "Deny";

    /// <summary>The resource is absent or protected — FR-018's answer, recorded as such.</summary>
    public const string NotFound = "NotFound";

    /// <summary>A visibility decision executed by a read route (SPEC-0008 section C).</summary>
    public const string View = "View";

    /// <summary>A decision over <c>PUT /api/containers/{slug}/policy</c> (SPEC-0008 section B).</summary>
    public const string ChangePolicy = "ChangePolicy";

    /// <summary>A decision over <c>POST /api/containers/{slug}/join</c> (SPEC-0008 section E).</summary>
    public const string Join = "Join";

    /// <summary>A decision over <c>POST /api/containers/{slug}/invites</c> (SPEC-0008 section E).</summary>
    public const string Invite = "Invite";

    /// <summary>
    /// A decision over <c>PATCH /api/containers/{slug}/members/{actorId}</c> (SPEC-0008 section F,
    /// FR-013): the one word the data-model §2.11 enum gives to papéis and moderation — role and
    /// state changes alike travel as it, the enum has no second word for them.
    /// </summary>
    public const string ChangeRole = "ChangeRole";

    /// <summary>
    /// A decision over the succession of section G — <c>PUT /successor</c> and <c>POST /claim</c>
    /// (SPEC-0008 G): the data-model §2.11 enum has no word for it, exactly as it had none for
    /// <see cref="ChangePolicy"/>, and a trail that cannot tell succession from a role edit is
    /// evidence nobody can read (FR-019, SC-011).
    /// </summary>
    public const string Successor = "Successor";

    /// <summary>A decision over an ownership transfer — the owner's <c>POST /transfer</c> or the operator's override (SPEC-0008 G).</summary>
    public const string Transfer = "Transfer";

    /// <summary>A decision over <c>DELETE /api/containers/{slug}</c> (SPEC-0008 G).</summary>
    public const string Delete = "Delete";

    /// <summary>Actor id of a request that carries no session (data-model §2.11 keeps a URI).</summary>
    public const string AnonymousActor = "urn:squid:anonymous";
}

/// <summary>
/// One append-only row of the audit trail, exactly the fields of data-model §2.11 and of
/// SPEC-0008 section C: when, who, which action, on which resource, the outcome, the policy
/// version the decision was taken under and why. Ordering is the store's business (insertion
/// order is the stable tiebreak the task's gaps table asks for), so the row carries no id.
/// </summary>
public sealed record PolicyAuditEntry(
    DateTimeOffset At,
    string ActorId,
    string Action,
    string ResourceId,
    string Result,
    int PolicyVersion,
    string Reason);

/// <summary>
/// The query <c>GET /api/admin/audit?actor=&amp;action=&amp;from=</c> answers (contract §7) plus the
/// pagination SPEC-0008 section C requires: newest first, stable under equal timestamps.
/// </summary>
public sealed record PolicyAuditFilter(
    string? Actor = null,
    string? Action = null,
    DateTimeOffset? From = null,
    int Limit = 100,
    int Offset = 0);

/// <summary>
/// The trail of SPEC-0008 section C (FR-019, SC-011): append-only, because a decision that can be
/// rewritten is not evidence. The edge declares it (ADR-35); <c>Squid.Service</c> implements it
/// over SQLite like every other store of this slice.
/// </summary>
public interface IPolicyAuditStore
{
    /// <summary>Adds one row at the end of the trail; there is no update and no delete.</summary>
    Task AppendAsync(PolicyAuditEntry entry);

    /// <summary>
    /// The matching rows ordered by <c>At</c> descending with insertion order as the stable
    /// tiebreak, then paged — never reordered between calls, so a page cannot skip or repeat.
    /// </summary>
    Task<IReadOnlyList<PolicyAuditEntry>> QueryAsync(PolicyAuditFilter filter);
}
