using Dapper;
using Microsoft.Data.Sqlite;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Posts;

namespace Squid.Service;

/// <summary>
/// <see cref="ILikeStore"/> over the same SQLite <c>IGrainStorage</c> (pattern
/// <c>SqliteActorStatusStore</c>): one document per post listing who liked it — the pair is the
/// idempotency key of contract §7.14, so the same actor liking twice is one row and one count —
/// plus the one <c>UPDATE</c> that corrects the projection's <c>like_count</c>, the column the feed
/// already serves. No table, no migration: the counters stay derived, never a second source of
/// truth (data-model §2.5).
/// </summary>
public sealed class SqliteLikeStore : ILikeStore
{
    /// <summary>Store name of the like documents (one per post id).</summary>
    public const string StoreName = "likes";

    /// <summary>Re-reads before giving up when the write races another like of the same post.</summary>
    private const int Attempts = 5;

    // The projection owns its tables, and this correction never fights the projector: its INSERT is
    // ON CONFLICT DO NOTHING and it never writes like_count itself.
    private const string UpdateCount =
        "UPDATE timeline_item SET like_count = @Count WHERE post_id = @PostId";

    private readonly IGrainStorage _storage;
    private readonly string _connectionString;

    /// <summary>Binds the store to the storage and the migrated file the host already created.</summary>
    public SqliteLikeStore(IGrainStorage storage, string connectionString)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    private static GrainId Row(string postId) => GrainId.Create("likes", postId);

    /// <inheritdoc />
    public async Task<long> LikeAsync(string postId, string actorUri, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUri);

        List<string> actors = [];
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<LikeDocument>();
            await _storage.ReadStateAsync(StoreName, Row(postId), read);
            var rows = read.State?.Actors ?? [];
            if (rows.Contains(actorUri, StringComparer.Ordinal))
            {
                actors = rows;
                break; // the pair is already recorded: the count below is the proof it did not grow
            }

            actors = new List<string>(rows) { actorUri };
            try
            {
                await _storage.WriteStateAsync(
                    StoreName, Row(postId), new GrainState<LikeDocument>(new(actors), read.ETag));
                break;
            }
            catch (InconsistentStateException)
            {
                // Another like moved the document in between: re-read and ask again.
                if (attempt == Attempts) throw;
            }
        }

        // Written whether or not this like was new: a like that arrived before the pump projected
        // the post corrects itself here, and a repeat rewrites the same number.
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(UpdateCount, new { PostId = postId, Count = actors.Count });
        return actors.Count;
    }

    /// <summary>Who liked one post, in the order the likes arrived.</summary>
    private sealed record LikeDocument(List<string> Actors);
}
