using System.Security.Cryptography;
using Squid.Grains.Actors;

namespace Squid.Api.Crypto;

/// <summary>
/// The runtime secret behind the outbound federation key (SPEC-0011 section B, GAP-09): the
/// <c>Squid:RecoveryKey</c> read at boot — base64 of the 32-byte <c>RK</c>, the same serialized
/// shape every other key material of this codebase travels in. Absent leaves the ring disabled:
/// the instance keeps serving local reads and writes and only the federated output stays off with
/// the boot warning (AC-01) — a missing key is never a reason to fall. A value that IS configured
/// but cannot open an actor's wrapped key — malformed, wrong length, or simply another key — fails
/// at activation as a typed error, never at boot and never with the material in the message
/// (AC-03), and the secret itself reaches no log, route or persisted row (SPEC-0011 non-functional).
/// </summary>
public sealed class RuntimeSigningKeyRing : IActorSigningKeyRing
{
    private readonly RootKey? _rootKey;

    private readonly bool _configured;

    private RuntimeSigningKeyRing(RootKey? rootKey, bool configured)
    {
        _rootKey = rootKey;
        _configured = configured;
    }

    /// <summary>True only when a well-formed secret was configured at boot — what the warning watches.</summary>
    public bool OutputEnabled => _rootKey is not null;

    /// <summary>
    /// Reads the configuration value. Anything that is not base64 of exactly
    /// <see cref="RootKey.SizeInBytes"/> bytes disables the output instead of throwing at boot
    /// (AC-01); the value stays remembered as configured so opening a key refuses loudly (AC-03).
    /// </summary>
    public static RuntimeSigningKeyRing FromConfiguration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new RuntimeSigningKeyRing(null, configured: false);
        }

        try
        {
            var bytes = Convert.FromBase64String(value.Trim());
            return bytes.Length == RootKey.SizeInBytes
                ? new RuntimeSigningKeyRing(RootKey.FromBytes(bytes), configured: true)
                : new RuntimeSigningKeyRing(null, configured: true);
        }
        catch (FormatException)
        {
            return new RuntimeSigningKeyRing(null, configured: true);
        }
    }

    /// <inheritdoc />
    public RSA? OpenSigningKey(string wrappedSigningKey)
    {
        if (_rootKey is null)
        {
            if (!_configured)
            {
                return null; // nothing configured at all: output disabled, nothing to try
            }

            // Configured but unusable: the failure belongs at activation, typed and without the
            // value itself (AC-03) — the message names the key, never its content.
            throw new FormatException("the configured Squid:RecoveryKey is not base64 of a 32-byte root key");
        }

        return KeyDerivation.UnwrapRsaPrivateKey(_rootKey, Convert.FromBase64String(wrappedSigningKey));
    }
}
