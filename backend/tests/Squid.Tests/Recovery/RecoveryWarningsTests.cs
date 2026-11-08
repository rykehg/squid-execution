using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Api.Recovery;

namespace Squid.Tests.Recovery;

/// <summary>
/// FR-007 (SPEC-0007 B-13) branch by branch, without a host: an unverified kit warns, a verified
/// one stops, and the notice comes back the moment the kit passes 12 months — verified or not.
/// </summary>
public sealed class RecoveryWarningsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Without_a_kit_or_without_a_verification_the_notice_lives()
    {
        Assert.Equal(new[] { "kit_unverified" }, Codes(RecoveryWarnings.For(null, null, Now)));
        Assert.Equal(
            new[] { "kit_unverified" },
            Codes(RecoveryWarnings.For(Kit(Now.AddMonths(-1)), null, Now)));
    }

    [Fact]
    public void A_verified_kit_inside_the_year_answers_with_no_notice()
    {
        Assert.Empty(RecoveryWarnings.For(Kit(Now.AddMonths(-11)), Now.AddDays(-1), Now));
        // exactly twelve months is not yet "mais de 12 meses"
        Assert.Empty(RecoveryWarnings.For(Kit(Now.AddMonths(-12)), Now, Now));
    }

    [Fact]
    public void A_kit_older_than_twelve_months_warns_even_though_it_was_verified()
    {
        Assert.Equal(
            new[] { "kit_stale" },
            Codes(RecoveryWarnings.For(Kit(Now.AddMonths(-13)), Now.AddDays(-1), Now)));

        // and an old kit that was never proven kept carries both notices
        Assert.Equal(
            new[] { "kit_unverified", "kit_stale" },
            Codes(RecoveryWarnings.For(Kit(Now.AddMonths(-13)), null, Now)));
    }

    private static KitRecord Kit(DateTimeOffset createdAt) => new(
        Convert.ToBase64String(new byte[KeyState.RecoveryCodeSizeInBytes]),
        Convert.ToBase64String(new byte[76]),
        createdAt);

    private static string[] Codes(IReadOnlyList<RecoveryWarning> warnings) =>
        warnings.Select(warning => warning.Code).ToArray();
}
