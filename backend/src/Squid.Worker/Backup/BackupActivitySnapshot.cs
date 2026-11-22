using Squid.Api.Posts;

namespace Squid.Worker.Backup;

/// <summary>
/// What one backup run serializes (contract backup-recovery section 3): the instance it belongs
/// to and the activity window the envelope covers. It lives beside the pipeline that serializes
/// it — the Worker is the only serializer of the flow — so the restore side deserializes the very
/// same type and the two halves cannot drift apart on a payload shape nobody owns. The Worker
/// compresses and ciphers it; no key and no plaintext ever leaves.
/// </summary>
/// <param name="InstanceId">The instance the window belongs to (contract section 3, keyed per instanceId).</param>
/// <param name="SeqFrom">First sequence the window covers — the envelope's own <c>seqFrom</c>.</param>
/// <param name="SeqTo">Last sequence the window covers — the envelope's own <c>seqTo</c>.</param>
/// <param name="Events">The activity events of the window, in <c>Seq</c> order.</param>
public sealed record BackupActivitySnapshot(
    string InstanceId,
    long SeqFrom,
    long SeqTo,
    IReadOnlyList<ActivityEvent> Events);
