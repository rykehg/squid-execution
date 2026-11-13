using System.Text.Json;
using System.Text.Json.Serialization;
using Squid.Domain.Containers;

namespace Squid.Protocols.ActivityPub;

/// <summary>
/// Canonical ↔ JSON-LD mapping of the collections of contract activitypub §1 (SPEC-0011 section D,
/// TASK-0043): an <c>OrderedCollection</c> carries <c>first</c>/<c>next</c> and the count, and
/// NEVER the items themselves — the items live on an <c>OrderedCollectionPage</c> behind those
/// links ("nunca o conjunto inteiro", AC-02). A collection that belongs to a container also names
/// that container with the three <c>squid:</c> fields of AC-03, so the outbox and the members page
/// say whose they are; an actor's own collections carry none. Pure functions over strings and
/// counts: no I/O, no query parsing, and the JSON-LD stays on this side of the boundary (ADR-11).
/// </summary>
public static class CollectionJsonLd
{
    /// <summary>
    /// The collection document itself: <paramref name="first"/> and <paramref name="next"/> are the
    /// absolute URLs of the pages, both always present — a caller that knows nothing else about the
    /// instance can still start at page 1 and walk forward. <paramref name="container"/> null means
    /// an actor's collection, where the <c>squid:</c> extension does not ride.
    /// </summary>
    public static string Collection(
        Container? container, string id, long totalItems, string first, string? next) =>
        JsonSerializer.Serialize(new CollectionDocument(
            ProfileJsonLd.ActivityStreamsContext, id, "OrderedCollection", totalItems, first, next,
            Kind(container), container?.Slug, Policy(container)));

    /// <summary>
    /// One page of <paramref name="id"/>: <paramref name="orderedItems"/> are ALREADY serialized
    /// documents (a Note of <see cref="ObjectJsonLd"/> or a bare actor URI), each re-read into a
    /// <see cref="JsonElement"/> so they embed verbatim instead of being quoted as strings;
    /// <paramref name="next"/> only rides when a later page actually exists.
    /// </summary>
    public static string Page(
        Container? container, string id, string partOf, IReadOnlyList<string> orderedItems, string? next)
    {
        ArgumentNullException.ThrowIfNull(orderedItems);

        var items = new JsonElement[orderedItems.Count];
        for (var index = 0; index < orderedItems.Count; index++)
        {
            using var document = JsonDocument.Parse(orderedItems[index]);
            items[index] = document.RootElement.Clone(); // detached from the document, safe to serialize after Dispose
        }

        return JsonSerializer.Serialize(new PageDocument(
            ProfileJsonLd.ActivityStreamsContext, id, "OrderedCollectionPage", partOf, items, next,
            Kind(container), container?.Slug, Policy(container)));
    }

    /// <summary>The container word of AC-03, absent when the collection belongs to an actor.</summary>
    private static string? Kind(Container? container) =>
        container is null ? null : ObjectJsonLd.Word(container.Kind);

    /// <summary>The policy of AC-03, absent when the collection belongs to an actor.</summary>
    private static PolicyDocument? Policy(Container? container) =>
        container is null ? null : ObjectJsonLd.Policy(container.Policy);
}

/// <summary>The collection half of contract §1: count plus the two page links, no items (AC-02).</summary>
internal sealed record CollectionDocument(
    [property: JsonPropertyName("@context")] string Context,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("totalItems")] long TotalItems,
    [property: JsonPropertyName("first")] string First,
    [property: JsonPropertyName("next")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Next,
    [property: JsonPropertyName("squid:kind")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind = null,
    [property: JsonPropertyName("squid:slug")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Slug = null,
    [property: JsonPropertyName("squid:policy")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PolicyDocument? Policy = null);

/// <summary>The page half: <c>partOf</c> points back to the collection, items are raw JSON documents.</summary>
internal sealed record PageDocument(
    [property: JsonPropertyName("@context")] string Context,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("partOf")] string PartOf,
    [property: JsonPropertyName("orderedItems")] JsonElement[] OrderedItems,
    [property: JsonPropertyName("next")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Next,
    [property: JsonPropertyName("squid:kind")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind = null,
    [property: JsonPropertyName("squid:slug")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Slug = null,
    [property: JsonPropertyName("squid:policy")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PolicyDocument? Policy = null);
