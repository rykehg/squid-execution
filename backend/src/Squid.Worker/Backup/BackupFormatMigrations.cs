using System.Text.Json;
using Squid.Api.Posts;

namespace Squid.Worker.Backup;

/// <summary>
/// Contract backup-recovery section 4 step 5 — the payload <c>format</c> brought forward, or a
/// loud refusal: SPEC-0012 section A fixes that restore refuses an unknown format with an explicit
/// error, never a silent read of a shape it does not understand. Format 0 is the pre-window
/// writer: its payload carried only the instance and the events, and the interval the envelope
/// itself claims (section 2: <c>seqFrom</c>/<c>seqTo</c>) is what fills the window back in.
/// </summary>
public static class BackupFormatMigrations
{
    /// <summary>
    /// Decodes <paramref name="payload"/> of <paramref name="format"/> into the current snapshot
    /// shape; an older registered format is migrated, an unknown one fails with
    /// <see cref="RestoreFailure"/> (FR-053: the message carries no path, table or stack).
    /// </summary>
    public static BackupActivitySnapshot Migrate(int format, byte[] payload, BackupEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(envelope);

        if (format == BackupEnvelope.CurrentFormat)
        {
            return JsonSerializer.Deserialize<BackupActivitySnapshot>(payload, BackupEnvelope.JsonOptions)
                ?? throw new RestoreFailure("o payload do backup não pôde ser lido");
        }

        if (format == 0)
        {
            var legacy = JsonSerializer.Deserialize<LegacySnapshot>(payload, BackupEnvelope.JsonOptions)
                ?? throw new RestoreFailure("o payload no formato antigo não pôde ser lido");
            return new BackupActivitySnapshot(
                legacy.InstanceId,
                envelope.SeqFrom,
                envelope.SeqTo,
                legacy.Events ?? throw new RestoreFailure("o payload no formato antigo não traz os eventos"));
        }

        throw new RestoreFailure(
            $"formato de payload {format} incompatível com esta versão do restore");
    }

    /// <summary>The pre-window payload shape: the instance and the events, nothing else.</summary>
    private sealed record LegacySnapshot(string InstanceId, List<ActivityEvent>? Events);
}
