namespace Squid.Api.Recovery;

/// <summary>One epoch of the instance kit keyring: the epoch number and its `RK` wrapped by the CURRENT instance key.</summary>
public sealed record InstanceKitKey(int Epoch, string WrappedKey);

/// <summary>
/// The persisted half of the instance recovery kit that SPEC-0012 section E reissues on every
/// rotation: the credential as the hash data-model section 2.12 fixes (never the credential
/// itself, never a clear `RK` — SPEC-0012 non-functional), and the keyring whose entries stay
/// wrapped so R-20's old epochs remain readable without any secret sitting in the table in
/// clear. No <c>VerifiedAt</c>: FR-007's notice belongs to the account kit, and nothing reads an
/// instance marker back — the credential hash is what <c>kit/verify</c> compares against.
/// </summary>
public sealed record InstanceRecoveryKit(
    string RecoveryCredentialHash,
    IReadOnlyList<InstanceKitKey> Keyring);

/// <summary>
/// Storage port of the instance kit document. Lives in <c>Recovery/**</c> beside the route that
/// verifies against it; implemented over the same <c>IGrainStorage</c> document table the kit
/// verification rows already use (TASK-0051/0016 precedent — no new table, no migration).
/// </summary>
public interface IInstanceRecoveryKitStore
{
    /// <summary>The current instance kit, or null before the first rotation ever answered one.</summary>
    Task<InstanceRecoveryKit?> FindAsync(CancellationToken ct = default);

    /// <summary>Replaces the document — the rotation's full write and the only writer besides itself.</summary>
    Task SaveAsync(InstanceRecoveryKit kit, CancellationToken ct = default);
}
