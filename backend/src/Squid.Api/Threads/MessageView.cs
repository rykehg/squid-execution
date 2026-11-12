namespace Squid.Api.Threads;

/// <summary>
/// The wire shape of a message in SPEC-0010 section B / contract §6: <c>{id, seq, content}</c> —
/// the id is the <c>/m/{token}</c> URI on the origin of the request that sent it (the P-03 emenda
/// rule the thread id follows), the seq is the total order of FR-030, and the content is the
/// sanitized text exactly as stored, so what a reader sees is what the server kept (GAP-24).
/// <c>replyToId</c>, attachments and reactions join this shape with TASK-0037/0039/0038.
/// </summary>
public static class MessageView
{
    /// <summary>One message of the delta (and the body <c>POST .../messages</c> answers with — 200 + seq).</summary>
    public static object Of(MessageState message) => new
    {
        id = message.Id,
        seq = message.Seq,
        content = message.Content,
    };
}
