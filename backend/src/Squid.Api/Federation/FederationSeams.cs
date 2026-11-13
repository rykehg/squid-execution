using Microsoft.AspNetCore.Http;
using Squid.Domain.Containers;
using Squid.Domain.Objects;
using Squid.Grains.Actors;

namespace Squid.Api.Federation;

/// <summary>
/// The seams the ActivityPub surface runs on (TASK-0043, the pattern <c>MapSquidDiscovery</c> was
/// born with in TASK-0015): <c>Squid.Api</c> owns the routes, the ladders and the paging, and the
/// document bytes plus the signature check come from <c>Squid.Protocols</c> through the delegates
/// the composition root hands in — this assembly references no adapter (its .csproj is outside
/// this task's touches) and ADR-11 keeps JSON-LD on the protocol side of the boundary.
///
/// The signatures are the wire contracts of those delegates:
/// <list type="bullet">
/// <item><description><c>Group(container, origin)</c> → the <c>Group</c> document of contract §1.2;</description></item>
/// <item><description><c>Note(post, container, origin)</c> → the <c>Note</c> document of §2.1;</description></item>
/// <item><description><c>Collection(container, id, total, first, next)</c> → the <c>OrderedCollection</c>
/// of §1, the container null for an actor's own collections and present (so the three
/// <c>squid:</c> fields of AC-03 ride) for the container's;</description></item>
/// <item><description><c>Page(container, id, partOf, items, next)</c> → the <c>OrderedCollectionPage</c>,
/// items already serialized, same container rule as above;</description></item>
/// <item><description><c>VerifyInbox(context, actors)</c> → true only for a request whose signature
/// verifies against a key this instance publishes (contract §3 step 1).</description></item>
/// </list>
/// </summary>
public sealed record FederationSeams(
    Func<Container, string, string> Group,
    Func<PostObject, Container, string, string> Note,
    Func<Container?, string, long, string, string?, string> Collection,
    Func<Container?, string, string, IReadOnlyList<string>, string?, string> Page,
    Func<HttpContext, ActorGrain, Task<bool>> VerifyInbox);
