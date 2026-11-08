using Squid.Api.Auth;

namespace Squid.Api.Recovery;

/// <summary>One pending notice of <c>GET /api/auth/me</c> (contract §2): a stable code and a message in the clear.</summary>
public sealed record RecoveryWarning(string Code, string Message);

/// <summary>
/// FR-007 (SPEC-0007 B-13) as one pure decision: the account keeps a warning while its kit is
/// unverified, and again once the kit passes <see cref="KitAgeMonths"/> — verifying silences the
/// first half only, and only a younger kit silences the second.
/// </summary>
public static class RecoveryWarnings
{
    /// <summary>Kit age after which the notice returns even for a verified kit: 12 months.</summary>
    public const int KitAgeMonths = 12;

    /// <summary>The notices owed for this kit state at <paramref name="now"/>, in report order.</summary>
    public static IReadOnlyList<RecoveryWarning> For(KitRecord? kit, DateTimeOffset? verifiedAt, DateTimeOffset now)
    {
        var warnings = new List<RecoveryWarning>(2);
        if (kit is null || verifiedAt is null)
        {
            warnings.Add(new RecoveryWarning("kit_unverified", "Kit de recuperação ainda não verificado."));
        }

        if (kit is not null && kit.CreatedAt.AddMonths(KitAgeMonths) < now)
        {
            warnings.Add(new RecoveryWarning("kit_stale", $"Kit de recuperação com mais de {KitAgeMonths} meses."));
        }

        return warnings;
    }
}
