using System.Net;
using System.Text.Json;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Threads;

/// <summary>
/// AC-02's second half of TASK-0038 — the whitelist is INSTANCE CONFIGURATION (GAP-34): this host
/// starts with <c>Reactions:AllowedEmojis:0=🚀</c> and no other entry, so the very same route that
/// refuses 🚀 on the default host accepts it here while refusing 🎉, an emoji the default set
/// carries. 👍 stays accepted either way — GAP-34 keeps the Like always in the list. The host is
/// built by this test alone (a class fixture would run a second silo beside it); the
/// <c>ServiceHost</c> collection is what keeps hosts from overlapping.
/// </summary>
[Collection("ServiceHost")]
public sealed class ThreadReactionWhitelistConfigTests
{
    [Fact] // AC-02 — mudar a config muda o conjunto aceito
    public async Task Changing_Reactions_AllowedEmojis_changes_what_the_reaction_route_accepts()
    {
        var storage = Path.Combine(Path.GetTempPath(), $"squid-rxn-{Guid.NewGuid():N}.db");
        var app = SquidServiceHost.Build([
            "--Storage:Path=" + storage,
            "--Logging:LogLevel:Default=Warning",
            "--Reactions:AllowedEmojis:0=🚀",
        ]);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        try
        {
            var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var client = new AuthClient(http);
            Assert.Equal(HttpStatusCode.Created,
                (await client.RegisterAsync(PostRoutes.Handle("reacfg"), PostRoutes.Password)).StatusCode);

            var created = await PostRoutes.SendAsync(client, HttpMethod.Post, "/api/threads",
                new { kind = "direct", participantActorIds = new[] { PostRoutes.Handle("parcfg") } },
                client.Csrf);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var threadId = await IdOfAsync(created, "id");

            Assert.Equal(HttpStatusCode.OK, (await PostRoutes.SendAsync(client, HttpMethod.Post,
                $"{AckPath(threadId)}", null, client.Csrf)).StatusCode);
            var sent = await PostRoutes.SendAsync(client, HttpMethod.Post, MessagesPath(threadId),
                new { content = "com config da instância" }, client.Csrf);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            var messageId = await IdOfAsync(sent, "id");

            // 🚀: fora do conjunto PADRÃO, dentro do que esta instância declara → 200.
            var accepted = await ReactAsync(client, threadId, messageId, "🚀");
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

            // 🎉: o conjunto padrão aceita, o desta instância não → 422 invalid_emoji. Mudou a
            // config, mudou o conjunto aceito (AC-02).
            var refused = await ReactAsync(client, threadId, messageId, "🎉");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains("invalid_emoji",
                await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // 👍 continua aceito de qualquer jeito: GAP-34 mantém o Like sempre na lista.
            Assert.Equal(HttpStatusCode.OK,
                (await ReactAsync(client, threadId, messageId, "👍")).StatusCode);

            // E a leitura dentro da parede devolve SÓ as linhas que passaram, com as contagens
            // derivadas: 🚀 e 👍 com 1 cada.
            var read = await PostRoutes.SendAsync(client, HttpMethod.Get,
                $"{MessagesPath(threadId)}?since=0");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
            var reactions = json.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == messageId)
                .GetProperty("reactions").EnumerateArray()
                .Select(entry => (
                    Emoji: entry.GetProperty("emoji").GetString()!,
                    Count: entry.GetProperty("count").GetInt64(),
                    Mine: entry.GetProperty("mine").GetBoolean()))
                .ToArray();
            Assert.Equal(
                new[] { (Emoji: "🚀", Count: 1L, Mine: true), (Emoji: "👍", Count: 1L, Mine: true) },
                reactions);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try
            {
                File.Delete(storage);
            }
            catch (IOException)
            {
                // A leftover temp file is not a product failure; the OS cleans it up.
            }
        }
    }

    private static async Task<string> IdOfAsync(HttpResponseMessage response, string property)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty(property).GetString()!;
    }

    private static Task<HttpResponseMessage> ReactAsync(
        AuthClient client, string threadId, string messageId, string emoji) =>
        PostRoutes.SendAsync(client, HttpMethod.Post, ReactionsPath(threadId, messageId),
            new { emoji }, client.Csrf);

    private static string MessagesPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/messages";

    private static string ReactionsPath(string threadId, string messageId) =>
        $"{MessagesPath(threadId)}/{Uri.EscapeDataString(messageId)}/reactions";

    private static string AckPath(string threadId) =>
        $"/api/threads/{Uri.EscapeDataString(threadId)}/ack-no-e2ee";
}
