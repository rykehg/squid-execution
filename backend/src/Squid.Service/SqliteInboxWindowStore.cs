using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Grains.Inbox;

namespace Squid.Service;

/// <summary>
/// <see cref="IInboxWindowStore"/> over the same SQLite <c>IGrainStorage</c> (pattern
/// <c>SqliteActorStatusStore</c>): no table and no migration — one keyed document per local
/// recipient holding the last <c>capacity</c> ids its inbox saw, written with the same optimistic
/// guard so two deliveries of the same id race into one remembering and one refusal (contract
/// activitypub §3 step 6, FR-035).
/// </summary>
public sealed class SqliteInboxWindowStore : IInboxWindowStore
{
    /// <summary>Store name of the windows (one document per recipient).</summary>
    public const string StoreName = "inbox-window";

    /// <summary>Re-reads before giving up when the write races another delivery.</summary>
    private const int Attempts = 5;

    private readonly IGrainStorage _storage;

    /// <summary>Binds the store to the storage the host already created (schema exists with it).</summary>
    public SqliteInboxWindowStore(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    private static GrainId Row(string recipient) => GrainId.Create("inbox", recipient);

    /// <inheritdoc />
    public async Task<bool> TryRememberAsync(string recipient, string activityId, int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<WindowDocument>();
            await _storage.ReadStateAsync(StoreName, Row(recipient), read);

            var ids = read.State?.Ids;
            if (ids is not null && ids.Contains(activityId, StringComparer.Ordinal))
            {
                return false; // the window already holds it: 202 without reprocessing
            }

            var next = ids is null ? new List<string>(1) : new List<string>(ids) { activityId };
            if (next.Count > capacity)
            {
                next.RemoveRange(0, next.Count - capacity); // the window keeps the most recent ids
            }

            try
            {
                await _storage.WriteStateAsync(
                    StoreName, Row(recipient), new GrainState<WindowDocument>(new(next), read.ETag));
                return true;
            }
            catch (InconsistentStateException)
            {
                // Another delivery moved the window in between: re-read and ask again.
            }
        }

        throw new IOException($"the inbox window of '{recipient}' kept conflicting after {Attempts} attempts");
    }

    /// <summary>The window itself: the ids of one recipient, oldest first.</summary>
    private sealed record WindowDocument(List<string> Ids);
}
