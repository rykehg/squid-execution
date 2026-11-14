using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Posts;
using Squid.Service;
using Squid.Tests.Unfurl;

namespace Squid.Tests.Federation;

/// <summary>
/// SPEC-0011 section C / GAP-49 — <c>Federation:TrustedPeerHosts</c>: an EXACT allowlist, empty by
/// default, consulted ONLY by the three federation clients (actor resolution, media download,
/// delivery to the peer). Every fact runs the production code against the
/// <see cref="FakeUnfurlHandler"/> that records each request — a refusal is asserted as "the
/// handler was never touched", so a regression that reaches a socket fails the suite.
/// </summary>
public sealed class TrustedPeerHostsTests
{
    // AC-01 — lista ausente (default): os três clientes de federação continuam recusando
    // loopback e rede privada, byte-idêntico ao de antes, sem tocar socket nenhum.
    [Fact]
    public async Task The_default_allowlist_keeps_every_federation_client_refusing_private_targets()
    {
        var handler = new FakeUnfurlHandler();
        var resolver = new FakeUnfurlResolver();
        resolver.Map("private.test", "192.168.1.9");
        using var http = new HttpClient(handler);
        var options = new UnfurlOptions(500, 4096);

        var media = new RemoteMediaClient(http, resolver, options, TrustedPeerHosts.Empty);
        Assert.Null(await media.FetchAsync("http://127.0.0.1:8080/a.png", CancellationToken.None));
        Assert.Null(await media.FetchAsync("http://private.test/a.png", CancellationToken.None));

        var actors = new HttpFederatedActorClient(
            http, resolver, options, TimeProvider.System, TrustedPeerHosts.Empty);
        Assert.False(await actors.ExistsAsync("http://127.0.0.1:8080/users/alice", CancellationToken.None));
        Assert.False(await actors.ExistsAsync("http://private.test/users/alice", CancellationToken.None));

        var sender = new HttpPeerSender(http, Config(), resolver, TrustedPeerHosts.Empty);
        Assert.False((await sender.SendWithStatusAsync(
            "http://127.0.0.1:8080/inbox", "s", null, "d", "{}", CancellationToken.None)).Accepted);
        Assert.False((await sender.SendWithStatusAsync(
            "http://private.test/inbox", "s", null, "d", "{}", CancellationToken.None)).Accepted);

        Assert.Empty(handler.Requests); // nenhuma recusa abriu socket: tudo barrado antes do handler
    }

    // AC-02 — host do par nomeado: ator e mídia aceitos MESMO em loopback/rede privada; o host
    // fora da lista segue recusado, inclusive na mesma porta, e uma lista nomeada não derrete a
    // guarda de um nome que não está nela.
    [Fact]
    public async Task A_named_peer_is_accepted_on_loopback_and_an_unlisted_host_on_the_same_port_is_not()
    {
        var handler = new FakeUnfurlHandler();
        handler.RespondWith((request, _) =>
        {
            // O ator responde o próprio id (o Parse do cliente exige); mídia responde bytes.
            var url = request.RequestUri!;
            var content = url.AbsolutePath.StartsWith("/users/", StringComparison.Ordinal)
                ? new StringContent($"{{\"id\":\"{url}\"}}", Encoding.UTF8, "application/activity+json")
                : new ByteArrayContent([1, 2, 3]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var resolver = new FakeUnfurlResolver();
        // Domínios NOMEADOS na lista respondem rede privada de propósito: confia-se no nome
        // nomeado, não na resposta (SPEC-0011 section C) — e fora da lista a mesma resposta recusa.
        resolver.Map("named.test", "192.168.1.9");
        resolver.Map("other.test", "192.168.1.9");
        using var http = new HttpClient(handler);
        var options = new UnfurlOptions(500, 4096);
        var trusted = TrustedPeerHosts.From(Config("Federation:TrustedPeerHosts", "127.0.0.1, named.test"));

        var media = new RemoteMediaClient(http, resolver, options, trusted);
        Assert.NotNull(await media.FetchAsync("http://127.0.0.1:8080/a.png", CancellationToken.None));
        Assert.NotNull(await media.FetchAsync("http://named.test:8080/a.png", CancellationToken.None));

        var actors = new HttpFederatedActorClient(
            http, resolver, options, TimeProvider.System, trusted);
        Assert.True(await actors.ExistsAsync("http://127.0.0.1:8080/users/alice", CancellationToken.None));
        Assert.Equal(3, handler.Requests.Count);

        // Fora da lista, MESMA porta 8080: recusado — a allowlist é de host, nunca de porta.
        Assert.Null(await media.FetchAsync("http://127.0.0.2:8080/a.png", CancellationToken.None));
        // Nome não listado que resolve privada: recusado mesmo com a lista nomeada.
        Assert.Null(await media.FetchAsync("http://other.test:8080/a.png", CancellationToken.None));
        Assert.False(await actors.ExistsAsync("http://other.test/users/alice", CancellationToken.None));
        Assert.Equal(3, handler.Requests.Count); // as três recusas continuam sem tocar socket
    }

    // Entrega ao par (o terceiro cliente de federação): envia ao par nomeado em loopback,
    // recusa o destinatário fora da lista — e a recusa responde como par inalcançável.
    [Fact]
    public async Task The_delivery_client_sends_to_a_named_peer_and_refuses_one_outside_the_list()
    {
        var handler = new FakeUnfurlHandler();
        handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)));
        using var http = new HttpClient(handler);
        var trusted = TrustedPeerHosts.From(Config("Federation:TrustedPeerHosts", "127.0.0.1"));
        var sender = new HttpPeerSender(http, Config(), new FakeUnfurlResolver(), trusted);

        var accepted = await sender.SendWithStatusAsync(
            "http://127.0.0.1:8080/inbox", "s", "d", "date", "{}", CancellationToken.None);
        Assert.True(accepted.Accepted);

        // MESMA porta, host fora da lista: nenhuma entrega sai.
        var refused = await sender.SendWithStatusAsync(
            "http://127.0.0.2:8080/inbox", "s", "d", "date", "{}", CancellationToken.None);
        Assert.False(refused.Accepted);
        Assert.Null(refused.StatusCode);

        Assert.Single(handler.Requests);
    }

