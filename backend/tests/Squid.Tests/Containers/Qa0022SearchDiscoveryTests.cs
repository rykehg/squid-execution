using System.Net;
using System.Text.Json;
using Squid.Api.Auth;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>
/// The two things of TASK-0022 the DEV suite does not state by themselves: the SC-005 shape of
/// AC-03 — one hundred sessionless attempts aimed at the restricted rows (the unlisted group and
/// the soft-deleted community) that never reveal either of them, while the same route keeps
/// answering a listed control query — and the S-33 collision the ODEV recorded: the literal
/// search route shadows a container whose slug is <c>search</c> (a parked backlog item, not an
/// acceptance criterion, pinned here so the shadow stays visible).
/// </summary>
[Collection("ServiceHost")]
public sealed class Qa0022SearchDiscoveryTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public Qa0022SearchDiscoveryTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-03 (SC-005 shape: 100 attempts, 0 leaks, route still answers)
    public async Task A_hundred_sessionless_attempts_reveal_neither_the_unlisted_nor_the_deleted_row()
    {
        var (owner, _) = await SignedInAsync("sc5");

        // This class owns its host and its temp database, so these four rows are the whole table.
        var listed = Slug("vitrine");
        var listedName = $"Vitrine {Token()}";
        await CreateAsync(owner, "community", listedName, listed);

        var unlisted = Slug("escondido");
        var unlistedName = $"Escondido {Token()}";
        await CreateAsync(owner, "group", unlistedName, unlisted);

        var deleted = Slug("desligado");
        var deletedName = $"Desligado {Token()}";
        await CreateAsync(owner, "community", deletedName, deleted);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Delete,
            $"/api/containers/{deleted}?confirm={deleted}", null, owner.Csrf)).StatusCode);

        // Listed but members-only behind: discovery may show the row, never its member count.
        var club = Slug("clube");
        var clubName = $"Clube {Token()}";
        await CreateAsync(owner, "group", clubName, club);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{club}", new { listed = true }, owner.Csrf)).StatusCode);

        var allowed = new[] { listed, club };
        var hidden = new[] { unlisted, deleted };
        var anonymous = _host.NewClient();

        // 100 restricted attempts: every spelling of both hidden rows (exact, upper-case, partial)
        // plus random tokens (SC-005's "tentativas aleatórias") — the seeds carry the meaning and
        // the random fill keeps the count at 100 without ever depending on what it finds.
        var seeds = new[]
        {
            unlistedName, unlisted, unlistedName.ToUpperInvariant(), unlisted.ToUpperInvariant(),
            deletedName, deleted, deletedName.ToUpperInvariant(), deleted.ToUpperInvariant(),
            unlisted[..^1], deletedName[..^1],
        };
        var probes = new List<string>(100);
        for (var i = 0; probes.Count < 100; i++)
        {
            probes.Add(i < seeds.Length ? seeds[i] : Token());
        }

        var attempts = 0;
        foreach (var query in probes)
        {
            var response = await anonymous.SendAsync(HttpMethod.Get,
                $"/api/containers/search?q={Uri.EscapeDataString(query)}");
            attempts++;

            // The route stays public and alive through every attempt: no 401, no 500, no refusal.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var slug = item.GetProperty("slug").GetString()!;
                Assert.DoesNotContain(slug, hidden);
                Assert.Contains(slug, allowed);
                Assert.True(item.GetProperty("listed").GetBoolean(),
                    $"search returned an unlisted row: {slug} for q={query}");
            }
        }

        Assert.Equal(100, attempts);

        // Positive controls, so a search that answered only empties could never pass this test:
        // the listed community comes back by its own name, the listed members-only row comes back
        // with memberCount null for a reader who cannot see it (SC-005 metadata rule).
        var found = Assert.Single(await SearchAsync(anonymous, listedName));
        Assert.Equal(listed, found.GetProperty("slug").GetString());

        var clubRow = Assert.Single(await SearchAsync(anonymous, clubName));
        Assert.Equal(club, clubRow.GetProperty("slug").GetString());
        Assert.Equal(JsonValueKind.Null, clubRow.GetProperty("memberCount").ValueKind);
    }

    [Fact] // S-33 (parked by the ODEV — verification of a recorded decision, not an AC)
    public async Task The_literal_search_route_shadows_a_container_whose_slug_is_search()
    {
        var (owner, _) = await SignedInAsync("s33");
        var name = $"Colisao {Token()}";
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name, slug = "search" }, owner.Csrf)).StatusCode);

        var anonymous = _host.NewClient();

        // The literal route answers first (S-33): without a term this path is the 400 of AC-02,
        // never the 200 body of the container that owns the slug.
        var noTerm = await anonymous.SendAsync(HttpMethod.Get, "/api/containers/search");
        Assert.Equal(HttpStatusCode.BadRequest, noTerm.StatusCode);
        Assert.Contains("validation_failed", await noTerm.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // With a term the same path is discovery — the shadowed row is reachable only here.
        var row = Assert.Single(await SearchAsync(anonymous, name));
        Assert.Equal("search", row.GetProperty("slug").GetString());
    }

    private async Task CreateAsync(
        AuthClient client, string kind, string name, string slug) =>
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/api/containers",
            new { kind, name, slug }, client.Csrf)).StatusCode);

    private async Task<List<JsonElement>> SearchAsync(AuthClient client, string query)
    {
        var response = await client.SendAsync(HttpMethod.Get,
            $"/api/containers/search?q={Uri.EscapeDataString(query)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.Clone()).ToList();
    }

    private async Task<(AuthClient Client, string Handle)> SignedInAsync(string prefix)
    {
        var handle = Handle(prefix);
        var client = _host.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await client.RegisterAsync(handle, Password)).StatusCode);
        return (client, handle);
    }

    private static Task<HttpResponseMessage> SendAsync(
        AuthClient client,
        HttpMethod method,
        string path,
        object? body = null,
        string? csrf = null) =>
        client.SendAsync(method, path, body, request =>
        {
            if (csrf is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }
        });

    private static string Token() =>
        Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    private static string Handle(string prefix) => prefix + Token();

    private static string Slug(string prefix) => prefix + "-" + Token();
}
