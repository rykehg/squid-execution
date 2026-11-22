using Squid.Api.Posts;

namespace Squid.Worker.Backup;

/// <summary>
/// The destination half of contract backup-recovery section 4, declared in the Worker for the
/// same ADR-35 reason as <c>IBackupStateSource</c>: the Worker owns the restore flow (select the
/// chain → validate hash and key epoch → decrypt → migrate the format), while only the
/// composition root may open the database and reapply state — step 6 of the contract: ledger,
/// projections and the log itself.
/// </summary>
public interface IRestoreDestination
{
    /// <summary>
    /// The destination file exists with its schema before anything reads it — the same idempotent
    /// Apply every host boot runs, so a lost database comes back without a hand-written DDL and an
    /// intact one is never touched beyond a no-op.
    /// </summary>
    Task PrepareAsync(CancellationToken ct);

    /// <summary>
    /// Step 6: reapplies the restored state (activity log with its sequences preserved, the
    /// projection pass, the quota ledger) and returns what the destination holds afterwards — the
    /// read-back step 7 compares against, count and hash per aggregate.
    /// </summary>
    Task<IReadOnlyList<ActivityEvent>> ReapplyAsync(BackupActivitySnapshot state, CancellationToken ct);
}