    // A leitura da configuração: default vazio, grafias exatas (sem curinga nem sufixo) e as
    // duas formas que o .NET aceita para a chave (escalar com vírgulas e filhos indexados).
    [Fact]
    public void The_allowlist_is_empty_by_default_and_matches_hosts_exactly()
    {
        var empty = TrustedPeerHosts.From(new ConfigurationBuilder().Build());
        Assert.False(empty.Contains("127.0.0.1")); // AC-01: vazia por padrão
        Assert.False(empty.Contains(null));

        var configured = TrustedPeerHosts.From(Config("Federation:TrustedPeerHosts", " 127.0.0.1 , [::1] , Peer-A.test "));
        Assert.True(configured.Contains("127.0.0.1"));
        Assert.True(configured.Contains("[::1]"));
        Assert.True(configured.Contains("::1"));            // a mesma entrada, grafias IPv6
        Assert.True(configured.Contains("peer-a.test"));    // DNS não tem caixa
        Assert.False(configured.Contains("127.0.0.2"));     // exata: nem o vizinho da mesma rede
        Assert.False(configured.Contains("a.test"));        // exata: sem sufixo nem curinga
        Assert.False(configured.Contains("evil.peer-a.test"));

        var indexed = TrustedPeerHosts.From(Config("Federation:TrustedPeerHosts:0", "peer-b.test"));
        Assert.True(indexed.Contains("peer-b.test"));
        Assert.False(indexed.Contains("peer-a.test")); // a forma indexada não inventa entradas
    }

    // AC-03 — a lista NOMEADA no host de produção: o unfurl continua bloqueando rede privada
    // (nunca recebe a lista), enquanto o cliente de federação do MESMO host aceita o alvo.
    [Fact]
    public async Task The_unfurl_keeps_blocking_private_even_with_the_allowlist_named()
    {
        var handler = new FakeUnfurlHandler();
        handler.RespondWith((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        }));
        var resolver = new FakeUnfurlResolver();
        var storage = Path.Combine(Path.GetTempPath(), $"squid-trusted-{Guid.NewGuid():N}.db");
        var app = SquidServiceHost.Build(
        [
            "--Storage:Path=" + storage,
            "--Logging:LogLevel:Default=Warning",
            "--Federation:TrustedPeerHosts=127.0.0.1", // os pares locais nomeados, como no runbook
        ], builder =>
        {
            builder.Services.AddSingleton<HttpMessageHandler>(handler);
            builder.Services.AddSingleton<IUnfurlResolver>(resolver);
        });

        try
        {
            var unfurler = app.Services.GetRequiredService<LinkUnfurler>();
            Assert.Null(await unfurler.TryUnfurlAsync("http://127.0.0.1:8080/artigo", CancellationToken.None));
            Assert.Empty(handler.Requests); // a guarda do unfurl não consultou lista nenhuma

            var media = app.Services.GetRequiredService<IRemoteMediaClient>();
            Assert.NotNull(await media.FetchAsync("http://127.0.0.1:8080/a.png", CancellationToken.None));
            Assert.Single(handler.Requests); // o cliente de federação do MESMO host aceita
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
                // Um resíduo de temp não é defeito de produto; o sistema operacional limpa.
            }
        }
    }

    // ---- house helpers -----------------------------------------------------------------

    private static IConfiguration Config() => new ConfigurationBuilder().Build();

    private static IConfiguration Config(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
}
