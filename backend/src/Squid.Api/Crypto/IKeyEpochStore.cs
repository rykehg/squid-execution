namespace Squid.Api.Crypto;

/// <summary>
/// The <c>key_epoch</c> ledger of data-model section 2.10 (SPEC-0012 section E): migration 0010
/// creates the table and never a row — rows are written by key rotation only. Declared on the
/// edge (ADR-35); the composition root binds it to the table beside the backup registry.
/// </summary>
public interface IKeyEpochStore
{
    /// <summary>
    /// The highest epoch of the ledger, or 1 before the first rotation — the value the next
    /// envelope must carry ("nenhum envelope sem KeyEpoch valida").
    /// </summary>
    Task<int> CurrentEpochAsync(CancellationToken ct = default);

    /// <summary>
    /// Retires <paramref name="previousEpoch"/> and opens <paramref name="newEpoch"/> in one
    /// transaction. When the previous row does not exist yet (first rotation, or a ledger a
    /// disaster wiped), it is materialised with <paramref name="previousFingerprint"/> and the
    /// retirement in the same write, so "RetiredAt da época anterior" exists even on a fresh
    /// instance — AC-01 of TASK-0058.
    /// </summary>
    Task AdvanceAsync(
        int previousEpoch,
        string previousFingerprint,
        int newEpoch,
        string newFingerprint,
        DateTimeOffset at,
        CancellationToken ct = default);
}
