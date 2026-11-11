using Squid.Api.Containers;
using Squid.Domain.Objects;

namespace Squid.Api.Posts;

/// <summary>
/// The wire shape of a post: SPEC-0009 outputs (<c>PostView</c>) plus the fields AC-01 freezes
/// at publishing — <c>id</c>, <c>seq</c>, <c>policyVersion</c> and <c>visibilityAtPublish</c> —
/// and the <c>attachments</c> of the media slice (data-model §2.6, always present, empty for a
/// text-only post). Enums travel as the contract's camelCase words through
/// <see cref="ContainerView.Word"/>, never as the ordinals <c>Results.Json</c> would emit by default.
/// </summary>
public static class PostView
{
    /// <summary>The body of a successful publish, of <c>GET /api/posts/{id}</c> and of a reply.</summary>
    public static object Of(PostObject post) => new
    {
        id = post.Id,
        kind = ContainerView.Word(post.Kind),
        content = post.Content,
        author = post.AuthorActorId,
        containerId = post.ContainerId,
        seq = post.Seq,
        depth = post.Depth,
        replyToId = post.ReplyToId,
        policyVersion = post.PolicyVersion,
        visibilityAtPublish = ContainerView.Word(post.VisibilityAtPublish),
        attachments = post.Attachments.Select(AttachmentView).ToArray(),
        createdAt = post.CreatedAt,
    };

    /// <summary>One attachment as §2.6 spells it on the wire — kind as the camelCase word, metadata untouched.</summary>
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
