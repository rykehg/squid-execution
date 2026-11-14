namespace Squid.Api.Posts;

/// <summary>
/// The like state of a federated post (SPEC-0011 section E, contract §7.14): one row per
/// <c>(post, actor)</c> — the idempotency key — counted into the <c>like_count</c> column the feed
/// already serves. ADR-35: the edge declares it, <c>Squid.Service</c> implements it as a keyed
/// document per post (pattern <c>SqliteActorStatusStore</c>): no table, no migration inside this
/// task, and the local <c>Like</c> route of a later phase adopts the same port.
/// </summary>
public interface ILikeStore
{
    /// <summary>
    /// Records <paramref name="actorUri"/>'s like of <paramref name="postId"/> — the same pair
    /// twice is one row and one count — and returns the post's total afterwards. The write also
    /// corrects the projection's <c>like_count</c>, so what the feed serves and what the store
    /// holds can never drift apart (shortcut: a like that arrives before the pump projected the
    /// post corrects itself on the next like — the column is rewritten from the rows every time).
    /// </summary>
    Task<long> LikeAsync(string postId, string actorUri, DateTimeOffset now);
}
