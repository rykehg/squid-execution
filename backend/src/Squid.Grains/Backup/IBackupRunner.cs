namespace Squid.Grains.Backup;

/// <summary>
/// One envelope as the wire sees it — contract backup-recovery section 2's shape, which is also
/// what <c>POST /api/admin/backup/run</c> answers (SPEC-0012 section C).
/// </summary>
/// <param name="Status">`Pending` | `Verified` | `Corrupt` — a `Corrupt` envelope is never used by restore.</param>
public sealed record BackupEnvelopeView(
    string Id,
    int KeyEpoch,
    long SeqFrom,
    long SeqTo,
    string ContentHash,
    long SizeBytes,
    string TargetId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt,
    int Format,
    string Status);

/// <summary>What one coordinator run ended as: the registered envelope, or the failure it reported.</summary>
/// <param name="Error">A message for the log only — a route never echoes it to a client (FR-053).</param>
public sealed record BackupRunResult(bool Succeeded, BackupEnvelopeView? Envelope, string? Error);

/// <summary>
/// The Worker half of contract backup-recovery section 3, declared in the grains layer for the
/// same ADR-35 reason as <see cref="IBackupStateSource"/>: the coordinator orchestrates, the
/// Worker serializes, compresses, encrypts, hashes, sends, verifies and registers.
/// </summary>
public interface IBackupRunner
{
    /// <summary>
    /// <c>seqFrom</c> of the next backup: <c>0</c> for a full snapshot, otherwise one past the
    /// highest sequence a destination already holds. The discovery goes through
    /// <c>IBackupTarget.ListAsync</c> inside the implementation — the coordinator keeps no
    /// parallel state of what was sent (contract section 1).
    /// </summary>
    Task<long> NextSeqFromAsync(CancellationToken ct);

    /// <summary>Runs one full backup of <paramref name="snapshot"/> over the given interval.</summary>
    Task<BackupRunResult> RunAsync(object snapshot, long seqFrom, long seqTo, CancellationToken ct);
}
