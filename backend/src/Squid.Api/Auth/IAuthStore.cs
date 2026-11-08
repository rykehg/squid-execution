namespace Squid.Api.Auth;

/// <summary>
/// Storage port of the credential row (password hash, sessions, kit). It lives in
/// <c>Squid.Api</c> because that is where the auth surface is, and it is implemented at the
/// composition root — <c>Squid.Api</c> may not reference <c>Squid.Persistence</c> (ADR-35), the
/// same arrangement the actor stream store has through <c>IActorEventStore</c>.
/// </summary>
public interface IAuthStore
{
    /// <summary>The row of <paramref name="handle"/>, or null when the instance has never seen it.</summary>
    Task<AuthAccount?> FindAsync(string handle);

    /// <summary>Inserts the row; a row that already exists is refused and nothing is overwritten.</summary>
    Task CreateAsync(string handle, AuthAccount account);

    /// <summary>
    /// Applies <paramref name="mutate"/> to the row of <paramref name="handle"/> and persists the
    /// result with compare-and-swap, re-reading on conflict. False when the row does not exist —
    /// nothing is created on the side (the same rule the actor store follows).
    /// </summary>
    Task<bool> UpdateAsync(string handle, Func<AuthAccount, AuthAccount> mutate);
}
