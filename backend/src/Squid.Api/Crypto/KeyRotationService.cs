using System.Security.Cryptography;
using Squid.Api.Admin;
using Squid.Api.Auth;
using Squid.Api.Recovery;

namespace Squid.Api.Crypto;

/// <summary>One epoch of the keyring the reissued kit answers with — epoch number and the `RK` itself, base64.</summary>
public sealed record RotationKey(int Epoch, string Key);

/// <summary>
/// The reissued kit of SPEC-0012 section E, exactly as the one-shot response delivers it: the new
/// credential and its mnemonic, the sealed `RK` only that credential opens, the epoch it belongs
/// to, and every epoch's material in the keyring so R-20's old backups stay restorable. Built
/// once per rotation and never served again (AC-06).
/// </summary>
public sealed record RotatedInstanceKit(
    int KeyEpoch,
    string RecoveryCredential,
    IReadOnlyList<string> Mnemonic,
    string EncryptedRootKey,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RotationKey> Keyring);

/// <summary>The rotation has nothing to rotate from: no usable `Squid:RecoveryKey` at this instance.</summary>
public sealed class RotationUnavailableException(string message) : Exception(message);

/// <summary>
/// The rotation of SPEC-0012 section E (TASK-0058, decision 6 — instance material only): a fresh
/// `Squid:RecoveryKey`, `keyEpoch++` with the previous epoch retired, the <c>0009</c> copies and
/// the kit keyring re-wrapped to the new chain, a fresh `RC`, and the kit reissued with the old
/// epochs still readable (R-20 — no backup file is ever touched, FR-047).
/// Order of the write: every secret is derived and every refusal is taken BEFORE a single row
/// moves, so the crypto failing leaves the instance byte-identical to before the call.
/// </summary>
public sealed class KeyRotationService(
    RuntimeSigningKeyRing ring,
    ConfiguredBackupKeySource keys,
    IKeyEpochStore epochs,
    IInstanceSigningKeyStore copies,
    IInstanceRecoveryKitStore kits)
{
    /// <summary>Runs one rotation and returns the kit the route delivers exactly once.</summary>
    public async Task<RotatedInstanceKit> RotateAsync(CancellationToken ct = default)
    {
        var oldKey = keys.Resolve()
            ?? throw new RotationUnavailableException(
                "a chave de recuperação (Squid:RecoveryKey) não está disponível ou é inválida — nada para rotacionar.");
        var newKey = RootKey.Create();

        // 0009: every stored copy re-wrapped for the new chain — all in memory first, so a row the
        // current key will not open refuses the whole rotation instead of half-applying it.
        var storedCopies = await copies.ListAllAsync(ct);
        var rewrapped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (handle, wrapped) in storedCopies)
        {
            using var rsa = KeyDerivation.UnwrapRsaPrivateKey(oldKey, Convert.FromBase64String(wrapped));
            rewrapped[handle] = Convert.ToBase64String(KeyDerivation.WrapRsaPrivateKey(newKey, rsa));
        }

        // The keyring every epoch travels in: the persisted entries (wrapped under the current
        // key), whatever configuration already carries, the current key at its epoch, then the new
        // one — the reissued kit ships all of them (R-20: an old envelope keeps opening).
        var keyring = new Dictionary<int, RootKey>();
        var storedKit = await kits.FindAsync(ct);
        if (storedKit is not null)
        {
            foreach (var entry in storedKit.Keyring)
            {
                keyring[entry.Epoch] = RootKey.FromBytes(
                    KeyDerivation.OpenKitKey(oldKey, Convert.FromBase64String(entry.WrappedKey)));
            }
        }

        foreach (var (epoch, key) in keys.ResolveRing())
        {
            keyring.TryAdd(epoch, key);
        }

        var ledgerEpoch = await epochs.CurrentEpochAsync(ct);
        var currentEpoch = Math.Max(ledgerEpoch, keyring.Count > 0 ? keyring.Keys.Max() : 1);
        // The configured key IS the current epoch's material: a wiped ledger (disaster) restarts
        // its numbering while the keyring keeps the real epochs — this is where they reconcile.
        keyring[currentEpoch] = oldKey;
        var newEpoch = currentEpoch + 1;
        keyring[newEpoch] = newKey;

        // Fresh credential, independent of the `RK` (doc 05 §5.1), and the sealed root key only
        // that credential opens — the Argon2id password of this kit is the credential itself,
        // because the rotation body carries nothing else to seal under (SPEC-0012 inputs: vazio).
        var credential = RandomNumberGenerator.GetBytes(KeyState.RecoveryCodeSizeInBytes);
        var credentialText = RecoveryCredential.Format(credential);
        var sealedRootKey = Convert.ToBase64String(RootKeyCipher.Encrypt(newKey, credentialText));
        var at = DateTimeOffset.UtcNow;

        // Writes first, in-memory swap last (the swap cannot fail): ledger, copies, kit document.
        // shortcut: no transaction spans the three stores — a crash between them is fixed by
        // restoring the database; each single write is atomic on its own.
        await epochs.AdvanceAsync(
            currentEpoch, Fingerprint(oldKey), newEpoch, Fingerprint(newKey), at, ct);
        await copies.SaveManyAsync(rewrapped, ct);
        await kits.SaveAsync(new InstanceRecoveryKit(
            Convert.ToBase64String(SHA256.HashData(credential)),
            keyring
                .OrderBy(pair => pair.Key)
                .Select(pair => new InstanceKitKey(
                    pair.Key,
                    Convert.ToBase64String(KeyDerivation.WrapKitKey(newKey, pair.Value.AsSpan()))))
                .ToList()), ct);

        ring.RotateTo(newKey);
        keys.RotateTo(newKey, keyring);

        var ordered = keyring
            .OrderBy(pair => pair.Key)
            .Select(pair => new RotationKey(pair.Key, Convert.ToBase64String(pair.Value.AsSpan())))
            .ToList();
        return new RotatedInstanceKit(
            newEpoch, credentialText, Mnemonic.FromRecoveryCode(credential), sealedRootKey, at, ordered);
    }

    /// <summary>
    /// data-model 2.10's <c>PublicFingerprint</c>: SHA-256 of the key, prefixed like the registry's
    /// content hash. It identifies the material without exposing it — inverting 256 CSPRNG bits is
    /// not an attack, and the same database already holds the wrapped form.
    /// </summary>
    private static string Fingerprint(RootKey key) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(key.AsSpan()));
}
