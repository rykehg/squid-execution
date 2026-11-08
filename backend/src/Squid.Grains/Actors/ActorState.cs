namespace Squid.Grains.Actors;

/// <summary>
/// One public key of the actor (data-model §2.1, <c>PublicKeys</c>): algorithm, key id and the
/// public bytes, base64. Public material only — it is meant to be served to the world (B-10).
/// </summary>
public sealed record ActorPublicKey(string Algorithm, string KeyId, string PublicKey);

/// <summary>
/// Everything the actor persists about its keys (data-model §2.1, GAP-09): the signing key wrapped
/// for storage and the public keys, both base64. The schema has no field that can carry the root
/// key or a private key in the clear — the grain is only ever handed wrapped/public bytes.
/// </summary>
public sealed record ActorKeyMaterial(ActorPublicKey[] PublicKeys, string WrappedSigningKey)
{
    /// <summary>
    /// Value equality over the key array: the synthesized record member would compare the array by
    /// reference, and a state rebuilt from storage must equal the state that was written (AC-01/AC-04).
    /// </summary>
    public bool Equals(ActorKeyMaterial? other) =>
        other is not null
        && WrappedSigningKey == other.WrappedSigningKey
        && PublicKeys.AsSpan().SequenceEqual(other.PublicKeys);

    /// <summary>Hash consistent with <see cref="Equals(ActorKeyMaterial?)"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(WrappedSigningKey, PublicKeys.Length);
}

/// <summary>
/// Direct state of the actor: exactly what a rebuild from the snapshot and the open log has to
/// reproduce (AC-01, AC-04). A record, so equality is the assertion those tests make.
/// </summary>
public sealed record ActorState(string Handle, string? DisplayName, ActorKeyMaterial Keys);
