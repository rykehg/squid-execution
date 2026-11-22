namespace Squid.Grains.Backup;

/// <summary>
/// The aggregate side of contract backup-recovery section 3, declared here for the same reason
/// <c>IActorEventStore</c> is: ADR-35 keeps <c>Squid.Grains</c> free of storage and of the
/// composition roots, so the coordinator asks for the last consolidated sequence and for the
/// snapshot through a port whose implementation lives in the root that knows where the instance
/// state is. The snapshot itself is an opaque, JSON-serializable object — the Worker is the only
/// serializer of the flow (contract section 3: "o Worker serializa, nunca o scheduler").
/// </summary>
public interface IBackupStateSource
{
    /// <summary>
    /// The global sequence the instance has consolidated so far (R-15's "registro global de
    /// último seq consolidado") — <c>seqTo</c> of the next backup.
    /// </summary>
    Task<long> LastConsolidatedSeqAsync(string instanceId, CancellationToken ct);

    /// <summary>
    /// The state of the instance covering <paramref name="seqFrom"/>..<paramref name="seqTo"/>,
    /// asked by the coordinator right before the run so the payload is exactly the interval the
    /// envelope will claim (contract section 2: <c>seqFrom/seqTo</c> define the interval covered).
    /// </summary>
    Task<object> SnapshotAsync(string instanceId, long seqFrom, long seqTo, CancellationToken ct);
}
