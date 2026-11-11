namespace Squid.ReadModels.Projections;

/// <summary>
/// Turns activity events into projection rows — data-model §4: tables derived from the event log,
/// updated off the write path (R-18). Implemented once, by <see cref="ActivityProjector"/>; the
/// interface exists so the pump's caller can wrap it (a failing projector is how the resume test
/// reproduces a crash mid-run) and so the composition root can register it without starting it.
/// </summary>
public interface IActivityProjector
{
    /// <summary>Writes the rows of one event. An event already projected leaves no second row.</summary>
    Task ProjectAsync(ActivityLogEntry activity);

    /// <summary>
    /// The global <c>Seq</c> already reflected in the projection tables — where a restarted pump
    /// resumes from (AC-04 of TASK-0024), 0 when nothing was ever projected.
    /// </summary>
    Task<long> LastProjectedSeqAsync();
}
