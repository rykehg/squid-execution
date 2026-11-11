using Squid.Domain.Objects;

namespace Squid.Api.Posts;

/// <summary>
/// The object store of SPEC-0009 section A (ADR-35: the edge declares it, <c>Squid.Service</c>
/// implements it over SQLite, pattern <c>SqliteContainerStore</c>): the post rows themselves plus
/// the per-container sequence of data-model §2.5. <see cref="NextSeqAsync"/> is the ETag-guarded
/// increment GAP-23 asks for — two publishes racing in the same container each take their own
/// number, and one container never touches another's counter (AC-03). No new table: documents
/// keyed in the TASK-0003 storage; the migration of TASK-0024 grows the schema behind these
/// methods without a caller changing.
/// </summary>
public interface IObjectStore
{
    /// <summary>
    /// The next <c>Object.Seq</c> of <paramref name="containerSlug"/>: unique and growing under
    /// concurrency, the write serialized by the stored ETag (<c>WHERE Version = @Version</c>).
    /// </summary>
    Task<long> NextSeqAsync(string containerSlug);

    /// <summary>Inserts the post under its id; false when that id already exists (insert only, never a rewrite).</summary>
    Task<bool> CreateAsync(PostObject post);

    /// <summary>
    /// The post of <paramref name="id"/>, or null: the id arrives as the opaque tail of
    /// <c>/o/{id}</c> or as the whole canonical URI, and the store reads whichever it is.
    /// A tombstoned post answers null here — the soft delete of SPEC-0009 section H hides it
    /// from every reader and every route through this one gate (data-model §2.5), while the row
    /// and the trail stay where they are.
    /// </summary>
    Task<PostObject?> FindAsync(string id);

    /// <summary>
    /// Soft-deletes the post of <paramref name="id"/> (data-model §2.5): the row stays and gains
    /// its tombstone — <c>DeletedAt</c> plus the <c>TakedownReason</c> a Moderator+ removal owes
    /// (R-07, null for an author's own removal). False when the id has no row or is already
    /// tombstoned, so the caller answers the shared 404 and never reports a write that did not land.
    /// </summary>
    Task<bool> DeleteAsync(string id, DateTimeOffset deletedAt, string? takedownReason);

    /// <summary>
    /// Every post whose <c>Attachments</c> carry <paramref name="contentHash"/>, maintained as a
    /// reverse index when a post is created — the media GET route reads it for the GAP-25 rule
    /// ("sees at least one post that references the hash"), each candidate going through the
    /// ordinary post visibility ladder afterwards. A ref whose post never landed simply is not
    /// returned, so the index may only ever be a superset.
    /// </summary>
    Task<IReadOnlyList<PostObject>> FindByContentHashAsync(string contentHash);
}
