using System.Net;
using Squid.Api.Auth;
using Squid.Tests.Auth;

namespace Squid.Tests.Posts;

/// <summary>
/// The house helpers of a route suite (mirror of <c>ContainerRoutesTests</c>'s private ones):
/// unique handles and slugs, a signed-in client per account, and the CSRF header every mutation
/// of this slice carries.
/// </summary>
internal static class PostRoutes
{
    /// <summary>The password the register endpoint accepts (house value).</summary>
    public const string Password = "senha-forte-1234";

    /// <summary>A unique handle inside <c>ActorHandle</c>'s rule.</summary>
    public static string Handle(string prefix) =>
        prefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    /// <summary>A unique container slug.</summary>
    public static string Slug(string prefix) =>
        prefix + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant();

    /// <summary>Registers a fresh account and returns its cookie jar.</summary>
    public static async Task<(AuthClient Client, string Handle)> SignedInAsync(AuthHostFixture host, string prefix)
    {
        var handle = Handle(prefix);
        var client = host.NewClient();
        var registered = await client.RegisterAsync(handle, Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return (client, handle);
    }

    /// <summary>Sends the request with the CSRF header (and <c>If-Match</c>) when a mutation carries one.</summary>
    public static Task<HttpResponseMessage> SendAsync(
        AuthClient client,
        HttpMethod method,
        string path,
        object? body = null,
        string? csrf = null,
        string? ifMatch = null) =>
        client.SendAsync(method, path, body, request =>
        {
            if (csrf is not null)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }

            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }
        });

    /// <summary>The origin (scheme + host) the test host answers on.</summary>
    public static string Origin(AuthHostFixture host) =>
        $"{host.Http.BaseAddress!.Scheme}://{host.Http.BaseAddress.Authority}";
}
