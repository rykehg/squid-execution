using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;
using Squid.Tests.Unfurl;

namespace Squid.Tests.Federation;

/// <summary>
/// AC-02 and the ed25519 half of AC-03 of TASK-0052 (S-41): the production
/// <see cref="HttpFederatedActorClient"/> — fetch, SSRF perimeter, TTL and
/// <c>KeyOf</c> all untouched production code — resolving a FOREIGN actor served as the exact
/// minimal document of contract activitypub §1.1, with the armored multi-line
/// <c>publicKeyPem</c> Mastodon/Friendica publish (which the reader in ProfileJsonLd used to
/// refuse because the compact ran before the armor Replace). Only the sockets are fake: the
/// handler answers the actor document, the resolver answers its name with a public address, and
/// the inbox below runs the real §3 order until 202. The control signature — same keyId, another
/// key — must stay 401 with no state, so the 202 can only come from the published key verifying.
/// </summary>
[Collection("ServiceHost")]
public sealed class ArmoredRemoteKeyTests
{
    /// <summary>Fixed did:key of R-02: multicodec 0xed 0x01 over <see cref="Ed25519Key"/>.</summary>
    private const string Multibase = "z6MknDuGrPLr1vNh92m7yLreZCB43CznQiJouwqbveBVHu95";

    /// <summary>The 32 raw bytes behind <see cref="Multibase"/> — ASCII, so the vector reads itself.</summary>
    private static readonly byte[] Ed25519Key = Encoding.ASCII.GetBytes("squid-ed25519-key-0123456789abcd");

    [Fact] // AC-02 (a chave)
    public async Task KeyOf_resolves_the_armored_rsa_of_a_realistic_actor_document()
    {
        using var rsa = RSA.Create(2048);
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var handle = InboxHarness.Unique("t52r");
        var actorUri = $"https://remote.example/users/{handle}";
        using var http = new HttpClient(HandlerFor(actorUri, ActorDocument(handle, ArmoredPemJson(spki))));
        var client = new HttpFederatedActorClient(
            http, Resolver(), new UnfurlOptions(500, 8192), TimeProvider.System, TrustedPeerHosts.Empty);

        var key = await client.ResolveKeyAsync($"{actorUri}#main-key", CancellationToken.None);
        Assert.NotNull(key);
        Assert.Equal(spki, key!.RsaSpki);
        Assert.Null(key.Ed25519);

        // AC-01 na ponta de rede: assina a chave que PUBLICOU, verifica a resolvida do documento.
        var payload = Encoding.UTF8.GetBytes("chave resolvida de um documento de ator realista");
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var resolved = RSA.Create();
        resolved.ImportSubjectPublicKeyInfo(key.RsaSpki!, out _);
        Assert.True(
            resolved.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "a chave resolvida do documento nao verificou a assinatura da chave que o documento publica");

        // §3 passo 7: o proprio ator responde pelo mesmo documento (cacheado no TTL).
        Assert.True(await client.ExistsAsync(actorUri, CancellationToken.None));
    }

    [Fact] // AC-03 (squid:ed25519Key continua resolvendo)
    public async Task The_ed25519_multibase_of_a_realistic_document_still_resolves()
    {
        var handle = InboxHarness.Unique("t52e");
        var actorUri = $"https://remote.example/users/{handle}";
        using var http = new HttpClient(HandlerFor(actorUri, ActorDocument(handle, ArmoredPemJson([1, 2, 3]))));
        var client = new HttpFederatedActorClient(
            http, Resolver(), new UnfurlOptions(500, 8192), TimeProvider.System, TrustedPeerHosts.Empty);

        var key = await client.ResolveKeyAsync($"{actorUri}#ed25519", CancellationToken.None);
        Assert.NotNull(key);
        Assert.Null(key!.RsaSpki);
        Assert.Equal(Ed25519Key, key.Ed25519); // o vetor decodifica sem tocar no leitor de PEM
    }

