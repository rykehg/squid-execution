using System.Text.Json;
using System.Text.Json.Serialization;
using Squid.Domain;
using Squid.Domain.Containers;
using Squid.Domain.Objects;

namespace Squid.Protocols.ActivityPub;

/// <summary>
/// Canonical ↔ JSON-LD mapping of the object and of the container (contract activitypub §1/§1.2,
/// SPEC-0011 section D, TASK-0043): the <c>Note</c> served at <c>/objects/{id}</c> and the
/// <c>Group</c> served at <c>/c/{slug}</c>, both under the fixed embedded <c>@context</c> and both
/// carrying <c>squid:kind</c>/<c>squid:slug</c>/<c>squid:policy</c> (AC-03). The policy published on
/// an object is the container policy in force where the object lives — one rendering of
/// data-model §2.3, so a container document and an object document can never spell it differently.
/// Pure functions over domain records: no I/O and no query parsing, JSON-LD stays on this side of
/// the boundary (ADR-11).
/// </summary>
public static class ObjectJsonLd
{
    /// <summary>The audience of a public publication (contract §2.1).</summary>
    private const string PublicCollection = "https://www.w3.org/ns/activitystreams#Public";

    /// <summary>The capabilities of contract §1.2 — what this instance lets a peer do to a group.</summary>
    private static readonly string[] Capabilities = ["post", "reply", "react", "share"];

    /// <summary>
    /// The <c>Note</c> of <paramref name="post"/> as contract §2.1 spells it: the canonical id of
    /// the row, the author, the sanitized content as stored (already escaped — never escaped a
    /// second time here), <c>to</c>/<c>cc</c> reflecting the visibility rule of §4, and the three
    /// <c>squid:</c> fields of AC-03 over <paramref name="container"/>'s own policy.
    /// </summary>
    public static string Note(PostObject post, Container container, string origin)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(container);

        var (to, cc) = Audience(post);
        return JsonSerializer.Serialize(new NoteDocument(
            Context(origin),
            post.Id,
            "Note",
            post.AuthorActorId,
            post.Content,
            Published(post),
            to,
            cc,
            Word(post.Kind),
            post.ContainerSlug,
            Policy(container.Policy),
            post.ReplyToId));
    }

    /// <summary>
    /// The outbound <c>Create</c> of SPEC-0011 section F (contract §2.1, TASK-0045): the activity
    /// envelope the fan-out sends per visibility — <paramref name="to"/>/<paramref name="cc"/> are
    /// the addressing the queue decided (§4 table), and <c>audience</c> names the container so the
    /// REMOTE side resolves the destination by D-01 (named destination only, SPEC-0011 section E —
    /// the GAP-48 amendment of AC-01). The inline object is the very Note the object route serves,
    /// same document, same <c>squid:slug</c>/<c>squid:policy</c> — one rendering of the row, never
    /// two — plus the <c>inReplyTo</c> a reply federates with (contract §7.15).
    /// </summary>
    public static string Create(
        string activityId, PostObject post, Container container, string origin,
        IReadOnlyList<string> to, IReadOnlyList<string> cc)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(cc);

        // One addressing, two spellings: the activity and its inline Note carry the SAME to/cc
        // (the §2.1 example mirrors them), so a reader that looks at either sees the same rule.
        var addressed = to.ToArray();
        var carbonCopies = cc.ToArray();

        var note = new NoteDocument(
            Context(origin),
            post.Id,
            "Note",
            post.AuthorActorId,
            post.Content,
            Published(post),
            addressed,
            carbonCopies,
            Word(post.Kind),
            post.ContainerSlug,
            Policy(container.Policy),
            post.ReplyToId);

        return JsonSerializer.Serialize(new CreateDocument(
            ProfileJsonLd.ActivityStreamsContext,
            activityId,
            "Create",
            post.AuthorActorId,
            Published(post),
            addressed,
            carbonCopies,
            container.Id,
            note));
    }

    /// <summary>
    /// The <c>Group</c> of contract §1.2: the container's own id and name plus the extension fields
    /// <c>squid:kind</c>/<c>squid:slug</c>/<c>squid:policy</c>/<c>squid:capabilities</c> and the
    /// three endpoints a peer posts to (AC-03, AC-05).
    /// </summary>
    public static string Group(Container container, string origin)
    {
        ArgumentNullException.ThrowIfNull(container);

        var id = container.Id;
        return JsonSerializer.Serialize(new GroupDocument(
            Context(origin),
            id,
            "Group",
            container.Name,
            container.Summary,
            Word(container.Kind),
            container.Slug,
            Policy(container.Policy),
            Capabilities,
            $"{id}/inbox",
            $"{id}/outbox",
            $"{id}/members"));
    }

    /// <summary>The <c>published</c> instant of a row on the wire — UTC, second precision (§2.1).</summary>
    private static string Published(PostObject post) =>
        post.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    /// <summary>
    /// The addressing of contract §4: <c>public</c> puts the Public collection in <c>to</c> and the
    /// author's followers in <c>cc</c>; <c>unlisted</c> addresses the followers only and never the
    /// Public collection; anything narrower never leaves this instance in clear, and the routes of
    /// this surface do not serve it (the object route refuses it, the collections filter it out).
    /// </summary>
    private static (string[] To, string[] Cc) Audience(PostObject post)
    {
        var followers = $"{post.AuthorActorId}/followers";
        return post.VisibilityAtPublish switch
        {
            VisibilityPolicy.Public => (new[] { PublicCollection }, new[] { followers }),
            VisibilityPolicy.Unlisted => (new[] { followers }, Array.Empty<string>()),
            _ => (Array.Empty<string>(), Array.Empty<string>()),
        };
    }

    /// <summary>The inline <c>@context</c> of §1.1: ActivityStreams plus this origin's squid namespace.</summary>
    private static object[] Context(string origin) =>
        [ProfileJsonLd.ActivityStreamsContext, new SquidNamespace($"{origin}/ns/squid#")];

    /// <summary>The contract's lowercase word for an enum value: <c>Community</c> → <c>community</c>.
    /// Internal for the container collections of <see cref="CollectionJsonLd"/>, which publish the
    /// same words on the three <c>squid:</c> fields (AC-03).</summary>
    internal static string Word<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>data-model §2.3 on the wire, in the shape of contract §1.2 (no internal field) —
    /// also the <c>squid:policy</c> the container collections publish (AC-03).</summary>
    internal static PolicyDocument Policy(ContainerPolicy policy) => new(
        Word(policy.Posting),
        Word(policy.Visibility),
        Word(policy.Join),
        new InteractionDocument(
            Word(policy.Interaction.CanComment),
            Word(policy.Interaction.CanReact),
            Word(policy.Interaction.CanShare),
            Word(policy.Interaction.CanAttach)),
        policy.Version);
}

