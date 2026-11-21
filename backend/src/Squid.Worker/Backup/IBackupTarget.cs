namespace Squid.Worker.Backup;

/// <summary>
/// The destination contract of backup-recovery section 1 (SPEC-0012 section B): a target receives
/// ONLY ciphertext and non-sensitive metadata — no key ever transits; a target failure never
/// corrupts the local backup; <see cref="ListAsync"/> is the single discovery of what was already
/// sent (no parallel state in the coordinator); <see cref="DeleteAsync"/> exists for rotation and
/// purge only. Implementations today: <see cref="LocalDiskTarget"/>; S3 is TASK-0057.
/// </summary>
public interface IBackupTarget
{
    /// <summary>Stable id of this destination (<c>"local-disk"</c>, <c>"s3-offsite"</c>, …).</summary>
    string TargetId { get; }

    /// <summary>Stores <paramref name="ciphertext"/> plus <paramref name="envelope"/> metadata.</summary>
    Task PutAsync(BackupEnvelope envelope, Stream ciphertext, CancellationToken ct);

    /// <summary>The stored ciphertext of one envelope, for the re-read the verification needs.</summary>
    Task<Stream> GetAsync(string envelopeId, CancellationToken ct);

    /// <summary>The envelopes of the destination covering <paramref name="fromSeq"/> onward — the only discovery.</summary>
    Task<IReadOnlyList<BackupEnvelope>> ListAsync(long fromSeq, CancellationToken ct);

    /// <summary>Whether the envelope was sent to this destination.</summary>
    Task<bool> ExistsAsync(string envelopeId, CancellationToken ct);

    /// <summary>Removes the envelope — rotation/purge only, never automatic (contract section 7).</summary>
    Task DeleteAsync(string envelopeId, CancellationToken ct);
}
