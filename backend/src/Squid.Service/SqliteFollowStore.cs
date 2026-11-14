using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Federation;

namespace Squid.Service;

/// <summary>
/// <see cref="IFollowStore"/> over the same SQLite <c>IGrainStorage</c> (pattern
/// <c>SqliteActorStatusStore</c>): two documents per local handle — the actors following it and
/// the actors it follows — with no table and no migration inside this task, exactly what the
/// predecessor task's closed decision pinned, so the <c>follow</c> table of TASK-0046's
/// <c>0008_federation.sql</c> stays free for the peer slice and this interface is the one
/// TASK-0045 adopts for its outbound Follow.
/// </summary>
public sealed class SqliteFollowStore : IFollowStore
{
    /// <summary>Store name of the follower documents (one per local handle).</summary>
    public const string FollowersStoreName = "followers";

    /// <summary>Store name of the following documents (one per local handle).</summary>
    public const string FollowingStoreName = "following";

    /// <summary>Re-reads before giving up when the write races another delivery.</summary>
    private const int Attempts = 5;

    private readonly IGrainStorage _storage;

    /// <summary>Binds the store to the storage the host already created (schema exists with it).</summary>
    public SqliteFollowStore(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    private static GrainId FollowersRow(string handle) => GrainId.Create("followers", handle);

    private static GrainId FollowingRow(string handle) => GrainId.Create("following", handle);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FollowEdge>> FollowersAsync(string handle) =>
        await ReadAsync(FollowersStoreName, FollowersRow(handle), establishedOnly: false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FollowEdge>> FollowingAsync(string handle) =>
        await ReadAsync(FollowingStoreName, FollowingRow(handle), establishedOnly: true);

    /// <inheritdoc />
    public Task AddFollowerAsync(string handle, string actorUri, DateTimeOffset now) =>
        AddAsync(FollowersStoreName, FollowersRow(handle), actorUri, established: true, now);

    /// <inheritdoc />
    public Task AddFollowingAsync(string handle, string actorUri, DateTimeOffset now) =>
        AddAsync(FollowingStoreName, FollowingRow(handle), actorUri, established: false, now);

    /// <inheritdoc />
    public Task<bool> RemoveFollowerAsync(string handle, string actorUri) =>
        RemoveAsync(FollowersStoreName, FollowersRow(handle), actorUri);

    /// <inheritdoc />
    public Task<bool> RemoveFollowingAsync(string handle, string actorUri) =>
        RemoveAsync(FollowingStoreName, FollowingRow(handle), actorUri);

    /// <inheritdoc />
    public async Task<bool> EstablishAsync(string handle, string actorUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUri);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<FollowDocument>();
            await _storage.ReadStateAsync(FollowingStoreName, FollowingRow(handle), read);
            var rows = read.State?.Rows;
            var existing = rows?.Find(row => string.Equals(row.ActorUri, actorUri, StringComparison.Ordinal));
            if (existing is null) return false; // no follow was ever sent: nothing to settle, never an invented row

            if (existing.Established) return true; // a repeat of the Accept changes nothing

            var next = rows!.ConvertAll(row => row.ActorUri == actorUri
                ? row with { Established = true }
                : row);
            if (await TryWriteAsync(FollowingStoreName, FollowingRow(handle), new(next), read.ETag))
            {
                return true;
            }
        }

        throw new IOException($"the following document of '{handle}' kept conflicting after {Attempts} attempts");
    }

    /// <summary>The edges of one document, pending ones filtered out when the caller federates them.</summary>
    private async Task<IReadOnlyList<FollowEdge>> ReadAsync(
        string storeName, GrainId row, bool establishedOnly)
    {
        var read = new GrainState<FollowDocument>();
        await _storage.ReadStateAsync(storeName, row, read);
        var rows = read.State?.Rows ?? [];
        return rows
            .Where(entry => !establishedOnly || entry.Established)
            .Select(entry => new FollowEdge(entry.ActorUri, entry.Established, entry.CreatedAt))
            .ToList();
    }

    /// <summary>Appends one edge unless the pair is already there — the idempotency both directions owe.</summary>
    private async Task AddAsync(string storeName, GrainId row, string actorUri, bool established, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUri);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<FollowDocument>();
            await _storage.ReadStateAsync(storeName, row, read);
            var rows = read.State?.Rows ?? [];
            if (rows.Exists(entry => string.Equals(entry.ActorUri, actorUri, StringComparison.Ordinal)))
            {
                return; // one row per pair, however often the activity arrives
            }

            var next = new List<FollowRow>(rows) { new(actorUri, established, now) };
            if (await TryWriteAsync(storeName, row, new(next), read.ETag))
            {
                return;
            }
        }

        throw new IOException($"the follow document '{row}' kept conflicting after {Attempts} attempts");
    }

    /// <summary>Drops one edge; false when the document never held it.</summary>
    private async Task<bool> RemoveAsync(string storeName, GrainId row, string actorUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUri);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<FollowDocument>();
            await _storage.ReadStateAsync(storeName, row, read);
            var rows = read.State?.Rows;
            var remaining = rows?
                .Where(entry => !string.Equals(entry.ActorUri, actorUri, StringComparison.Ordinal))
                .ToList();
            if (remaining is null || remaining.Count == rows!.Count)
            {
                return false; // nothing there to remove
            }

            if (await TryWriteAsync(storeName, row, new(remaining), read.ETag))
            {
                return true;
            }
        }

        throw new IOException($"the follow document '{row}' kept conflicting after {Attempts} attempts");
    }

    private async Task<bool> TryWriteAsync(string storeName, GrainId row, FollowDocument document, string etag)
    {
        try
        {
            await _storage.WriteStateAsync(storeName, row, new GrainState<FollowDocument>(document, etag));
            return true;
        }
        catch (InconsistentStateException)
        {
            return false; // another writer moved the document: the loop re-reads it
        }
    }

    /// <summary>One handle's edges in one direction.</summary>
    private sealed record FollowDocument(List<FollowRow> Rows);

    /// <summary>One edge as the document stores it.</summary>
    private sealed record FollowRow(string ActorUri, bool Established, DateTimeOffset CreatedAt);
}
