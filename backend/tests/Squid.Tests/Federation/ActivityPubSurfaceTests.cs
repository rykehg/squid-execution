using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orleans;
using Squid.Api.Crypto;
using Squid.Protocols.ActivityPub;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Federation;

/// <summary>
/// AC-01..AC-06 of TASK-0043, proved over HTTP against the host <see cref="SquidServiceHost"/>
/// builds: the actor document with the four endpoints S-26 left open, the three collections that
/// answer first/next and never the whole set, the object and the container with their
/// <c>squid:</c> fields and their pages, nodeinfo, the two inboxes refusing every message without
/// a verifiable signature with the state intact, and the Accept negotiation of contract §6.
/// </summary>
[Collection("ServiceHost")]
public sealed class ActivityPubSurfaceTests : IClassFixture<AuthHostFixture>
{
    private const string ActivityJson = "application/activity+json";

    private const string ActivityStreams = "https://www.w3.org/ns/activitystreams";

    private readonly AuthHostFixture _host;

    public ActivityPubSurfaceTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task The_actor_document_carries_both_keys_and_the_four_endpoints()
    {
        var handle = PostRoutes.Handle("ap01");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, PostRoutes.Password)).StatusCode);

        var response = await _host.Http.GetAsync($"/users/{handle}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ActivityJson, response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var document = json.RootElement;
        var actor = $"{Origin}/users/{handle}";

        // The RSA key is a single OBJECT, never an array — the denominator Friendica expects (§1.1).
        var publicKey = document.GetProperty("publicKey");
        Assert.Equal(JsonValueKind.Object, publicKey.ValueKind);
        Assert.Equal($"{actor}#main-key", publicKey.GetProperty("id").GetString());
        Assert.Equal(actor, publicKey.GetProperty("owner").GetString());

        Assert.Equal($"{actor}#ed25519", document.GetProperty("squid:ed25519Key").GetProperty("id").GetString());

        // S-26, closed here: the four links the contract §1.1 requires.
        Assert.Equal($"{actor}/inbox", document.GetProperty("inbox").GetString());
        Assert.Equal($"{actor}/outbox", document.GetProperty("outbox").GetString());
        Assert.Equal($"{actor}/followers", document.GetProperty("followers").GetString());
        Assert.Equal($"{actor}/following", document.GetProperty("following").GetString());
    }

    [Fact] // AC-02
    public async Task The_three_actor_collections_answer_first_and_next_without_the_items()
    {
        var (_, ownerHandle, _, postId) = await PublishAsync("ap02");

        foreach (var relation in new[] { "outbox", "followers", "following" })
        {
            var id = $"{Origin}/users/{ownerHandle}/{relation}";
            var response = await _host.Http.GetAsync(id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ActivityJson, response.Content.Headers.ContentType?.MediaType);

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var collection = json.RootElement;
            Assert.Equal("OrderedCollection", collection.GetProperty("type").GetString());
            Assert.Equal($"{id}?page=1", collection.GetProperty("first").GetString());
            Assert.Equal($"{id}?page=2", collection.GetProperty("next").GetString());
            Assert.False(
                collection.TryGetProperty("orderedItems", out _),
                $"{id} must never carry the whole set (AC-02)");

            // The first link resolves, and it answers for the very collection it came from.
            var pageResponse = await _host.Http.GetAsync(collection.GetProperty("first").GetString()!);
            Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
            using var pageJson = JsonDocument.Parse(await pageResponse.Content.ReadAsStringAsync());
            var page = pageJson.RootElement;
            Assert.Equal("OrderedCollectionPage", page.GetProperty("type").GetString());
            Assert.Equal(id, page.GetProperty("partOf").GetString());
            Assert.True(page.TryGetProperty("orderedItems", out var items));

            if (relation == "outbox")
            {
                // The publication above is on page 1, newest first — a page, not the log.
                Assert.Equal(
                    postId,
                    items.EnumerateArray().First().GetProperty("id").GetString());
            }
            else
            {
                Assert.Empty(items.EnumerateArray()); // the follow rows arrive with TASK-0044
            }
        }

        // An unknown actor answers the same 404 the profile answers — no collection leaks a name.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _host.Http.GetAsync($"{Origin}/users/{PostRoutes.Handle("ap02none")}/outbox")).StatusCode);

        // A page that is not a positive integer is refused; a page past the end is simply empty.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _host.Http.GetAsync($"{Origin}/users/{ownerHandle}/outbox?page=abc")).StatusCode);
        var beyond = await _host.Http.GetAsync($"{Origin}/users/{ownerHandle}/outbox?page=999");
        Assert.Equal(HttpStatusCode.OK, beyond.StatusCode);
        using var beyondJson = JsonDocument.Parse(await beyond.Content.ReadAsStringAsync());
        Assert.Empty(beyondJson.RootElement.GetProperty("orderedItems").EnumerateArray());
    }

    [Fact] // AC-03
    public async Task The_object_and_the_container_answer_jsonld_with_the_squid_fields()
    {
        var (_, ownerHandle, slug, postId) = await PublishAsync("ap03");
        var token = postId[(Origin.Length + "/o/".Length)..];

        var objectResponse = await _host.Http.GetAsync($"/objects/{token}");
        Assert.Equal(HttpStatusCode.OK, objectResponse.StatusCode);
        Assert.Equal(ActivityJson, objectResponse.Content.Headers.ContentType?.MediaType);
        using var objectJson = JsonDocument.Parse(await objectResponse.Content.ReadAsStringAsync());
        var note = objectJson.RootElement;
        Assert.Equal(ActivityStreams, note.GetProperty("@context")[0].GetString());
        Assert.Equal("Note", note.GetProperty("type").GetString());
        Assert.Equal(postId, note.GetProperty("id").GetString());
        Assert.Equal($"{Origin}/users/{ownerHandle}", note.GetProperty("attributedTo").GetString());
        Assert.Equal(slug, note.GetProperty("squid:slug").GetString());
        Assert.Equal("post", note.GetProperty("squid:kind").GetString());
        Assert.Equal("public", note.GetProperty("squid:policy").GetProperty("visibility").GetString());

        // The whole canonical id pastes back into the route as well (contract §1).
        Assert.Equal(
            HttpStatusCode.OK,
            (await _host.Http.GetAsync($"/objects/{Uri.EscapeDataString(postId)}")).StatusCode);

        var containerResponse = await _host.Http.GetAsync($"/c/{slug}");
        Assert.Equal(HttpStatusCode.OK, containerResponse.StatusCode);
        Assert.Equal(ActivityJson, containerResponse.Content.Headers.ContentType?.MediaType);
        using var containerJson = JsonDocument.Parse(await containerResponse.Content.ReadAsStringAsync());
        var group = containerJson.RootElement;
        Assert.Equal(ActivityStreams, group.GetProperty("@context")[0].GetString());
        Assert.Equal("Group", group.GetProperty("type").GetString());
        Assert.Equal($"{Origin}/c/{slug}", group.GetProperty("id").GetString());
        Assert.Equal("community", group.GetProperty("squid:kind").GetString());
        Assert.Equal(slug, group.GetProperty("squid:slug").GetString());
        Assert.Equal($"{Origin}/c/{slug}/inbox", group.GetProperty("inbox").GetString());
        Assert.Equal($"{Origin}/c/{slug}/outbox", group.GetProperty("outbox").GetString());
        Assert.Equal($"{Origin}/c/{slug}/members", group.GetProperty("members").GetString());

        var policy = group.GetProperty("squid:policy");
        Assert.Equal("public", policy.GetProperty("visibility").GetString());
        Assert.Equal("members", policy.GetProperty("posting").GetString());
        Assert.Equal("open", policy.GetProperty("join").GetString());
        Assert.Equal(1, policy.GetProperty("version").GetInt32());
    }

    [Fact] // AC-03 (container collections, first page and visibility)
    public async Task The_container_outbox_is_paged_and_the_members_page_respects_visibility()
    {
        var (owner, ownerHandle, slug, postId) = await PublishAsync("ap03b");

        // A second actor joins, so the page has something visibility can hide.
        var joiner = _host.NewClient();
        var joinerHandle = PostRoutes.Handle("ap03j");
        Assert.Equal(
            HttpStatusCode.Created,
            (await joiner.RegisterAsync(joinerHandle, PostRoutes.Password)).StatusCode);
        Assert.True(((int)(await PostRoutes.SendAsync(
            joiner, HttpMethod.Post, $"/api/containers/{slug}/join", null, joiner.Csrf)).StatusCode) is 200 or 201);

        var outboxId = $"{Origin}/c/{slug}/outbox";
        var outbox = await _host.Http.GetAsync(outboxId);
        Assert.Equal(HttpStatusCode.OK, outbox.StatusCode);
        using var outboxJson = JsonDocument.Parse(await outbox.Content.ReadAsStringAsync());
        Assert.Equal("OrderedCollection", outboxJson.RootElement.GetProperty("type").GetString());
        Assert.Equal($"{outboxId}?page=1", outboxJson.RootElement.GetProperty("first").GetString());
        Assert.False(outboxJson.RootElement.TryGetProperty("orderedItems", out _));
        AssertContainerFields(outboxJson.RootElement, slug); // AC-03 on the collection itself

        var page = await _host.Http.GetAsync($"{outboxId}?page=1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var pageJson = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Assert.Equal(outboxId, pageJson.RootElement.GetProperty("partOf").GetString());
        Assert.Equal(
            postId,
            pageJson.RootElement.GetProperty("orderedItems").EnumerateArray().First().GetProperty("id").GetString());
        AssertContainerFields(pageJson.RootElement, slug); // …and on its first page

        // Members: both actors while both are Active…
        var membersId = $"{Origin}/c/{slug}/members";
        var membersCollection = await _host.Http.GetAsync(membersId);
        Assert.Equal(HttpStatusCode.OK, membersCollection.StatusCode);
        using var membersJson = JsonDocument.Parse(await membersCollection.Content.ReadAsStringAsync());
        AssertContainerFields(membersJson.RootElement, slug);
        Assert.Equal(
            new[] { $"{Origin}/users/{ownerHandle}", $"{Origin}/users/{joinerHandle}" },
            await MemberIdsAsync(membersId));

        // …and only the owner once the other one is banned (the rule /members applies to a reader
        // that is not Admin+): the page carries visibility, it does not mirror the table.
        var ban = await PostRoutes.SendAsync(
            owner, HttpMethod.Patch, $"/api/containers/{slug}/members/{joinerHandle}",
            new { state = "ban", justification = "removido pela visibilidade da coleção" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.OK, ban.StatusCode);
        Assert.Equal(new[] { $"{Origin}/users/{ownerHandle}" }, await MemberIdsAsync(membersId));

        // A members-only container (the group preset) is invisible to a federated reader: the
        // container, its outbox and its members all answer the same 404 as an absent slug.
        var groupSlug = await CreateContainerAsync(owner, "group");
        foreach (var path in new[] { $"/c/{groupSlug}", $"/c/{groupSlug}/outbox", $"/c/{groupSlug}/members" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _host.Http.GetAsync(path)).StatusCode);
        }
    }

    [Fact] // AC-04
    public async Task Nodeinfo_reports_the_software_and_its_version()
    {
        var response = await _host.Http.GetAsync("/.well-known/nodeinfo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var document = json.RootElement;
        Assert.Equal("2.0", document.GetProperty("version").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.GetProperty("software").GetProperty("name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.GetProperty("software").GetProperty("version").GetString()));
        Assert.Contains("activitypub", document.GetProperty("protocols").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact] // AC-05
    public async Task Both_inboxes_refuse_without_a_verifiable_signature_and_change_nothing()
    {
        var (owner, ownerHandle, slug, _) = await PublishAsync("ap05");
        var activity = $$"""
            {
              "@context": "{{ActivityStreams}}",
              "id": "{{Origin}}/activities/unsigned",
              "type": "Follow",
              "actor": "https://remote.example/users/bob",
              "object": "{{Origin}}/users/{{ownerHandle}}"
            }
            """;

        var inboxes = new[] { $"/users/{ownerHandle}/inbox", $"/c/{slug}/inbox" };

        // No signature at all: 401, and the payload is never read.
        var bodies = new List<string>();
        foreach (var inbox in inboxes)
        {
            var response = await PostAsync(inbox, activity, signature: null);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        // A signature that is present but cannot be verified: the very same refusal, so a caller
        // cannot tell "you sent nothing" from "what you sent does not hold".
        var unverifiable = await PostAsync(
            inboxes[0], activity,
            "keyId=\"https://remote.example/users/bob#main-key\",algorithm=\"rsa-sha256\","
            + "headers=\"(request-target) host date\",signature=AAAA");
        Assert.Equal(HttpStatusCode.Unauthorized, unverifiable.StatusCode);
        bodies.Add(await unverifiable.Content.ReadAsStringAsync());
        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.DoesNotContain("traceId", bodies[0], StringComparison.Ordinal);

        // State intact: the follow above reached neither the membership of the container…
        var members = await PostRoutes.SendAsync(owner, HttpMethod.Get, $"/api/containers/{slug}/members");
        Assert.Single(JsonDocument.Parse(await members.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray());

        // …nor the outbox of the actor it was addressed to.
        var outbox = await _host.Http.GetAsync($"{Origin}/users/{ownerHandle}/outbox?page=1");
        using var outboxJson = JsonDocument.Parse(await outbox.Content.ReadAsStringAsync());
        Assert.Single(outboxJson.RootElement.GetProperty("orderedItems").EnumerateArray());
    }

    [Fact] // AC-05, the other half: a signature that DOES verify is accepted, still applying nothing
    public async Task A_locally_verifiable_signature_is_accepted_without_applying_the_activity()
    {
        var handle = PostRoutes.Handle("ap05s");
        var client = _host.NewClient();
        var register = await client.RegisterAsync(handle, PostRoutes.Password);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        // The very key the instance published, opened the way the ring opens it at boot (TASK-0042).
        using var kit = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var encryptedRootKey = kit.RootElement.GetProperty("kit").GetProperty("encryptedRootKey").GetString()!;
        var rootKey = RootKeyCipher.Decrypt(Convert.FromBase64String(encryptedRootKey), PostRoutes.Password);
        var row = await AuthDb.ReadAsync(
            _host.StoragePath, SqliteActorEventStore.StoreName, GrainId.Create("actor", handle).ToString());
        Assert.NotNull(row);
        var wrapped = Regex.Match(row!, "\"WrappedSigningKey\":\"([^\"]*)\"");
        Assert.True(wrapped.Success, "the wrapped signing key was not found in the actor row");
        // The row is JSON, so the base64 arrives escaped (S-25: System.Text.Json escapes the '+'
        // of base64 out of the printable range): decode the captured value AS a JSON string
        // instead of reading it raw.
        var wrappedKey = JsonSerializer.Deserialize<string>($"\"{wrapped.Groups[1].Value}\"");
        Assert.False(string.IsNullOrEmpty(wrappedKey));
        using var rsa = KeyDerivation.UnwrapRsaPrivateKey(rootKey, Convert.FromBase64String(wrappedKey!));

        var body = Encoding.UTF8.GetBytes($$"""
            {
              "@context": "{{ActivityStreams}}",
              "id": "{{Origin}}/activities/signed",
              "type": "Create",
              "actor": "{{Origin}}/users/{{handle}}",
              "object": { "id": "{{Origin}}/o/00000000000000000000000000000000", "type": "Note", "content": "não deve virar post" }
            }
            """);
        var signed = HttpSignatureSigner.Sign(
            new SignRequest(
                "post", $"/users/{handle}/inbox", _host.Http.BaseAddress!.Authority,
                $"{Origin}/users/{handle}#main-key", body, DateTimeOffset.UtcNow),
            signingString => rsa.SignData(signingString, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/users/{handle}/inbox")
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ActivityJson);
        request.Headers.TryAddWithoutValidation("Signature", signed.Signature);
        request.Headers.TryAddWithoutValidation("Digest", signed.Digest);
        request.Headers.TryAddWithoutValidation("Date", signed.Date);

        var response = await _host.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); // 202: accepted, nothing applied

        // The Create above is still not a post: the outbox of the actor stays empty (TASK-0044
        // is the task that will read the body and write state).
        var outbox = await _host.Http.GetAsync($"{Origin}/users/{handle}/outbox?page=1");
        Assert.Equal(HttpStatusCode.OK, outbox.StatusCode);
        using var outboxJson = JsonDocument.Parse(await outbox.Content.ReadAsStringAsync());
        Assert.Empty(outboxJson.RootElement.GetProperty("orderedItems").EnumerateArray());
    }

    [Fact] // AC-06
    public async Task The_profile_negotiates_accept_and_webfinger_keeps_only_the_self_link()
    {
        var handle = PostRoutes.Handle("ap06");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, PostRoutes.Password)).StatusCode);

        // Both media types of contract §6 are accepted…
        foreach (var accept in new[]
        {
            ActivityJson,
            "application/ld+json; profile=\"" + ActivityStreams + "\"",
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/users/{handle}");
            request.Headers.TryAddWithoutValidation("Accept", accept);
            var response = await _host.Http.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ActivityJson, response.Content.Headers.ContentType?.MediaType);
        }

        // …and nothing else is (406 before the lookup, so existence never leaks through a refusal).
        using var html = new HttpRequestMessage(HttpMethod.Get, $"/users/{handle}");
        html.Headers.TryAddWithoutValidation("Accept", "text/html");
        Assert.Equal(
            HttpStatusCode.NotAcceptable,
            (await _host.Http.SendAsync(html)).StatusCode);

        // webfinger: rel=self to the canonical actor, and NO rel=profile — P-08 owns that link (S-26).
        var webfinger = await _host.Http.GetAsync(
            $"/.well-known/webfinger?resource={Uri.EscapeDataString($"acct:{handle}@{_host.Http.BaseAddress!.Authority}")}");
        Assert.Equal(HttpStatusCode.OK, webfinger.StatusCode);
        using var jrd = JsonDocument.Parse(await webfinger.Content.ReadAsStringAsync());
        var links = jrd.RootElement.GetProperty("links").EnumerateArray().ToList();
        var self = links.Single(link => link.GetProperty("rel").GetString() == "self");
        Assert.Equal(ActivityJson, self.GetProperty("type").GetString());
        Assert.Equal($"{Origin}/users/{handle}", self.GetProperty("href").GetString());
        Assert.DoesNotContain(links, link => link.GetProperty("rel").GetString() == "profile");
    }

    /// <summary>Registers an owner, creates a public container and publishes one post in it.</summary>
    private async Task<(AuthClient Owner, string OwnerHandle, string Slug, string PostId)> PublishAsync(string prefix)
    {
        var (owner, ownerHandle) = await PostRoutes.SignedInAsync(_host, prefix);
        var slug = await CreateContainerAsync(owner, "community");
        var published = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, $"/api/containers/{slug}/posts",
            new { content = "primeiro post federado" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);

        using var json = JsonDocument.Parse(await published.Content.ReadAsStringAsync());
        var postId = json.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(postId));
        return (owner, ownerHandle, slug, postId!);
    }

    /// <summary>Creates a container of the given preset for the signed-in owner.</summary>
    private async Task<string> CreateContainerAsync(AuthClient owner, string kind)
    {
        var slug = PostRoutes.Slug(kind);
        var created = await PostRoutes.SendAsync(
            owner, HttpMethod.Post, "/api/containers",
            new { kind, name = "Rust Brasil", slug, summary = "comunidade de teste" }, owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return slug;
    }

    /// <summary>The actor URIs on page 1 of a members collection, in the order they federate.</summary>
    private async Task<string[]> MemberIdsAsync(string collectionId)
    {
        var response = await _host.Http.GetAsync($"{collectionId}?page=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("orderedItems").EnumerateArray()
            .Select(item => item.GetString())
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    /// <summary>AC-03 on any document of a container: kind, slug and the policy in force.</summary>
    private static void AssertContainerFields(JsonElement document, string slug)
    {
        Assert.Equal("community", document.GetProperty("squid:kind").GetString());
        Assert.Equal(slug, document.GetProperty("squid:slug").GetString());
        Assert.Equal("public", document.GetProperty("squid:policy").GetProperty("visibility").GetString());
    }

    /// <summary>POSTs an ActivityPub body to an inbox, with the optional draft-cavage Signature.</summary>
    private async Task<HttpResponseMessage> PostAsync(string inbox, string activity, string? signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, inbox)
        {
            Content = new StringContent(activity, Encoding.UTF8, ActivityJson),
        };
        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("Signature", signature);
        }

        return await _host.Http.SendAsync(request);
    }

    /// <summary>The origin the host answers on — the same rule the documents themselves publish.</summary>
    private string Origin => $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}";
}
