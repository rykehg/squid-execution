using System.Text.Json;

namespace Squid.Worker.Backup;

/// <summary>
/// <see cref="IBackupTarget"/> over the configured volume (<c>Backup:Local:Path</c>, SPEC-0012
/// section A): one envelope lives as <c>&lt;id&gt;.bin</c> — the ciphertext PutAsync received — plus
/// <c>&lt;id&gt;.json</c> — its envelope, the header the pipeline re-reads. The JSON is written LAST,
/// so a backup interrupted mid-write is invisible to <see cref="ListAsync"/>: discovery of "what
/// was already sent" (contract section 1) only ever sees envelopes whose ciphertext landed
/// completely. The destination is untrusted by design: it gets ciphertext and the wrapped-DK
/// header, never a key in clear (AC-03).
/// </summary>
public sealed class LocalDiskTarget : IBackupTarget
{
    private readonly string _path;

    /// <summary>Binds the target to the volume of <c>Backup:Local:Path</c>; the directory is created on first write.</summary>
    public LocalDiskTarget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <inheritdoc />
    public string TargetId => "local-disk";

    /// <inheritdoc />
    public async Task PutAsync(BackupEnvelope envelope, Stream ciphertext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(ciphertext);

        Directory.CreateDirectory(_path);

        // Ciphertext first, envelope second: the JSON is the commit marker of a complete backup.
        await using (var file = new FileStream(
            PathOf(envelope.Id, ".bin"), FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true))
        {
            await ciphertext.CopyToAsync(file, ct);
        }

        await File.WriteAllTextAsync(
            PathOf(envelope.Id, ".json"),
            JsonSerializer.Serialize(envelope, BackupEnvelope.JsonOptions),
            ct);
    }

    /// <inheritdoc />
    public Task<Stream> GetAsync(string envelopeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            PathOf(envelopeId, ".bin"), FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupEnvelope>> ListAsync(long fromSeq, CancellationToken ct)
    {
        if (!Directory.Exists(_path))
        {
            return [];
        }

        var found = new List<BackupEnvelope>();
        foreach (var file in Directory.EnumerateFiles(_path, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var envelope = JsonSerializer.Deserialize<BackupEnvelope>(
                    await File.ReadAllTextAsync(file, ct), BackupEnvelope.JsonOptions)
                ?? throw new InvalidOperationException($"backup envelope '{Path.GetFileName(file)}' has no content");
            if (envelope.SeqTo >= fromSeq)
            {
                found.Add(envelope);
            }
        }

        // Restore takes envelopes in order of seqTo (contract section 4), so the destination
        // already hands them over in that order.
        return found.OrderBy(envelope => envelope.SeqTo).ThenBy(envelope => envelope.Id, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string envelopeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Both halves: an envelope whose ciphertext went missing is NOT a stored backup.
        return Task.FromResult(
            File.Exists(PathOf(envelopeId, ".json")) && File.Exists(PathOf(envelopeId, ".bin")));
    }

    /// <inheritdoc />
    public Task DeleteAsync(string envelopeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // File.Delete is a no-op for a missing file: rotation/purge is idempotent.
        File.Delete(PathOf(envelopeId, ".bin"));
        File.Delete(PathOf(envelopeId, ".json"));
        return Task.CompletedTask;
    }

    /// <summary>The file of one envelope under the volume — an id is a plain name, never a path.</summary>
    private string PathOf(string envelopeId, string extension)
    {
        if (string.IsNullOrWhiteSpace(envelopeId) || Path.GetFileName(envelopeId) != envelopeId)
        {
            throw new ArgumentException(
                "an envelope id must be a plain file name inside the volume", nameof(envelopeId));
        }

        return Path.Combine(_path, envelopeId + extension);
    }
}
