using Squid.ReadModels.Projections;

namespace Squid.Tests.Projections;

/// <summary>
/// Test doubles behind <see cref="IActivityProjector"/>: the pump consumes through the interface,
/// so a test can watch the exact order it projected in (recording) and reproduce a failure in the
/// middle of a batch (failing) — the two halves of AC-04 of TASK-0024.
/// </summary>
internal sealed record RecordingProjector(IActivityProjector Inner) : IActivityProjector
{
    /// <summary>The global Seq of every event this projector was asked to project, in call order.</summary>
    public List<long> Seqs { get; } = [];

    /// <inheritdoc />
    public Task<long> LastProjectedSeqAsync() => Inner.LastProjectedSeqAsync();

    /// <inheritdoc />
    public Task ProjectAsync(ActivityLogEntry activity)
    {
        Seqs.Add(activity.Seq);
        return Inner.ProjectAsync(activity);
    }
}

/// <summary>
/// A projector that fails after the first event of a batch: the rows of the event before the
/// failure are committed (each event is one transaction), the rest of the batch never runs —
/// exactly the mid-run crash AC-04 resumes from.
/// </summary>
internal sealed record FailingProjector(IActivityProjector Inner, int EventsBeforeFailure) : IActivityProjector
{
    private int _projected;

    /// <inheritdoc />
    public Task<long> LastProjectedSeqAsync() => Inner.LastProjectedSeqAsync();

    /// <inheritdoc />
    public Task ProjectAsync(ActivityLogEntry activity)
    {
        if (_projected++ >= EventsBeforeFailure)
        {
            throw new InvalidOperationException("the projection failed in the middle of the batch (simulated)");
        }

        return Inner.ProjectAsync(activity);
    }
}
