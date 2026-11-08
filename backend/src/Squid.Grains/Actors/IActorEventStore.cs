namespace Squid.Grains.Actors;

/// <summary>What a read hands back: the stream plus the ETag the next write must present.</summary>
public sealed record ActorStreamRecord(ActorEventStream Stream, string? ETag);

/// <summary>
/// Storage port of the actor stream. The grain layer may not reference the storage project
/// (ADR-35), so the production implementation wraps the SQLite <c>IGrainStorage</c> with ETag of
/// TASK-0003 at the composition root; the suite ships the same adapter under its tests.
/// </summary>
public interface IActorEventStore
{
    /// <summary>Reads the stream of <paramref name="handle"/>, or null when it was never written.</summary>
    Task<ActorStreamRecord?> ReadAsync(string handle);

    /// <summary>
    /// Writes <paramref name="stream"/> with compare-and-swap: a null <paramref name="expectedETag"/>
    /// only ever inserts, a non-null one only replaces the row still carrying that ETag, and a
    /// divergent one throws <see cref="ActorStoreConflictException"/> with nothing written.
    /// Returns the ETag of the row after the write.
    /// </summary>
    Task<string> WriteAsync(string handle, ActorEventStream stream, string? expectedETag);
}