/// <summary>The Note of contract §2.1 plus the three <c>squid:</c> extension fields of AC-03.</summary>
internal sealed record NoteDocument(
    [property: JsonPropertyName("@context")] object[] Context,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("attributedTo")] string AttributedTo,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("published")] string Published,
    [property: JsonPropertyName("to")] string[] To,
    [property: JsonPropertyName("cc")] string[] Cc,
    [property: JsonPropertyName("squid:kind")] string Kind,
    [property: JsonPropertyName("squid:slug")] string Slug,
    [property: JsonPropertyName("squid:policy")] PolicyDocument Policy,
    [property: JsonPropertyName("inReplyTo")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InReplyTo = null);

/// <summary>
/// The outbound <c>Create</c> of contract §2.1 (TASK-0045): the activity envelope around the Note
/// above, with <c>audience</c> naming the container — the named destination D-01 resolves on the
/// receiving side (GAP-48 amendment, AC-01).
/// </summary>
internal sealed record CreateDocument(
    [property: JsonPropertyName("@context")] string Context,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("published")] string Published,
    [property: JsonPropertyName("to")] string[] To,
    [property: JsonPropertyName("cc")] string[] Cc,
    [property: JsonPropertyName("audience")] string Audience,
    [property: JsonPropertyName("object")] NoteDocument Object);

/// <summary>The Group of contract §1.2 — the container extended for the federated surface.</summary>
internal sealed record GroupDocument(
    [property: JsonPropertyName("@context")] object[] Context,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("squid:kind")] string Kind,
    [property: JsonPropertyName("squid:slug")] string Slug,
    [property: JsonPropertyName("squid:policy")] PolicyDocument Policy,
    [property: JsonPropertyName("squid:capabilities")] string[] Capabilities,
    [property: JsonPropertyName("inbox")] string Inbox,
    [property: JsonPropertyName("outbox")] string Outbox,
    [property: JsonPropertyName("members")] string Members);

/// <summary>The policy half of contract §1.2, shared by the Group and the Note documents.</summary>
internal sealed record PolicyDocument(
    [property: JsonPropertyName("posting")] string Posting,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("join")] string Join,
    [property: JsonPropertyName("interaction")] InteractionDocument Interaction,
    [property: JsonPropertyName("version")] int Version);

/// <summary>The four interaction gates of data-model §2.3 (contract §1.2).</summary>
internal sealed record InteractionDocument(
    [property: JsonPropertyName("canComment")] string CanComment,
    [property: JsonPropertyName("canReact")] string CanReact,
    [property: JsonPropertyName("canShare")] string CanShare,
    [property: JsonPropertyName("canAttach")] string CanAttach);
