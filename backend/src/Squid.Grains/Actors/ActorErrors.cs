namespace Squid.Grains.Actors;

/// <summary>
/// Typed rejections of the actor aggregate: the API maps each one to its own status code
/// (422 citing <see cref="ActorHandle.Rule"/>, 409 with a clear message, 404).
/// </summary>
public sealed class InvalidActorHandleException(string handle)
    : Exception($"handle '{handle}' does not match {ActorHandle.Rule}")
{
    /// <summary>The handle that was refused.</summary>
    public string Handle { get; } = handle;
}

/// <summary>The handle is already taken by another actor: nothing was persisted (409, clear message).</summary>
public sealed class ActorHandleTakenException(string handle)
    : Exception($"handle '{handle}' is already taken")
{
    /// <summary>The handle that was refused.</summary>
    public string Handle { get; } = handle;
}

/// <summary>No actor exists for the handle — a missing actor is never created on the side.</summary>
public sealed class ActorNotFoundException(string handle)
    : Exception($"no actor exists for handle '{handle}'")
{
    /// <summary>The handle that was looked up.</summary>
    public string Handle { get; } = handle;
}

/// <summary>
/// The store refused the write: the ETag it holds differs from the expected one, so nothing was
/// written (compare-and-swap of the SQLite IGrainStorage, TASK-0003).
/// </summary>
public sealed class ActorStoreConflictException(string handle, Exception? inner = null)
    : Exception($"store conflict for handle '{handle}': the stored version differs from the expected one", inner)
{
    /// <summary>The handle whose row was not written.</summary>
    public string Handle { get; } = handle;
}
