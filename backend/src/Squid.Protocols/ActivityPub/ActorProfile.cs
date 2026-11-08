using Squid.Grains.Actors;

namespace Squid.Protocols.ActivityPub;

/// <summary>
/// Canonical profile of a local actor (SPEC-0007 B-11): exactly the fields the JSON-LD document
/// carries, in canonical form, under the one origin of the request that asked (B-09a). The
/// round-trip canonical → JSON-LD → canonical has to reproduce this record unchanged — anything
/// the document drops shows up here as an inequality, which is what AC-04's test asserts.
/// </summary>
public sealed record ActorProfile(
    string Origin,
    string PreferredUsername,
    string? Name,
    string RsaPublicKeyBase64,
    string Ed25519PublicKeyBase64)
{
    /// <summary>Canonical id of the actor document — origin + handle, the single place it forms (B-09a).</summary>
    public string Id => $"{Origin}/users/{PreferredUsername}";

    /// <summary>Builds the profile from actor state, taking only published (public) material.</summary>
    public static ActorProfile FromState(ActorState state, string origin)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        return new ActorProfile(
            origin,
            state.Handle,
            state.DisplayName,
            KeyOf(state, "RSA-2048"),
            KeyOf(state, "Ed25519"));
    }

    private static string KeyOf(ActorState state, string algorithm) =>
        state.Keys.PublicKeys.First(key =>
            string.Equals(key.Algorithm, algorithm, StringComparison.Ordinal)).PublicKey;
}
