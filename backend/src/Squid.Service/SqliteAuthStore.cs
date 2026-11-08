using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Auth;
using Squid.Persistence;

namespace Squid.Service;

/// <summary>
/// <see cref="IAuthStore"/> over the same SQLite <c>IGrainStorage</c> the actor stream uses: one
/// row per handle (<c>auth</c> / <c>account/&lt;handle&gt;</c>), no new table and no migration —
/// the schema of TASK-0003 already stores any keyed document. Every update is a read-modify-write
/// under the ETag compare-and-swap, re-read on conflict, so two requests cannot lose each other's
/// session or revocation.
/// </summary>
public sealed class SqliteAuthStore : IAuthStore
{
    /// <summary>Store name of the credential rows (the stateName argument of the storage).</summary>
    public const string StoreName = "auth";

    /// <summary>Re-reads before giving up: the row is small and contended only per handle.</summary>
    private const int Attempts = 3;

    private readonly IGrainStorage _storage;

    /// <summary>Binds the store to the storage the host already created (schema exists with it).</summary>
    public SqliteAuthStore(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    /// <summary>Row key of the credential document: the handle travels as the grain key.</summary>
    private static GrainId AccountId(string handle) => GrainId.Create("account", handle);

    /// <inheritdoc />
    public async Task<AuthAccount?> FindAsync(string handle)
    {
        var state = new GrainState<AuthAccount>();
        await _storage.ReadStateAsync(StoreName, AccountId(handle), state);
        return state.State;
    }

    /// <inheritdoc />
    public async Task CreateAsync(string handle, AuthAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        // A null ETag only ever inserts (SqliteGrainStorage): an existing row is a conflict and
        // nothing is overwritten.
        var state = new GrainState<AuthAccount>(account, null);
        await _storage.WriteStateAsync(StoreName, AccountId(handle), state);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(string handle, Func<AuthAccount, AuthAccount> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var current = new GrainState<AuthAccount>();
            await _storage.ReadStateAsync(StoreName, AccountId(handle), current);
            if (current.State is null) return false;

            var state = new GrainState<AuthAccount>(mutate(current.State), current.ETag);
            try
            {
                await _storage.WriteStateAsync(StoreName, AccountId(handle), state);
                return true;
            }
            catch (InconsistentStateException)
            {
                // Another request moved the row first: re-read its current form and apply again.
                // The loop either converges or reports the conflict after the last attempt.
            }
        }

        throw new IOException($"the credential row of '{handle}' kept conflicting after {Attempts} attempts");
    }
}
