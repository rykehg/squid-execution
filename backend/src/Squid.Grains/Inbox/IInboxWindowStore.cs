namespace Squid.Grains.Inbox;

/// <summary>
/// The dedupe port of the inbox window (contract activitypub §3 step 6): one bounded list of
/// already-seen <c>activity.id</c> per local recipient, persisted by <c>Squid.Service</c> (ADR-35:
/// this project declares the port, it never learns where the document lives).
/// </summary>
public interface IInboxWindowStore
{
    /// <summary>
    /// True when <paramref name="activityId"/> is a NEW id for <paramref name="recipient"/> and the
    /// window now remembers it; false when the window already held it — the caller answers
    /// <c>202</c> without reprocessing either way, only the return value says which happened.
    /// </summary>
    Task<bool> TryRememberAsync(string recipient, string activityId, int capacity);
}
