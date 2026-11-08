namespace Squid.Api.Recovery;

/// <summary>The moment the account proved it kept its kit — data-model §2.12 <c>VerifiedAt</c> (FR-007).</summary>
public sealed record KitVerification(DateTimeOffset VerifiedAt);

/// <summary>
/// Storage port of the kit verification row (SPEC-0007 B-13). It lives in
/// <c>Squid.Api/Recovery</c> because the credential row (<see cref="Auth.IAuthStore"/> and the
/// <c>KitRecord</c> it carries) belongs to the closed auth slice and has no field for it — the
/// notice FR-007 needs is a row of its own, implemented at the composition root exactly like the
/// stores before it (ADR-35: <c>Squid.Api</c> sees no storage project).
/// </summary>
public interface IRecoveryStore
{
    /// <summary>When the kit was verified, or null while FR-007 keeps the warning alive.</summary>
    Task<DateTimeOffset?> ReadVerifiedAtAsync(string handle);

    /// <summary>
    /// Records the verification under compare-and-swap, inserting the row on the first proof.
    /// </summary>
    Task MarkVerifiedAsync(string handle, DateTimeOffset verifiedAt);
}
