using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Posts;
using Squid.Domain.Objects;

namespace Squid.Service;

/// <summary>
/// <see cref="IObjectStore"/> over the same SQLite <c>IGrainStorage</c> the credential and
/// container rows use (pattern <c>SqliteContainerStore</c>): no new table and no migration — the
/// schema of TASK-0003 already stores any keyed document, and TASK-0024 grows it behind this
/// same interface. Two kinds of row: one post per <see cref="StoreName"/> document keyed by the
/// tail of its id, and one sequence per container under <see cref="SeqStoreName"/> — the
/// per-container <c>Object.Seq</c> of GAP-23 (it lives in its own document rather than inside
/// <see cref="Squid.Domain.Containers.Container"/> because the aggregate is TASK-0017's and this
/// task may not edit it; the semantics are the ones the spec asks for — one counter per
/// container, serialized by the ETag compared in <c>WHERE Version = @Version</c>, so two
/// racing publishes each take their own number and containers never interfere).
/// </summary>
public sealed class SqliteObjectStore : IObjectStore
{
    /// <summary>Store name of the post rows (one document per id).</summary>
    public const string StoreName = "objects";

    /// <summary>Store name of the per-container sequences (one document per slug).</summary>
    public const string SeqStoreName = "object-seq";

    /// <summary>Store name of the post↔media reverse index (one document per content hash, GAP-25).</summary>
    public const string MediaRefStoreName = "post-media-ref";

    /// <summary>
    /// Store name of the soft-delete tombstones (one document per removed id, SPEC-0009 section H):
    /// a second keyed document rather than new fields on the post row — <c>PostObject</c> is
    /// data-model §2.5's and belongs to <c>Squid.Domain</c>, which this task may not edit, and a
    /// row written before this task must keep reading unchanged (no rewrite of stored documents).
    /// </summary>
    public const string TombstoneStoreName = "post-tombstone";

    /// <summary>Re-reads before giving up when the sequence is raced by another publisher (AC-03).</summary>
    private const int Attempts = 5;

    private readonly IGrainStorage _storage;

    /// <summary>Binds the store to the storage the host already created (schema exists with it).</summary>
    public SqliteObjectStore(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    private static GrainId PostRow(string key) => GrainId.Create("post", key);

    private static GrainId SeqRow(string slug) => GrainId.Create("objectseq", slug);

    private static GrainId MediaRefRow(string contentHash) => GrainId.Create("postmedia", contentHash);

    private static GrainId TombstoneRow(string key) => GrainId.Create("posttomb", key);

    /// <inheritdoc />
    public async Task<long> NextSeqAsync(string containerSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerSlug);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<SeqDocument>();
            await _storage.ReadStateAsync(SeqStoreName, SeqRow(containerSlug), read);
            var document = read.State ?? new SeqDocument(0);
            var write = new GrainState<SeqDocument>(document with { LastSeq = document.LastSeq + 1 }, read.ETag);
            try
            {
                await _storage.WriteStateAsync(SeqStoreName, SeqRow(containerSlug), write);
                return write.State.LastSeq;
            }
            catch (InconsistentStateException)
            {
                // Another publish took the number in between: re-read and take the next one.
                // A failed write wrote nothing, so no number is ever handed out twice.
            }
        }

