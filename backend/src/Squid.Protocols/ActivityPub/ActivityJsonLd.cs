using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Squid.Protocols.ActivityPub;

/// <summary>
/// The inbound half of the adapter (SPEC-0011 section E, contract activitypub §3 step 4, R-13):
/// the body of an inbox <c>POST</c> decoded into the few fields the pipeline acts on, every
/// unknown member dropped here and preserved by the caller through the raw body it still holds —
/// leitura tolerante, escrita estrita. The depth ceiling of contract §3 (32) is enforced while
/// parsing: a deeper document is <see cref="PayloadTooDeepException"/>, anything that is not JSON
/// at all is <see cref="FormatException"/>, and both reach the pipeline as a <c>400</c> without
/// the process ever falling (FR-040). Pure functions over bytes — no I/O, no network, JSON-LD
/// stays on this side of the boundary (ADR-11).
/// </summary>
public static class ActivityJsonLd
{
    /// <summary>The nesting ceiling of contract §3 step 4 (SPEC-0011 section C).</summary>
    public const int MaxDepth = 32;

    /// <summary>The one ActivityStreams context every outbound document embeds (contract §2.1).</summary>
    public const string ActivityStreamsContext = "https://www.w3.org/ns/activitystreams";

    /// <summary>What counts as a public audience when a payload spells no visibility (contract §4).</summary>
    public const string PublicCollection = "https://www.w3.org/ns/activitystreams#Public";

    /// <summary>
    /// Decodes <paramref name="body"/> as one activity: at most <see cref="MaxDepth"/> levels of
    /// nesting, tolerant of every shape the contract accepts (string or object <c>actor</c>, string
    /// or array <c>to</c>/<c>cc</c>/<c>audience</c>, inline or referenced <c>object</c>).
    /// </summary>
    /// <exception cref="PayloadTooDeepException">the document nests past §3's ceiling.</exception>
    /// <exception cref="FormatException">the bytes are not a JSON activity at all.</exception>
    public static async ValueTask<IncomingActivity> ReceiveAsync(
        byte[] body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        JsonDocument document;
        try
        {
            document = await ParseAsync(body, MaxDepth, cancellationToken);
        }
        catch (JsonException)
        {
            // Deeper than the ceiling, or not JSON: parsing again with a generous depth tells the
            // two apart — a document that then parses was refused for its depth alone (§3 step 4),
            // anything else is malformed. Past 1024 levels the second parse also gives up, which
            // still answers §3's 400; only the label of the refusal is coarser there.
            try
            {
                using var probe = await ParseAsync(body, 1024, cancellationToken);
            }
            catch (JsonException)
            {
                throw new FormatException("the inbox body is not a JSON document");
            }

            throw new PayloadTooDeepException();
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("the inbox body is not an activity object");
            }

            return new IncomingActivity(
                Text(root, "id"),
                Text(root, "type"),
                ActorOf(root),
                ObjectUriOf(root),
                root.TryGetProperty("object", out var inline) && inline.ValueKind == JsonValueKind.Object
                    ? ObjectOf(inline)
                    : null,
                List(root, "to"),
                List(root, "cc"),
                List(root, "audience"),
                Text(root, "inReplyTo"),
                Text(root, "squid:slug"),
                VisibilityOf(root),
                Text(root, "summary") ?? Text(root, "content"));
        }
    }

    /// <summary>
    /// The <c>Accept</c> an incoming <c>Follow</c> is answered with (SPEC-0011 section E, AC-05 of
    /// TASK-0044): the local actor accepts, the original Follow rides verbatim as <c>object</c>,
    /// and the id is this instance's canonical activity URL (contract §2.1 spelling — minted here,
    /// never fetched).
    /// </summary>
    public static string Accept(string origin, string localActorUri, string followJson)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(localActorUri);
        ArgumentNullException.ThrowIfNull(followJson);

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        return JsonSerializer.Serialize(new AcceptDocument(
            ActivityStreamsContext,
            $"{origin}/activities/{token}",
            "Accept",
            localActorUri,
            JsonSerializer.Deserialize<JsonElement>(followJson)));
    }

    private static async Task<JsonDocument> ParseAsync(byte[] body, int maxDepth, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(body, writable: false);
        return await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { MaxDepth = maxDepth, CommentHandling = JsonCommentHandling.Skip },
            cancellationToken);
    }

    /// <summary>The inline object of an activity — a Note, or the nested activity of Undo/Accept/Follow.</summary>
    private static IncomingObject ObjectOf(JsonElement element) => new(
        Text(element, "id"),
        Text(element, "type"),
        IdOf(element, "attributedTo"),
        IdOf(element, "actor"),
        NestedObjectUri(element),
        Text(element, "content"),
        Text(element, "summary") ?? Text(element, "name"),
        Text(element, "published"),
        List(element, "to"),
        List(element, "cc"),
        List(element, "audience"),
        Text(element, "inReplyTo"),
        Text(element, "squid:slug"),
        VisibilityOf(element),
        Attachments(element));

    /// <summary>The <c>object</c> of an activity when it is a reference: a URI, or the first URI of an array (Flag).</summary>
    private static string? ObjectUriOf(JsonElement root)
    {
        if (!root.TryGetProperty("object", out var element)) return null;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => element.EnumerateArray()
                .FirstOrDefault(item => item.ValueKind == JsonValueKind.String).GetString(),
            _ => null,
        };
    }

    /// <summary>The <c>object</c> of a nested activity (the Follow inside an Undo, the one inside an Accept).</summary>
    private static string? NestedObjectUri(JsonElement element) =>
        element.TryGetProperty("object", out var nested) && nested.ValueKind == JsonValueKind.String
            ? nested.GetString()
            : null;

    /// <summary>The <c>actor</c>: a plain URI, or an object carrying one — never a Link this MVP does not speak.</summary>
    private static string? ActorOf(JsonElement root)
    {
        if (!root.TryGetProperty("actor", out var element)) return null;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Object => Text(element, "id"),
            _ => null,
        };
    }

    /// <summary><c>attributedTo</c> in either spelling (contract §2.1 always carries a URI).</summary>
    private static string? IdOf(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? Text(value, "id")
            : Text(element, property);

    /// <summary>The declared policy visibility of <c>squid:policy</c> (contract §1.2), absent when the payload spells none.</summary>
    private static string? VisibilityOf(JsonElement element) =>
        element.TryGetProperty("squid:policy", out var policy)
        && policy.ValueKind == JsonValueKind.Object
            ? Text(policy, "visibility")
            : null;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>An <c>to</c>/<c>cc</c>/<c>audience</c> in either spelling — one URI or a list of them.</summary>
    private static IReadOnlyList<string> List(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return [];

        return value.ValueKind switch
        {
            JsonValueKind.String => new[] { value.GetString()! },
            JsonValueKind.Array => value.EnumerateArray()
                .Select(item => item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Object => Text(item, "id"),
                    _ => null,
                })
                .Where(item => !string.IsNullOrEmpty(item))
                .Select(item => item!)
                .ToList(),
            _ => [],
        };
    }

    private static IReadOnlyList<IncomingAttachment> Attachments(JsonElement element)
    {
        if (!element.TryGetProperty("attachment", out var value)) return [];

        IEnumerable<JsonElement> items = value.ValueKind switch
        {
            JsonValueKind.Object => [value],
            JsonValueKind.Array => value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object),
            _ => [],
        };

        return items.Select(item => new IncomingAttachment(
            Text(item, "type"),
            Text(item, "mediaType"),
            Text(item, "url") ?? Text(item, "href"),
            Text(item, "name") ?? Text(item, "description") ?? Text(item, "summary"),
            Text(item, "sha256"))).ToList();
    }

    private sealed record AcceptDocument(
        [property: JsonPropertyName("@context")] string Context,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("actor")] string Actor,
        [property: JsonPropertyName("object")] JsonElement? Object);
}

