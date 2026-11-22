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

    /// <summary>
    /// Every epoch this instance can open, by number (SPEC-0012 section E: the kit reissue keeps
    /// the old epochs readable — R-20). Empty while only one key is known, which is what keeps a
    /// single-key setup on the ledger fallback restore already used.
    /// </summary>
    IReadOnlyDictionary<int, RootKey> ResolveRing() => new Dictionary<int, RootKey>();
}

/// <summary>
/// Reads <c>Squid:RecoveryKey</c> — the same secret <c>RuntimeSigningKeyRing</c> opens at boot —
/// and rebuilds the `RK` the backup chain wraps its per-backup `DK`s with (SPEC-0012 section A:
/// <c>HKDF(RK, "fed/backup-kek")</c>; section E rotates exactly this value and re-wraps them).
/// After a rotation the swapped-in key and keyring win over configuration, so the running process
/// keeps backing up under the new epoch until the operator reloads the configuration with the
/// values the reissued kit delivered (AC-05).
/// <para>
/// It lives in <c>Squid.Api</c> because <c>RootKey.FromBytes</c> is internal to this assembly and
/// the backup surface of this task's <c>touches</c> is <c>Squid.Api/Admin</c>; it mirrors the parse
/// <c>RuntimeSigningKeyRing.FromConfiguration</c> does, whose root key is deliberately private.
/// </para>
/// </summary>
public sealed class ConfiguredBackupKeySource : IBackupKeySource
{
    private readonly IConfiguration _configuration;

    private RootKey? _rotated;

    private IReadOnlyDictionary<int, RootKey>? _rotatedRing;

    /// <summary>Binds the source to the configuration the host already built.</summary>
    public ConfiguredBackupKeySource(IConfiguration configuration) =>
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <inheritdoc />
    public RootKey? Resolve() => _rotated ?? ParseKey(_configuration["Squid:RecoveryKey"]);

    /// <inheritdoc />
    public IReadOnlyDictionary<int, RootKey> ResolveRing() =>
        _rotatedRing ?? ParseRing(_configuration["Squid:RecoveryKeyRing"]);

    /// <summary>
    /// Installs what a rotation just produced (SPEC-0012 section E): the current key and the full
    /// keyring, both in memory — the response to the operator is the one-shot delivery, so the
    /// process itself never re-reads them from anywhere else.
    /// </summary>
    public void RotateTo(RootKey current, IReadOnlyDictionary<int, RootKey> ring)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(ring);
        _rotated = current;
        _rotatedRing = ring;
    }

    /// <summary>The configured value as an `RK` — base64 of exactly 32 bytes, or null (same rule as the ring).</summary>
    internal static RootKey? ParseKey(string? value)
    {
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

    /// <summary>
    /// The retired-epoch half of the kit's keyring: <c>epoch:base64,epoch:base64</c>, the shape
    /// the rotation response hands the operator to paste into <c>Squid:RecoveryKeyRing</c>.
    /// Malformed entries are skipped rather than thrown: like every other configuration read of
    /// this codebase, a bad value degrades to "fewer epochs open" and never breaks the boot.
    /// </summary>
    internal static IReadOnlyDictionary<int, RootKey> ParseRing(string? value)
    {
        var ring = new Dictionary<int, RootKey>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return ring;
        }

        foreach (var entry in value.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0
                || !int.TryParse(entry[..separator], out var epoch)
                || epoch < 1
                || ring.ContainsKey(epoch))
            {
                continue;
            }

            var key = ParseKey(entry[(separator + 1)..]);
            if (key is not null)
            {
                ring[epoch] = key;
            }
        }

        return ring;
    }
}
