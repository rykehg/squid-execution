using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Recovery;
using Squid.Persistence;

namespace Squid.Service;

/// <summary>
/// <see cref="IRecoveryStore"/> over the same SQLite <c>IGrainStorage</c> the credential row uses:
/// one marker per handle (<c>recovery</c> / <c>kit/&lt;handle&gt;</c>) — no new table and no
/// migration, because FR-007 needs a timestamp, not a schema (ADR-35 keeps this adapter at the
/// composition root). Every write is the read-modify-write under the ETag compare-and-swap the
/// other two stores already follow.
/// </summary>
public sealed class SqliteRecoveryStore : IRecoveryStore
{
    /// <summary>Store name of the kit verification rows (the stateName argument of the storage).</summary>
    public const string StoreName = "recovery";

    /// <summary>Re-reads before giving up: the row is tiny and contended only per handle.</summary>
    private const int Attempts = 3;

    private readonly IGrainStorage _storage;

    /// <summary>Binds the store to the storage the host already created (schema exists with it).</summary>
    public SqliteRecoveryStore(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    /// <summary>Row key of the verification document: the handle travels as the grain key.</summary>
    private static GrainId KitId(string handle) => GrainId.Create("kit", handle);

    /// <inheritdoc />
    public async Task<DateTimeOffset?> ReadVerifiedAtAsync(string handle)
    {
        var state = new GrainState<KitVerification>();
        await _storage.ReadStateAsync(StoreName, KitId(handle), state);
        return state.State?.VerifiedAt;
    }

    /// <inheritdoc />
    public async Task MarkVerifiedAsync(string handle, DateTimeOffset verifiedAt)
    {
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var current = new GrainState<KitVerification>();
            await _storage.ReadStateAsync(StoreName, KitId(handle), current);

            // Null ETag only ever inserts: the first proof creates the row, later ones move it.
            var state = new GrainState<KitVerification>(new KitVerification(verifiedAt), current.ETag);
            try
            {
                await _storage.WriteStateAsync(StoreName, KitId(handle), state);
                return;
            }
            catch (InconsistentStateException)
            {
                // Another request wrote first: re-read its marker and apply this one again.
            }
        }

        throw new IOException($"the kit verification row of '{handle}' kept conflicting after {Attempts} attempts");
    }
}
