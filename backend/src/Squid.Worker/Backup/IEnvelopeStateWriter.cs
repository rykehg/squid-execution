namespace Squid.Worker.Backup;

/// <summary>
/// The destination-side half of SPEC-0012 section A's "grava <c>verifiedAt</c>": the header the
/// destination stores is written by <c>PutAsync</c> BEFORE the re-read can possibly know the
/// result, so the pipeline comes back through this seam once the hash has been confirmed and
/// re-serializes the metadata with the decided state. Only the metadata moves — the ciphertext is
/// never sent twice. A destination that does not offer the seam simply keeps the pre-verification
/// header; the registry (<c>backup_envelope.verified_at</c>) is the state of record either way.
/// </summary>
public interface IEnvelopeStateWriter
{
    /// <summary>Rewrites the stored envelope metadata of <paramref name="envelope"/> as it stands now.</summary>
    Task WriteEnvelopeStateAsync(BackupEnvelope envelope, CancellationToken ct);
}
