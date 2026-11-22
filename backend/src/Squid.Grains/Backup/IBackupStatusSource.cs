namespace Squid.Grains.Backup;

/// <summary>
/// The observability row of contract backup-recovery section 6 behind a port, so
/// <c>GET /api/admin/backup/status</c> reads it without the edge ever touching storage (ADR-35).
/// </summary>
/// <param name="LastSuccessAt">`backup.last_success_at` — the newest `verified_at` of the registry.</param>
/// <param name="EnvelopesCount">`backup.envelopes_count`.</param>
/// <param name="BytesTotal">`backup.bytes_total`.</param>
/// <param name="VerifyFailures">`backup.verify_failures` — envelopes registered without a confirmed re-read.</param>
/// <param name="LastDrillAt">`restore.last_drill_at` — null until the drills of TASK-0056 record one.</param>
/// <param name="LastDrillResult">`restore.last_drill_result` — null until the drills of TASK-0056 record one.</param>
public sealed record BackupStatus(
    DateTimeOffset? LastSuccessAt,
    long EnvelopesCount,
    long BytesTotal,
    long VerifyFailures,
    DateTimeOffset? LastDrillAt,
    string? LastDrillResult);

/// <summary>Reads the section 6 state of the backup registry.</summary>
public interface IBackupStatusSource
{
    /// <summary>The current <see cref="BackupStatus"/> of the instance.</summary>
    Task<BackupStatus> ReadAsync(CancellationToken ct);
}