        throw new IOException(
            $"the object sequence of '{containerSlug}' kept conflicting after {Attempts} attempts");
    }

    /// <inheritdoc />
    public async Task<bool> CreateAsync(PostObject post)
    {
        ArgumentNullException.ThrowIfNull(post);

        var key = KeyOf(post.Id);
        if (key.Length == 0) return false;

        // The reverse index of GAP-25 goes in BEFORE the row: a ref whose post never lands is
        // skipped at read time (the lookup answers null), while a ref lost after the insert would
        // hide a blob from a reader who can see the post.
        foreach (var attachment in post.Attachments)
        {
            // A Link attachment carries no blob (§2.6): there is no hash to index — an empty key
            // would both pile every link post into one shared ref row and mean nothing to a
            // reader (FindByContentHashAsync refuses empty hashes anyway).
            if (string.IsNullOrWhiteSpace(attachment.ContentHash)) continue;
            await AppendRefAsync(attachment.ContentHash, post.Id);
        }

        // A null ETag only ever inserts (SqliteGrainStorage): an id that is already there reports
        // the conflict, and nothing is overwritten — the log of an object is append by id.
        var state = new GrainState<PostObject>(post, null);
        try
        {
            await _storage.WriteStateAsync(StoreName, PostRow(key), state);
            return true;
        }
        catch (InconsistentStateException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<PostObject?> FindAsync(string id)
    {
        var key = KeyOf(id ?? string.Empty);
        if (key.Length == 0) return null;

        var state = new GrainState<PostObject>();
        await _storage.ReadStateAsync(StoreName, PostRow(key), state);
        if (state.State is null) return null;

        // The tombstone is the one gate every read passes: a removed post answers null here, so
        // GET, replies, the media refs and every future lookup see the same disappearance
        // (SPEC-0009 section H), while the row itself stays for the retention of data-model §2.5.
        var tombstone = new GrainState<PostTombstone>();
        await _storage.ReadStateAsync(TombstoneStoreName, TombstoneRow(key), tombstone);
        return tombstone.State is null ? state.State : null;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, DateTimeOffset deletedAt, string? takedownReason)
    {
        var key = KeyOf(id ?? string.Empty);
        if (key.Length == 0) return false;

        var post = new GrainState<PostObject>();
        await _storage.ReadStateAsync(StoreName, PostRow(key), post);
        if (post.State is null) return false;

        // A null ETag only ever inserts: the first removal wins and a repeat reports false, so a
        // second DELETE answers the shared 404 instead of rewriting the tombstone's instant.
        var state = new GrainState<PostTombstone>(new PostTombstone(deletedAt, takedownReason), null);
        try
        {
            await _storage.WriteStateAsync(TombstoneStoreName, TombstoneRow(key), state);
            return true;
        }
        catch (InconsistentStateException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PostObject>> FindByContentHashAsync(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash)) return [];

        var state = new GrainState<MediaRefDocument>();
        await _storage.ReadStateAsync(MediaRefStoreName, MediaRefRow(contentHash.Trim().ToLowerInvariant()), state);

        var ids = state.State?.PostIds ?? [];
        var posts = new List<PostObject>(ids.Count);
        foreach (var id in ids)
        {
            var post = await FindAsync(id);
            if (post is not null) posts.Add(post);
        }

        return posts;
    }

    /// <summary>Appends one post id to a hash's ref list, re-reading when a concurrent publish raced it.</summary>
    private async Task AppendRefAsync(string contentHash, string postId)
    {
        var row = MediaRefRow(contentHash.Trim().ToLowerInvariant());
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<MediaRefDocument>();
            await _storage.ReadStateAsync(MediaRefStoreName, row, read);
            var document = read.State ?? new MediaRefDocument([]);
            if (document.PostIds.Contains(postId, StringComparer.Ordinal)) return;

            var write = new GrainState<MediaRefDocument>(
                new MediaRefDocument([.. document.PostIds, postId]), read.ETag);
            try
            {
                await _storage.WriteStateAsync(MediaRefStoreName, row, write);
                return;
            }
            catch (InconsistentStateException)
            {
                // Another publish added a reference in between: re-read and append to its list.
            }
        }

        throw new IOException(
            $"the media reference of '{contentHash}' kept conflicting after {Attempts} attempts");
    }

    /// <summary>
    /// The row key behind a post id: the tail of the canonical <c>…/o/{id}</c>, or the opaque id
    /// exactly as a client sent it — query and fragment stripped, so every spelling of the same
    /// resource lands on the same row.
    /// </summary>
    public static string KeyOf(string id)
    {
        var value = Uri.UnescapeDataString(id ?? string.Empty).Trim();
        var cut = value.IndexOfAny(['?', '#']);
        if (cut >= 0) value = value[..cut];

        value = value.TrimEnd('/');
        var slash = value.LastIndexOf('/');
        return slash >= 0 ? value[(slash + 1)..] : value;
    }

    /// <summary>The sequence row of one container: the last number handed out (0 before the first post).</summary>
    private sealed record SeqDocument(long LastSeq);

    /// <summary>The posts referencing one content hash — the reverse index GAP-25's media rule reads.</summary>
    private sealed record MediaRefDocument(IReadOnlyList<string> PostIds);

    /// <summary>The tombstone of a removed post — data-model §2.5's DeletedAt + TakedownReason (R-07).</summary>
    private sealed record PostTombstone(DateTimeOffset DeletedAt, string? TakedownReason);
}
