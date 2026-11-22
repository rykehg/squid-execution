using Microsoft.Extensions.Configuration;
using Squid.Api.Crypto;

namespace Squid.Api.Admin;

/// <summary>The instance `RK` the per-backup `DK` is wrapped under (SPEC-0012 section A).</summary>
public interface IBackupKeySource
{
    /// <summary>
    /// The configured instance root key, or null when <c>Squid:RecoveryKey</c> is absent or
    /// unusable — the run then reports its own failure instead of encrypting under a key no kit
    /// can restore. Never logged, never persisted.
    /// </summary>
    RootKey? Resolve();
}

/// <summary>
/// Reads <c>Squid:RecoveryKey</c> — the same secret <c>RuntimeSigningKeyRing</c> opens at boot —
/// and rebuilds the `RK` the backup chain wraps its per-backup `DK`s with (SPEC-0012 section A:
/// <c>HKDF(RK, "fed/backup-kek")</c>; section E rotates exactly this value and re-wraps them).
/// <para>
/// It lives in <c>Squid.Api</c> because <c>RootKey.FromBytes</c> is internal to this assembly and
/// the backup surface of this task's <c>touches</c> is <c>Squid.Api/Admin</c>; it mirrors the parse
/// <c>RuntimeSigningKeyRing.FromConfiguration</c> does, whose root key is deliberately private.
/// </para>
/// </summary>
public sealed class ConfiguredBackupKeySource(IConfiguration configuration) : IBackupKeySource
{
    /// <inheritdoc />
    public RootKey? Resolve()
    {
        var value = configuration["Squid:RecoveryKey"];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(value.Trim());
            return bytes.Length == RootKey.SizeInBytes ? RootKey.FromBytes(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
