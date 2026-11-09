using System.Net;
using System.Text.Json;
using Squid.Api.Auth;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>
/// AC-01..AC-03 of TASK-0022 over HTTP against the host <see cref="SquidServiceHost"/> builds:
/// the unlisted group that never leaves the search under its exact name, authenticated or not
/// (AC-01); the case-insensitive substring of name and slug with the 400 of a missing, empty or
/// blank term (AC-02, GAP-15); and the sessionless route that answers a stranger while neither
/// an unlisted row, nor a soft-deleted one, nor the memberCount of a container they cannot see
/// leaks out of it (AC-03, FR-016/FR-018/SC-005).
/// </summary>
[Collection("ServiceHost")]
public sealed class ContainerSearchRoutesTests : IClassFixture<AuthHostFixture>
{
    private const string Password = "senha-forte-1234";

    private readonly AuthHostFixture _host;

    public ContainerSearchRoutesTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-01
    public async Task An_unlisted_group_never_appears_even_with_its_exact_name_authenticated_or_not()
    {
        var (owner, _) = await SignedInAsync("ac01");
        var slug = Slug("segredo");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = "Grupo Segredo", slug }, owner.Csrf)).StatusCode);

        var anonymous = _host.NewClient();

        // The exact name and the exact slug: with a session and without one, never a row (FR-016).
        Assert.DoesNotContain(slug, Slugs(await SearchAsync(anonymous, "Grupo Segredo")));
        Assert.DoesNotContain(slug, Slugs(await SearchAsync(anonymous, slug)));
        Assert.DoesNotContain(slug, Slugs(await SearchAsync(owner, "Grupo Segredo")));
        Assert.DoesNotContain(slug, Slugs(await SearchAsync(owner, slug)));

        // The flag is the only door: the very same row comes back once listed=true is written.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{slug}", new { listed = true }, owner.Csrf)).StatusCode);
        Assert.Contains(slug, Slugs(await SearchAsync(anonymous, "Grupo Segredo")));
    }

    [Fact] // AC-02
    public async Task The_term_matches_name_and_slug_ignoring_case_and_a_blank_term_is_a_400()
    {
        var (owner, _) = await SignedInAsync("ac02");
        var slug = Slug("vila");
        var name = $"Vila São Jorge Videoclube {Token()}";
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name, slug }, owner.Csrf)).StatusCode);

        var anonymous = _host.NewClient();

        // Substring, case-insensitive, on the name (lower, upper, accented) and on the slug —
        // no FTS, no ranking, no accent folding (GAP-15).
        Assert.Equal(new[] { slug }, Slugs(await SearchAsync(anonymous, "videoclube")));
        Assert.Equal(new[] { slug }, Slugs(await SearchAsync(anonymous, "VIDEOCLUBE")));
        Assert.Equal(new[] { slug }, Slugs(await SearchAsync(anonymous, "SÃO JORGE")));
        Assert.Equal(new[] { slug }, Slugs(await SearchAsync(anonymous, slug[..12].ToUpperInvariant())));
        Assert.Empty(await SearchAsync(anonymous, Token()));

        // q missing, q empty, q only blanks: one 400 validation_failed each, before any read.
        foreach (var path in new[]
        {
            "/api/containers/search",
            "/api/containers/search?q=",
            "/api/containers/search?q=%20%20",
        })
        {
            var refused = await anonymous.SendAsync(HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("validation_failed", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // The page parameters ride the house rules of GET /members: limit cuts the page,
        // a limit below 1 is the same refusal as a blank term.
        Assert.Single(await SearchAsync(anonymous, "videoclube", "&limit=1"));
        Assert.Empty(await SearchAsync(anonymous, "videoclube", "&limit=1&offset=1"));
        var badLimit = await anonymous.SendAsync(HttpMethod.Get, "/api/containers/search?q=videoclube&limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
    }

    [Fact] // AC-03
    public async Task The_route_is_public_and_no_unlisted_no_deleted_nor_hidden_count_leaves_it()
    {
        var (owner, _) = await SignedInAsync("ac03");
        var listedToken = Token();
        var hiddenToken = Token();
        var deletedToken = Token();

        var listed = Slug("vitrine");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = $"Vitrine {listedToken}", slug = listed }, owner.Csrf)).StatusCode);

        // A group flipped to listed: it stays members-only behind, only the flag moved.
        var hidden = Slug("mostruario");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "group", name = $"Mostruario {hiddenToken}", slug = hidden }, owner.Csrf)).StatusCode);
        Assert.Empty(await SearchAsync(_host.NewClient(), hiddenToken));

        var deleted = Slug("fora-ar");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(owner, HttpMethod.Post, "/api/containers",
            new { kind = "community", name = $"Fora do ar {deletedToken}", slug = deleted }, owner.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Delete,
            $"/api/containers/{deleted}?confirm={deleted}", null, owner.Csrf)).StatusCode);

        var anonymous = _host.NewClient();

        // Public route: no session, no cookie, and the listed row arrives with its count — it is
        // a public container, so the number is visible to whoever can already see the row.
        var found = await SearchAsync(anonymous, listedToken);
        var only = Assert.Single(found);
        Assert.Equal(listed, only.GetProperty("slug").GetString());
        Assert.True(only.GetProperty("listed").GetBoolean());
        Assert.Equal(1, only.GetProperty("memberCount").GetInt64());

        // No unlisted row, signed in or not; no deleted row either, same query, same emptiness.
        Assert.Empty(await SearchAsync(anonymous, hiddenToken));
        Assert.Empty(await SearchAsync(owner, hiddenToken));
        Assert.Empty(await SearchAsync(anonymous, deletedToken));
        Assert.Empty(await SearchAsync(owner, deletedToken));

        // Listed members-only: discovery reveals the row (FR-016), but SC-005's metadata rule
        // keeps the memberCount of a container this reader cannot see at null.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner, HttpMethod.Patch,
            $"/api/containers/{hidden}", new { listed = true }, owner.Csrf)).StatusCode);
        var revealed = Assert.Single(await SearchAsync(anonymous, hiddenToken));
        Assert.Equal(hidden, revealed.GetProperty("slug").GetString());
        Assert.Equal(JsonValueKind.Null, revealed.GetProperty("memberCount").ValueKind);
    }

    /// <summary>The route itself: 200 + the items page, asserted before the caller reads it.</summary>
    private async Task<List<JsonElement>> SearchAsync(
        AuthClient client, string query, string extra = "")
    {
        var response = await client.SendAsync(HttpMethod.Get,
            $"/api/containers/search?q={Uri.EscapeDataString(query)}{extra}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.Clone()).ToList();
    }

    private static string[] Slugs(IEnumerable<JsonElement> items) =>
        items.Select(item => item.GetProperty("slug").GetString()!).ToArray();

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

    private static string Handle(string prefix) =>
        prefix + Token();

    private static string Slug(string prefix) =>
        prefix + "-" + Token();
}