/// <summary>The inbox activity under application — only the members the pipeline acts on (contract §2, §3).</summary>
/// <param name="Id">The dedupe key of §3 step 6; null when the payload carries no id at all.</param>
/// <param name="Type">The activity word; null is an unknown type and never an error (contract §2).</param>
/// <param name="Actor">The sender as the payload spells it.</param>
/// <param name="ObjectUri">The referenced target: a URI, or the first URI of a Flag's list.</param>
/// <param name="Object">The inline object document, when <c>object</c> is a document instead of a reference.</param>
/// <param name="Summary">The activity-level <c>summary</c>/<c>content</c> — the reason a Flag carries.</param>
public sealed record IncomingActivity(
    string? Id,
    string? Type,
    string? Actor,
    string? ObjectUri,
    IncomingObject? Object,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Audience,
    string? InReplyTo,
    string? Slug,
    string? Visibility,
    string? Summary);

/// <summary>
/// The inline <c>object</c> of an activity: the Note of a Create, or the nested activity an
/// Undo/Accept carries. Every field is optional — leitura tolerante (R-13) — and the pipeline
/// decides which of them it needs.
/// </summary>
public sealed record IncomingObject(
    string? Id,
    string? Type,
    string? AttributedTo,
    string? Actor,
    string? ObjectUri,
    string? Content,
    string? Summary,
    string? Published,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Audience,
    string? InReplyTo,
    string? Slug,
    string? Visibility,
    IReadOnlyList<IncomingAttachment> Attachments);

/// <summary>One <c>attachment</c> of a remote Note — url, media type, alt text and the declared digest (GAP-44).</summary>
public sealed record IncomingAttachment(
    string? Type,
    string? MediaType,
    string? Url,
    string? Alt,
    string? Sha256);

/// <summary>An activity that nests past contract §3's ceiling — the pipeline answers §3's 400 with it.</summary>
public sealed class PayloadTooDeepException : Exception
{
    /// <summary>Builds the refusal; the message is fixed so no payload fragment can reach a log.</summary>
    public PayloadTooDeepException()
        : base("the activity nests past the depth ceiling of contract §3")
    {
    }
}
