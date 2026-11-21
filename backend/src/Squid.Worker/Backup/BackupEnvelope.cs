using System.Text.Json;
using System.Text.Json.Serialization;

namespace Squid.Worker.Backup;

/// <summary>
/// What one backup run ended as (SPEC-0012 section A): `Pending` until the target re-read is
/// compared, `Verified` when the hash matched, `Corrupt` when it did not — and a `Corrupt`
/// envelope is never used by restore (contract backup-recovery section 3).
/// </summary>
public enum BackupEnvelopeStatus
{
    Pending,
    Verified,
    Corrupt,
}

/// <summary>
/// The envelope of contract backup-recovery section 2 and data-model section 2.10: what the
/// target stores beside the ciphertext and what the registry row holds.
/// <list type="bullet">
/// <item><c>KeyEpoch</c> is mandatory — 2.10's invariant "nenhum envelope sem KeyEpoch valida".</item>
/// <item><c>ContentHash</c> is the SHA-256 of the CIPHERTEXT (contract section 2), so verifying
/// never needs a key.</item>
/// <item><c>Status</c>/<c>VerifiedAt</c> only ever become set after the re-read of the target
/// confirmed that hash (the fixed order of contract section 3).</item>
/// <item><c>WrappedDek</c> + <c>Nonce</c> are the header SPEC-0012 section A re-reads: the
/// per-backup `DK` wrapped by <c>HKDF(RK, "fed/backup-kek")</c> and the ChaCha20-Poly1305 nonce —
/// the destination sees them wrapped, never a key in clear (AC-03).</item>
/// </list>
/// </summary>
public sealed class BackupEnvelope
{
    /// <summary>The payload format version this pipeline writes; restore refuses an unknown one.</summary>
    public const int CurrentFormat = 1;

    public string Id { get; init; } = string.Empty;

    public int KeyEpoch { get; init; }

    public long SeqFrom { get; init; }

    public long SeqTo { get; init; }

    public string ContentHash { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public string TargetId { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public int Format { get; init; } = CurrentFormat;

    public BackupEnvelopeStatus Status { get; set; } = BackupEnvelopeStatus.Pending;

    /// <summary>The per-backup `DK`, base64 of the AEAD box — wrapped, never clear (AC-03).</summary>
    public string WrappedDek { get; init; } = string.Empty;

    /// <summary>The ChaCha20-Poly1305 nonce, base64.</summary>
    public string Nonce { get; init; } = string.Empty;

    /// <summary>
    /// The one JSON shape of this module: camelCase of contract section 2 (<c>id</c>,
    /// <c>keyEpoch</c>, …) with enums as their name, so the stored header reads like the contract.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