    [Fact] // AC-02 (a inbox aceita com 202)
    public async Task The_inbox_accepts_the_activity_signed_with_the_realistic_documents_key()
    {
        using var rsa = RSA.Create(2048);
        var handle = InboxHarness.Unique("t52a");
        var actorUri = $"https://remote.example/users/{handle}";
        var handler = HandlerFor(actorUri, ActorDocument(handle, ArmoredPemJson(rsa.ExportSubjectPublicKeyInfo())));
        var storage = Path.Combine(Path.GetTempPath(), $"squid-armored-{Guid.NewGuid():N}.db");
        var app = SquidServiceHost.Build(
        [
            "--Storage:Path=" + storage,
            "--Logging:LogLevel:Default=Warning",
            // a fila fica parada: so a entrada de §3 e observada aqui (o Accept nunca sai).
            "--Federation:DeliveryPumpSeconds=0",
        ], builder =>
        {
            // As duas costuras de rede vao para o fake; IFederatedActorClient continua PRODUCAO.
            builder.Services.AddSingleton<HttpMessageHandler>(handler);
            builder.Services.AddSingleton<IUnfurlResolver>(Resolver());
        });
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        try
        {
            var local = PostRoutes.Handle("t52");
            Assert.Equal(HttpStatusCode.Created,
                (await new AuthClient(http).RegisterAsync(local, PostRoutes.Password)).StatusCode);
            var localActor = $"{http.BaseAddress!.Scheme}://{http.BaseAddress.Authority}/users/{local}";
            var inbox = $"/users/{local}/inbox";
            var follow = $$"""
                {
                  "@context": "{{InboxHarness.ActivityStreams}}",
                  "id": "{{actorUri}}/activities/{{InboxHarness.Unique("t52f")}}",
                  "type": "Follow",
                  "actor": "{{actorUri}}",
                  "object": "{{localActor}}"
                }
                """;

            // O controle: MESMA keyId, OUTRA chave — 401 e nada aplicado (a verificacao e real).
            using var impostor = RSA.Create(2048);
            var refused = await InboxHarness.SendSignedAsync(
                http, inbox, follow, impostor, $"{actorUri}#main-key");
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Empty(await FollowersAsync(http, localActor));

            // A chave certa, publicada no documento realista: 202 com o Accept do Follow.
            var accepted = await InboxHarness.SendSignedAsync(
                http, inbox, follow, rsa, $"{actorUri}#main-key");
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            using (var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync()))
            {
                Assert.Equal("Accept", body.RootElement.GetProperty("type").GetString());
                Assert.Equal(localActor, body.RootElement.GetProperty("actor").GetString());
            }

            Assert.Equal(new[] { actorUri }, await FollowersAsync(http, localActor));
            // E a resolucao passou pelo fetch PRODUCAO do documento, nao por atalho nenhum.
            Assert.Contains(new Uri(actorUri), handler.Requests);
        }
        finally
        {
            await app.DisposeAsync();
            try
            {
                File.Delete(storage);
            }
            catch (IOException)
            {
                // leftover de temp nao e defeito de produto; o sistema operacional limpa
            }
        }
    }

    // ---- fixture ------------------------------------------------------------------------

    /// <summary>The handler that answers EXACTLY the actor document of §1.1 and nothing else.</summary>
    private static FakeUnfurlHandler HandlerFor(string actorUri, string document)
    {
        var handler = new FakeUnfurlHandler();
        handler.RespondWith((request, _) => Task.FromResult(
            request.RequestUri!.AbsoluteUri == actorUri
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(document, Encoding.UTF8, "application/activity+json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        return handler;
    }

    /// <summary>The DNS seam answering the remote name with a PUBLIC address (the perimeter judges addresses).</summary>
    private static FakeUnfurlResolver Resolver()
    {
        var resolver = new FakeUnfurlResolver();
        resolver.Map("remote.example", "93.184.216.34");
        return resolver;
    }

    /// <summary>
    /// The byte-realistic remote actor: the exact minimal document of contract §1.1 — same field
    /// set, same order — carrying an armored multi-line <c>publicKeyPem</c> (escaped as it travels
    /// inside JSON) and the <c>squid:ed25519Key</c> multibase of R-02.
    /// </summary>
    private static string ActorDocument(string handle, string publicKeyPem) =>
        $$"""
        {
          "@context": [
            "https://www.w3.org/ns/activitystreams",
            { "squid": "https://remote.example/ns/squid#" }
          ],
          "id": "https://remote.example/users/{{handle}}",
          "type": "Person",
          "preferredUsername": "{{handle}}",
          "name": "Alice",
          "inbox": "https://remote.example/users/{{handle}}/inbox",
          "outbox": "https://remote.example/users/{{handle}}/outbox",
          "followers": "https://remote.example/users/{{handle}}/followers",
          "publicKey": {
            "id": "https://remote.example/users/{{handle}}#main-key",
            "owner": "https://remote.example/users/{{handle}}",
            "publicKeyPem": "{{publicKeyPem}}"
          },
          "squid:ed25519Key": {
            "id": "https://remote.example/users/{{handle}}#ed25519",
            "publicKeyMultibase": "{{Multibase}}"
          }
        }
        """;

    /// <summary>The armored PEM exactly as the wire carries it inside JSON: armor, 64-column lines, \n escapes.</summary>
    private static string ArmoredPemJson(byte[] spki)
    {
        var base64 = Convert.ToBase64String(spki);
        var lines = new List<string>();
        for (var offset = 0; offset < base64.Length; offset += 64)
        {
            lines.Add(base64.Substring(offset, Math.Min(64, base64.Length - offset)));
        }

        return "-----BEGIN PUBLIC KEY-----\\n"
            + string.Join("\\n", lines)
            + "\\n-----END PUBLIC KEY-----";
    }

    /// <summary>The actor URIs of page 1 of a relation collection, as the host federates them.</summary>
    private static async Task<string[]> FollowersAsync(HttpClient http, string actorUri)
    {
        using var response = await http.GetAsync($"{actorUri}/followers?page=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("orderedItems").EnumerateArray()
            .Select(item => item.GetString())
            .OfType<string>()
            .ToArray();
    }
}
