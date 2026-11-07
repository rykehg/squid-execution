namespace Squid.Domain;

/// <summary>Who may post — data-model §2.3 (GAP-12: these names prevail over doc 05's lowercase table).</summary>
public enum PostingPolicy
{
    Everyone,
    Members,
    Moderators,
    Admins,
    SinglePublisher,
}

/// <summary>Who may see the container — data-model §2.3.</summary>
public enum VisibilityPolicy
{
    Public,
    Unlisted,
    MembersOnly,
    Private,
}

/// <summary>How one gets in — data-model §2.3, FR-014.</summary>
public enum JoinPolicy
{
    Open,
    Request,
    Invite,
    Closed,
}

/// <summary>
/// The four fine-grained interaction gates (comment, react, share, attach), each one in the
/// <see cref="PostingPolicy"/> domain — data-model §2.3.
/// </summary>
public sealed record InteractionPolicy(
    PostingPolicy CanComment = PostingPolicy.Members,
    PostingPolicy CanReact = PostingPolicy.Members,
    PostingPolicy CanShare = PostingPolicy.Members,
    PostingPolicy CanAttach = PostingPolicy.Members);

/// <summary>
/// Versioned access policy of one container — doc 05 §2.4, data-model §2.3. An immutable record
/// whose constructor validates every value, so an invalid policy never exists to be evaluated:
/// <c>SinglePublisher</c> without publishers is rejected at construction (FR-015, AC-02), and an
/// enum outside its domain is an argument error, never a silent default.
/// </summary>
public sealed record ContainerPolicy
{
    public PostingPolicy Posting { get; }

    public VisibilityPolicy Visibility { get; }

    public JoinPolicy Join { get; }

    public InteractionPolicy Interaction { get; }

    public IReadOnlyList<string> PublisherIds { get; }

    public int Version { get; }

    public ContainerPolicy(
        PostingPolicy posting,
        VisibilityPolicy visibility,
        JoinPolicy join,
        InteractionPolicy interaction,
        IReadOnlyList<string> publisherIds,
        int version)
    {
        if (!Enum.IsDefined(posting))
        {
            throw new ArgumentOutOfRangeException(nameof(posting), posting, "posting policy outside its domain");
        }

        if (!Enum.IsDefined(visibility))
        {
            throw new ArgumentOutOfRangeException(nameof(visibility), visibility, "visibility policy outside its domain");
        }

        if (!Enum.IsDefined(join))
        {
            throw new ArgumentOutOfRangeException(nameof(join), join, "join policy outside its domain");
        }

        ArgumentNullException.ThrowIfNull(interaction);
        if (!Enum.IsDefined(interaction.CanComment)
            || !Enum.IsDefined(interaction.CanReact)
            || !Enum.IsDefined(interaction.CanShare)
            || !Enum.IsDefined(interaction.CanAttach))
        {
            throw new ArgumentException("interaction gate outside the posting policy domain", nameof(interaction));
        }

        ArgumentNullException.ThrowIfNull(publisherIds);
        if (posting == PostingPolicy.SinglePublisher && publisherIds.Count == 0)
        {
            throw new ArgumentException("SinglePublisher needs at least one publisher id (FR-015)", nameof(publisherIds));
        }

        Posting = posting;
        Visibility = visibility;
        Join = join;
        Interaction = interaction;
        PublisherIds = publisherIds.ToArray();
        Version = version;
    }

    /// <summary>
    /// Group preset (friends): members-only visibility, invite-only entry — doc 05 §2.3 defaults,
    /// postagem padrão membersOnly (FR-010). Discovery (não listado) belongs to the container
    /// aggregate, not to this record — FR-016 is P-03.
    /// </summary>
    public static ContainerPolicy Group() =>
        new(PostingPolicy.Members, VisibilityPolicy.MembersOnly, JoinPolicy.Invite, new InteractionPolicy(), [], 1);

    /// <summary>
    /// Community preset (interests): public visibility, entry open or request — doc 05 §2.3
    /// defaults, postagem padrão membersOnly (FR-010). Discovery (listada) belongs to the container
    /// aggregate, not to this record — FR-016 is P-03.
    /// </summary>
    public static ContainerPolicy Community(JoinPolicy join = JoinPolicy.Open) =>
        new(PostingPolicy.Members, VisibilityPolicy.Public, join, new InteractionPolicy(), [], 1);

    /// <summary>The next version: numbers only grow and are never reused (data-model §2.3).</summary>
    public ContainerPolicy NextVersion() =>
        new(Posting, Visibility, Join, Interaction, PublisherIds, Version + 1);
}
