namespace Squid.Api.Auth;

/// <summary>
/// One open session of a local actor (SPEC-0007 B-05): the id the cookie carries and the CSRF
/// secret the double submission compares (B-06). Both are opaque random tokens.
/// </summary>
public sealed record SessionRecord(string Id, string CsrfToken, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// The recovery material registration generates (B-02): the `RC` and the `RK` already sealed under
/// the password, in the two shapes the kit needs. The clear `RK` never reaches this record (B-03).
/// </summary>
public sealed record KitRecord(string RecoveryCode, string EncryptedRootKey, DateTimeOffset CreatedAt);

/// <summary>
/// The credential row of a local actor: the Argon2id password hash (B-04), its open sessions (B-05)
/// and the kit material (B-12). One document per handle in the SQLite grain storage — a row the
/// composition root owns, because <c>Squid.Api</c> may not reference the storage project (ADR-35).
/// </summary>
public sealed record AuthAccount
{
    /// <summary>PHC string of the password, never the password (FR-002, B-04).</summary>
    public required string PasswordHash { get; init; }

    /// <summary>Open sessions; empty after <c>logout-all</c>, without this one after <c>logout</c>.</summary>
    public List<SessionRecord> Sessions { get; init; } = [];

    /// <summary>Kit material of this account — generated once, at registration.</summary>
    public KitRecord? Kit { get; init; }
}

/// <summary>The `RC` as the wire shows it: <c>RC-1A2B3C4D-…</c> (contracts/http-api.md §3).</summary>
public static class RecoveryCredential
{
    /// <summary>Groups of 4 hex characters, so the value reads as the contract's example does.</summary>
    public static string Format(byte[] recoveryCode)
    {
        ArgumentNullException.ThrowIfNull(recoveryCode);

        var hex = Convert.ToHexString(recoveryCode);
        var groups = new string[hex.Length / 4];
        for (var i = 0; i < groups.Length; i++)
        {
            groups[i] = hex.AsSpan(i * 4, 4).ToString();
        }

        return "RC-" + string.Join('-', groups);
    }
}
