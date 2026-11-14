namespace Squid.Grains.Inbox;

/// <summary>
/// The idempotency window of SPEC-0011 section E (FR-035, contract activitypub §3 step 6): the
/// recent <c>activity.id</c>s of one local recipient, so a redelivered activity answers
/// <c>202</c> without touching state again (§7.4). Like <c>ActorGrain</c> it is a plain class on
/// purpose — <c>Squid.Grains</c> carries no Orleans package and may not gain one — the window's
/// document is read and written through <see cref="IInboxWindowStore"/>, whose adapter lives in
/// the composition root's own project.
/// </summary>
public sealed class InboxGrain
{
    /// <summary>Ids kept per recipient — the window of §3 step 6 ("janela de N ids").</summary>
    public const int DefaultCapacity = 1024;

    private readonly IInboxWindowStore _store;

    /// <summary>Binds the grain to the window store the composition root registered.</summary>
    public InboxGrain(IInboxWindowStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>
    /// Remembers <paramref name="activityId"/> for <paramref name="recipient"/> and reports whether
    /// it was new. The window marks the id BEFORE the activity is applied — the contract's own
    /// order puts idempotency at step 6, before the sender and the application behind him.
    /// </summary>
    public Task<bool> RememberAsync(string recipient, string activityId, int capacity = DefaultCapacity) =>
        _store.TryRememberAsync(recipient, activityId, capacity);
}
