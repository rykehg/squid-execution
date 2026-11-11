namespace Squid.ReadModels.Quota;

/// <summary>
/// The instance quota ledger of SPEC-0009 section F (data-model §2.13, FR-029): one row, integer
/// bytes (R-19), default 10 GiB. The upload gate reserves against it optimistically and the daily
/// blob sweep corrects it — the sweep is the source of truth, the reservation is only the fast
/// gate (GAP-20 assumption in force: drift between two sweeps is accepted).
/// </summary>
public interface IQuotaLedger
{
    /// <summary>
    /// Reserves <paramref name="bytes"/> against <paramref name="limitBytes"/> in one atomic
    /// conditional increment. False means it would break the limit: the row is NOT touched, so the
    /// caller persists nothing and answers <c>507 quota_exceeded</c>. The first reservation of an
    /// instance creates the row (AC-03).
    /// </summary>
    Task<bool> TryReserveAsync(long bytes, long limitBytes, DateTimeOffset now);

    /// <summary>The reconcile write: the byte total the blob sweep found, stamped with its time (AC-02).</summary>
    Task SetUsedAsync(long usedBytes, DateTimeOffset now);
}
