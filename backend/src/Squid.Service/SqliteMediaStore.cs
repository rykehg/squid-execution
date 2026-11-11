using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Media;

namespace Squid.Service;

/// <summary>
/// <see cref="IMediaStore"/> over the same SQLite <c>IGrainStorage</c> the other stores use
/// (pattern <c>SqliteObjectStore</c>) plus the blob volume of data-model §5: the record is one
/// keyed document per hash, the bytes are one file per hash under <c>Media:BlobRoot</c> —
/// content-addressed, so an identical re-upload lands on the same file and stays a no-op. The
/// store accepts only 64-hex keys, which is what keeps a route value from ever naming a path
/// outside the blob root (trust boundary of <c>GET /api/media/{contentHash}</c>).
/// </summary>
public sealed class SqliteMediaStore : IMediaStore
{
    /// <summary>Store name of the upload records (one document per content hash).</summary>
    public const string StoreName = "media";

    private readonly IGrainStorage _storage;

    private readonly string _blobRoot;

    /// <summary>Binds the store to the storage the host already created and the configured blob volume.</summary>
    public SqliteMediaStore(IGrainStorage storage, string blobRoot)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _blobRoot = blobRoot ?? throw new ArgumentNullException(nameof(blobRoot));
    }

    private static GrainId Row(string contentHash) => GrainId.Create("media", contentHash);

    /// <inheritdoc />
    public async Task<MediaRecord?> FindAsync(string contentHash)
    {
        var key = Normalize(contentHash);
        if (key is null) return null;

        var state = new GrainState<MediaRecord>();
        await _storage.ReadStateAsync(StoreName, Row(key), state);
        return state.State;
    }

    /// <inheritdoc />
    public async Task<byte[]?> ReadAsync(string contentHash)
    {
        var key = Normalize(contentHash);
        if (key is null) return null;

        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path);
    }

    /// <inheritdoc />
    public async Task SaveAsync(byte[] bytes, MediaRecord record)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(record);

        var key = Normalize(record.ContentHash)
            ?? throw new ArgumentException("contentHash must be 64 hex characters (SHA-256)", nameof(record));

        Directory.CreateDirectory(_blobRoot);
        try
        {
            // CreateNew: the same hash is the same bytes, so a racing identical upload lands on
            // one blob and the loser simply keeps the file that is already there.
            await using (var stream = new FileStream(
                PathFor(key), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.WriteAsync(bytes);
            }
        }
        catch (IOException) when (File.Exists(PathFor(key)))
        {
            // Already stored — content-addressed, so the bytes are the very ones we would write.
        }

        var state = new GrainState<MediaRecord>(record with { ContentHash = key }, null);
        try
        {
            await _storage.WriteStateAsync(StoreName, Row(key), state);
        }
        catch (InconsistentStateException)
        {
            // The record exists: this exact upload already happened.
            // shortcut: the FIRST upload's record wins — a later upload of the same bytes with a
            // different alt keeps the stored alt/uploader (the model has one Attachment per hash);
            // when to upgrade: if a per-uploader alt is ever specified, key the record per actor.
        }
    }

    /// <inheritdoc />
    public async Task<byte[]?> ReadThumbnailAsync(string contentHash)
    {
        var key = Normalize(contentHash);
        if (key is null) return null;

        var path = ThumbnailPathFor(key);
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path);
    }

    /// <summary>The blob file of a hash: one flat name under the volume root, or null if it is not a hash.</summary>
    private string PathFor(string contentHash) => Path.Combine(_blobRoot, contentHash);

    /// <summary>
    /// The registered thumbnail of a hash: the same key inside <see cref="IMediaStore.ThumbnailFolder"/>
    /// (SPEC-0009 section J) — the cross-process convention Squid.Worker writes with, so the two
    /// roots never spell the layout differently.
    /// </summary>
    private string ThumbnailPathFor(string contentHash) =>
        Path.Combine(_blobRoot, IMediaStore.ThumbnailFolder, contentHash);

    /// <summary>64 hex characters, lowercased — the one spelling the row and the file answer to.</summary>
    private static string? Normalize(string? contentHash)
    {
        var value = contentHash?.Trim();
        if (value is not { Length: 64 }) return null;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character)) return null;
        }

        return value.ToLowerInvariant();
    }
}
