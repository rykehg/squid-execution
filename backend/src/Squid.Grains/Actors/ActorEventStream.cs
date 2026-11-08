using System.Text.Json.Serialization;

namespace Squid.Grains.Actors;

/// <summary>
/// Persisted document of one actor: one JSON row per handle in the SQLite <c>IGrainStorage</c> of
/// TASK-0003 (format decision of this task, registered in the task log — store <c>actors</c>, key
/// <c>actor/&lt;handle&gt;</c>). <see cref="Snapshot"/> is the folded state at
/// <see cref="SnapshotVersion"/>; <see cref="Events"/> carries only what happened after it, so
/// reaching the threshold writes the snapshot and truncates the log in the same row write
/// (doc 05 §4.7 #3 — the log stops containing the events the snapshot already covers).
/// </summary>
public sealed class ActorEventStream
{
    /// <summary>Folded state at the last snapshot; null until the threshold is reached once.</summary>
    public ActorState? Snapshot { get; set; }

    /// <summary>How many events <see cref="Snapshot"/> already covers (0 while there is none).</summary>
    public long SnapshotVersion { get; set; }

    /// <summary>Events after the snapshot, in order; emptied by every snapshot (the truncation).</summary>
    public List<ActorEvent> Events { get; set; } = [];

    /// <summary>Total events applied to this stream: snapshot coverage plus the open log.</summary>
    [JsonIgnore]
    public long Version => SnapshotVersion + Events.Count;
}
