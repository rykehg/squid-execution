using Squid.Api.Containers;
using Squid.Domain.Objects;

namespace Squid.Api.Threads;

/// <summary>
/// The wire shape of a message in SPEC-0010 section B / contract §6: <c>{id, seq, content,
/// replyToId, attachments, reactions}</c> — the id is the <c>/m/{token}</c> URI on the origin of
/// the request that sent it (the P-03 emenda rule the thread id follows), the seq is the total
/// order of FR-030, and the content is the sanitized text exactly as stored, so what a reader sees
/// is what the server kept (GAP-24). <c>replyToId</c> is FR-057's reference (section E): null when
/// the message answers nothing and otherwise always a message of the SAME thread — the shape carries
/// it the same way <c>PostView</c> carries its own, so a reader sees the reference only through the
/// wall it already crosses. <c>attachments</c> is TASK-0039's section G (GAP-35 emenda (b)): the
/// §2.6 list the send froze from <c>contentHashes</c>, always present and empty when the message
/// carries none — the SAME fields <c>PostView</c> spells, so a client reads one attachment shape
/// whether it came from a post or from a conversation. <c>reactions</c> is SPEC-0010 section F's
/// derived projection (<c>{emoji, count, mine}</c>): empty when no row exists, and only ever
/// assembled AFTER the participant probe of the caller — a non-participant never receives this
/// shape at all, because the route refuses with the 404 first (§9.10, AC-04).
/// </summary>
public static class MessageView
{
    /// <summary>One message of the delta (and the body <c>POST .../messages</c> answers with — 200 + seq).</summary>
    /// <param name="message">The stored row.</param>
    /// <param name="reactions">
    /// The rows of <c>message_reaction</c> this reader may see for the message, already counted —
    /// null or empty means no reaction exists, and a freshly sent message always answers with none.
    /// </param>
    public static object Of(MessageState message, IReadOnlyList<ReactionCount>? reactions = null) => new
    {
        id = message.Id,
        seq = message.Seq,
        content = message.Content,
        replyToId = message.ReplyToId,
        attachments = message.Attachments.Select(AttachmentView).ToList(),
        reactions = (reactions ?? []).Select(ReactionView.Of).ToList(),
    };

    /// <summary>One attachment as §2.6 spells it on the wire — the <c>PostView</c> shape, word for word.</summary>
    private static object AttachmentView(Attachment attachment) => new
    {
        kind = ContainerView.Word(attachment.Kind),
        contentHash = attachment.ContentHash,
        mimeType = attachment.MimeType,
        url = attachment.Url,
        blurHash = attachment.BlurHash,
        alt = attachment.Alt,
        width = attachment.Width,
        height = attachment.Height,
        durationSeconds = attachment.DurationSeconds,
        sizeBytes = attachment.SizeBytes,
    };
}

/// <summary>The wire entry of one derived reaction count — contract §6's <c>reactions</c> item.</summary>
public static class ReactionView
{
    public static object Of(ReactionCount reaction) => new
    {
        emoji = reaction.Emoji,
        count = reaction.Count,
        mine = reaction.Mine,
    };
}
