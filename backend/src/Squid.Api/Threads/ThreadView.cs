namespace Squid.Api.Threads;

/// <summary>
/// The wire shape of SPEC-0010's <c>GET /api/threads</c> output row — <c>{id, kind, participants,
/// messageSeq, lastReadSeq}</c> —, the same body <c>POST /api/threads</c> answers with. The id
/// carries the origin rule of the P-03 emenda (scheme + Host of the request that created it).
/// </summary>
public static class ThreadView
{
    /// <summary>One thread with the reader's own <c>lastReadSeq</c> (GAP-38: born 0).</summary>
    public static object Of(ThreadState thread, IReadOnlyList<string> participantActorIds, long lastReadSeq) => new
    {
        id = thread.Id,
        kind = thread.Kind,
        participants = participantActorIds,
        messageSeq = thread.MessageSeq,
        lastReadSeq,
    };

    /// <summary>The same row as the store's list shapes it.</summary>
    public static object Of(ThreadListItem item) => Of(item.Thread, item.ParticipantActorIds, item.LastReadSeq);
}
