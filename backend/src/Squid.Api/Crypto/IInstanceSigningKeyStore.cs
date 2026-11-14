namespace Squid.Api.Crypto;

/// <summary>
/// The row home of the double wrap of SPEC-0011 section B (TASK-0051): registration writes the
/// copy of an actor's signing key that the boot <c>Squid:RecoveryKey</c> wrapped, and activation
/// reads it back by handle. Wrapped base64 material only — the port never carries a clear private
/// key (AC-04). Declared on the edge (ADR-35); the composition root binds it to
/// <c>instance_signing_keys</c> (0009_instance_signing_keys.sql).
/// </summary>
public interface IInstanceSigningKeyStore
{
    /// <summary>Persists the instance-wrapped copy of <paramref name="handle"/> — one row per handle.</summary>
    Task SaveAsync(string handle, string wrappedSigningKey);

    /// <summary>The instance-wrapped copy (base64), or null when this actor has none (AC-05).</summary>
    Task<string?> FindAsync(string handle);
}
