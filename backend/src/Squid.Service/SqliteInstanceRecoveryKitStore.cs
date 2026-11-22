using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Recovery;

namespace Squid.Service;

/// <summary>
/// <see cref="IInstanceRecoveryKitStore"/> over the same SQLite <c>IGrainStorage</c> the kit
/// verification rows use: one document (<c>instance-kit</c> / <c>instance</c>) — no new table and
/// no migration, the <see cref="SqliteRecoveryStore"/> precedent. What the document holds is what
/// SPEC-0012 section E allows to exist after the one-shot response left: the credential as its
/// hash and every epoch's key wrapped — never a clear key in persistence.
/// </summary>
public sealed class SqliteInstanceRecoveryKitStore : IInstanceRecoveryKitStore
{
    /// <summary>Store name of the instance kit document (the stateName argument of the storage).</summary>
    public const string StoreName = "instance-kit";

    /// <summary>Re-reads before giving up: the row is tiny and only a rotation writes it.</summary>
    private const int Attempts = 3;

    private readonly IGrainStorage _storage;

    /// <summary>Binds the store to the storage the host already created (schema exists with it).</summary>
    public SqliteInstanceRecoveryKitStore(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    /// <summary>Row key of the document: the instance is one, so the key is constant.</summary>
    private static GrainId KitId() => GrainId.Create("instance-kit", "instance");

    /// <inheritdoc />
    public async Task<InstanceRecoveryKit?> FindAsync(CancellationToken ct = default)
    {
        var state = new GrainState<InstanceRecoveryKit>();
        await _storage.ReadStateAsync(StoreName, KitId(), state);
        return state.State;
    }

    /// <inheritdoc />
    public async Task SaveAsync(InstanceRecoveryKit kit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(kit);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var current = new GrainState<InstanceRecoveryKit>();
            await _storage.ReadStateAsync(StoreName, KitId(), current);

            // Null ETag only ever inserts: the first rotation creates the row, later ones replace it.
            var state = new GrainState<InstanceRecoveryKit>(kit, current.ETag);
            try
            {
                await _storage.WriteStateAsync(StoreName, KitId(), state);
                return;
            }
            catch (InconsistentStateException)
            {
                // Another write landed first (a concurrent verify never writes, but the retry is
                // free and keeps the same CAS discipline as every store beside this one).
            }
        }

        throw new IOException("the instance kit row kept conflicting after " + Attempts + " attempts");
    }
}
