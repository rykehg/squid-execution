using System.Security.Cryptography;
using Squid.Grains.Actors;

namespace Squid.Api.Crypto;

/// <summary>
/// The runtime secret behind the outbound federation key (SPEC-0011 section B, GAP-09): the
/// <c>Squid:RecoveryKey</c> read at boot — base64 of the 32-byte <c>RK</c>, the same serialized
/// shape every other key material of this codebase travels in. Absent leaves the ring disabled:
/// the instance keeps serving local reads and writes and only the federated output stays off with
/// the boot warning (AC-01) — a missing key is never a reason to fall. A value that IS configured
/// but cannot open the copy — malformed, wrong length, or simply another key — fails at activation
/// as a typed error, never at boot and never with the material in the message (AC-03), and the
/// secret itself reaches no log, route or persisted row (SPEC-0011 non-functional).
/// TASK-0051 (duplo embrulho): this ring is also the instance half of a registration — it wraps
/// the actor's own RSA a second time, and it opens the copy that <c>0009_instance_signing_keys</c>
/// keeps for the handle. An actor with no copy (registered before the secret existed) activates to
/// null: its output stays disabled, auditably, at the send (AC-05).
/// </summary>
public sealed class RuntimeSigningKeyRing : IActorSigningKeyRing
{
    private readonly RootKey? _rootKey;

    private readonly bool _configured;

    private readonly IInstanceSigningKeyStore _copies;

    private RuntimeSigningKeyRing(RootKey? rootKey, bool configured, IInstanceSigningKeyStore copies)
    {
        _rootKey = rootKey;
        _configured = configured;
        _copies = copies;
    }

    /// <summary>True only when a well-formed secret was configured at boot — what the warning watches.</summary>
    public bool OutputEnabled => _rootKey is not null;

    /// <summary>
    /// Reads the configuration value against the store the copies live in. Anything that is not
    /// base64 of exactly <see cref="RootKey.SizeInBytes"/> bytes disables the output instead of
    /// throwing at boot (AC-01); the value stays remembered as configured so opening a key refuses
    /// loudly (AC-03).
    /// </summary>
    public static RuntimeSigningKeyRing FromConfiguration(string? value, IInstanceSigningKeyStore copies)
    {
        ArgumentNullException.ThrowIfNull(copies);

        if (string.IsNullOrWhiteSpace(value))
        {
            return new RuntimeSigningKeyRing(null, configured: false, copies);
        }

        try
        {
            var bytes = Convert.FromBase64String(value.Trim());
            return bytes.Length == RootKey.SizeInBytes
                ? new RuntimeSigningKeyRing(RootKey.FromBytes(bytes), configured: true, copies)
                : new RuntimeSigningKeyRing(null, configured: true, copies);
        }
        catch (FormatException)
        {
            return new RuntimeSigningKeyRing(null, configured: true, copies);
        }
    }

    /// <summary>
    /// Registration side of the double wrap (SPEC-0011 section B, TASK-0051 AC-01): the SAME
    /// actor RSA sealed once more with <c>HKDF(RK_instance, "fed/rsa-wrap")</c>. Null when no
    /// usable secret is configured — the registration then births no copy and behaves exactly as
    /// it did before the task.
    /// </summary>
    public byte[]? WrapInstanceCopy(RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        return _rootKey is null ? null : KeyDerivation.WrapRsaPrivateKey(_rootKey, rsa);
    }

    /// <inheritdoc />
    public async Task<RSA?> OpenInstanceCopyAsync(string handle)
    {
        if (!_configured)
        {
            return null; // nothing configured at all: output disabled, no lookup, nothing attempted
        }

        var wrapped = await _copies.FindAsync(handle);
        if (wrapped is null)
        {
            return null; // registered before the configuration: no copy, output disabled (AC-05)
        }

        if (_rootKey is null)
        {
            // Configured but unusable: the failure belongs at activation, typed and without the
            // value itself (AC-03) — the message names the key, never its content.
            throw new FormatException("the configured Squid:RecoveryKey is not base64 of a 32-byte root key");
        }

        return KeyDerivation.UnwrapRsaPrivateKey(_rootKey, Convert.FromBase64String(wrapped));
    }
}
