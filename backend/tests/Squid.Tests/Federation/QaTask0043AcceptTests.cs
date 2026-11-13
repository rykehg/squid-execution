using System.Net;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Federation;

/// <summary>
/// AC-06 of TASK-0043 on the routes this task ships: <c>FederationEndpoints</c> claims to walk
/// every route negotiating the <c>Accept</c> first, so a caller that cannot read JSON-LD gets
/// <c>406</c> before any lookup (existence never leaks through a refusal) and both media types
/// of contract §6 are served with the document media type. Written by QA-0043: the DEV scenario
/// proved the same rule on the profile route only.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0043AcceptTests : IClassFixture<AuthHostFixture>
{
    private const string ActivityJson = "application/activity+json";

    private const string ActivityStreams = "https://www.w3.org/ns/activitystreams";

    private readonly AuthHostFixture _host;

    public QaTask0043AcceptTests(AuthHostFixture host) => _host = host;

    [Fact] // AC-06, the federation half
    public async Task The_federation_routes_negotiate_the_accept_before_any_lookup()
    {
        var handle = PostRoutes.Handle("qa0043ac6");
        Assert.Equal(
            HttpStatusCode.Created,
            (await _host.NewClient().RegisterAsync(handle, PostRoutes.Password)).StatusCode);
        var origin = $"{_host.Http.BaseAddress!.Scheme}://{_host.Http.BaseAddress.Authority}";
        var outbox = $"{origin}/users/{handle}/outbox";
        var absent = PostRoutes.Handle("qa0043none");

        // Both media types of contract §6 are served, in the document media type.
        foreach (var accept in new[]
        {
            ActivityJson,
            "application/ld+json; profile=\"" + ActivityStreams + "\"",
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, outbox);
            request.Headers.TryAddWithoutValidation("Accept", accept);
            var response = await _host.Http.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ActivityJson, response.Content.Headers.ContentType?.MediaType);
        }

        // Anything else is refused 406 BEFORE the lookup, on a route that exists…
        using var html = new HttpRequestMessage(HttpMethod.Get, outbox);
        html.Headers.TryAddWithoutValidation("Accept", "text/html");
        Assert.Equal(
            HttpStatusCode.NotAcceptable,
            (await _host.Http.SendAsync(html)).StatusCode);

        // …and on one that does not: a caller that cannot read JSON-LD learns nothing.
        using var missingHtml = new HttpRequestMessage(HttpMethod.Get, $"{origin}/users/{absent}/outbox");
        missingHtml.Headers.TryAddWithoutValidation("Accept", "text/html");
        Assert.Equal(
            HttpStatusCode.NotAcceptable,
            (await _host.Http.SendAsync(missingHtml)).StatusCode);

        // The same absent actor with a readable Accept answers the plain 404 of the ladder.
        using var missingOk = new HttpRequestMessage(HttpMethod.Get, $"{origin}/users/{absent}/outbox");
        missingOk.Headers.TryAddWithoutValidation("Accept", ActivityJson);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.Http.SendAsync(missingOk)).StatusCode);
    }
}
