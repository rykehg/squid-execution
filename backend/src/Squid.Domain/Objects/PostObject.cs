namespace Squid.Domain.Objects;

/// <summary>
/// The kind of <c>Object</c> the MVP publishes (data-model §2.5): <c>Post</c> only —
/// <c>Event</c>/<c>Question</c>/<c>Objective</c> are feature 003 (ADR-31) and arrive later.
/// </summary>
public enum ObjectKind
{
    Post,
}

/// <summary>
/// A publication inside a container — data-model §2.5: the URI of the object, its author and
/// container, the sanitized text (§2.5 <c>Content</c> é texto sanitizado, GAP-24 applied before
/// it ever reaches storage), <c>VisibilityAtPublish</c> frozen at the moment of publishing,
/// <c>PolicyVersion</c> = the version that was evaluated, and <c>Seq</c> = the container's own
/// total order (GAP-23: one sequence per container, never global). A comment chains through
/// <c>ReplyToId</c> and carries its <c>Depth</c> for the ≤ 3 rule of §2.5 — the root post sits
/// at depth 0, see <see cref="ReplyDepth"/>.
/// </summary>
public sealed record PostObject
{
    /// <summary>The content cap of §2.5 — a longer payload is a refusal, never a truncation.</summary>
    public const int MaxContentLength = 5000;

    /// <summary>Canonical URI <c>{scheme}://{host}/o/{id}</c> (SPEC-0009 section A, origin rule of P-03).</summary>
    public required string Id { get; init; }

    /// <summary>Canonical actor URI of the author (<c>{scheme}://{host}/users/{handle}</c>).</summary>
    public required string AuthorActorId { get; init; }

    /// <summary>Canonical container URI — the resource this object belongs to on the wire.</summary>
    public required string ContainerId { get; init; }

    /// <summary>The slug behind that URI: the row key of the container aggregate, and the scope of <see cref="Seq"/>.</summary>
    public required string ContainerSlug { get; init; }

    public required ObjectKind Kind { get; init; }

    /// <summary>The text as stored: HTML escaped and C0 controls removed by <see cref="PostSanitizer"/>.</summary>
    public required string Content { get; init; }

    /// <summary>The container's visibility at the moment of publishing; reading applies the current one (§4, never more permissive).</summary>
    public required VisibilityPolicy VisibilityAtPublish { get; init; }

    /// <summary>The policy version evaluated by the decision that admitted this post.</summary>
    public required int PolicyVersion { get; init; }

    /// <summary>Total order inside the container — unique and growing under concurrent publishes (AC-03).</summary>
    public required long Seq { get; init; }

    /// <summary>Canonical URI of the parent post; null for the root of a thread.</summary>
    public string? ReplyToId { get; init; }

    /// <summary>Edges from the root: 0 for a root post, 1 for a direct answer, ≤ 3 for anything publishable.</summary>
    public required int Depth { get; init; }

    /// <summary>
    /// The media attached at publishing (data-model §2.5 list ≤ 10, fields of §2.6): empty for a
    /// text-only post, filled from the <c>contentHashes</c> of the publish body (SPEC-0009 E).
    /// </summary>
    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }
}
