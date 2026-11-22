using System.Globalization;

namespace Squid.Api.Admin;

/// <summary>
/// The redundancy row of contract backup-recovery section 6 (SPEC-0012 section G): how many of
/// the destinations this host is configured to write hold the most recent VERIFIED envelope.
/// The score string lives here — not in each caller — so <c>GET /api/admin/backup/status</c> and
/// the thin page <c>GET /admin/backup</c> serves render the very same value: one formatter, two
/// surfaces, no drift between them.
/// </summary>
/// <param name="VerifiedTargets">
/// Configured destinations holding a verified row of the newest verified envelope's window —
/// read from the registry, never from a destination's header (the S-47 honesty rule).
/// </param>
/// <param name="ConfiguredTargets">
/// Destinations the composition root wires: the local volume always, off-site only when
/// <c>Backup:S3:*</c> names endpoint, bucket and credential.
/// </param>
/// <param name="OffsiteVerified">
/// A configured off-site destination holds that envelope verified — the disaster-recovery half
/// doc 05 §8 asks about.
/// </param>
public sealed record BackupRedundancy(
    int VerifiedTargets,
    int ConfiguredTargets,
    bool OffsiteVerified)
{
    /// <summary>
    /// <c>redundancy.score</c> in the format doc 05 §8 and contract section 6 fix:
    /// <c>"Redundância: 1/2 — sem recuperação de desastre"</c> while no configured off-site
    /// destination holds the newest verified envelope, and without the suffix once one does
    /// (SPEC-0012 section G).
    /// </summary>
    public string Score
    {
        get
        {
            var score = FormattableString.Invariant(
                $"Redundância: {VerifiedTargets}/{ConfiguredTargets}");
            return OffsiteVerified ? score : score + " — sem recuperação de desastre";
        }
    }
}

/// <summary>Reads the redundancy of the backup registry (contract backup-recovery section 6).</summary>
public interface IBackupRedundancySource
{
    /// <summary>The current <see cref="BackupRedundancy"/> of the instance.</summary>
    Task<BackupRedundancy> ReadAsync(CancellationToken ct);
}
