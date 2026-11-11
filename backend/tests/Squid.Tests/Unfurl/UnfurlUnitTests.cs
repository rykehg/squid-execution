using System.Net;
using Microsoft.Extensions.Configuration;
using Squid.Api.Posts;

namespace Squid.Tests.Unfurl;

/// <summary>
/// The pure pieces of TASK-0029 — the SSRF perimeter, the single-URL rule, the OpenGraph reader
/// and the configuration defaults — with no host in the way: the guard's ranges are the security
/// path of AC-03 checked against every boundary the spec names.
/// </summary>
public sealed class UnfurlUnitTests
{
    [Theory] // AC-03 — dentro do perímetro: recusado
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fec0::1")]
    [InlineData("::ffff:127.0.0.1")] // IPv4-mapped loopback
    [InlineData("::ffff:10.0.0.1")]  // IPv4-mapped private
    [InlineData("::7f00:1")]         // IPv4-compatible spelling of 127.0.0.1
    public void The_perimeter_refuses_every_address_inside_it(string address) =>
        Assert.True(SsrfGuard.Blocked(IPAddress.Parse(address)));

    [Theory] // fora do perímetro: admitido (as fronteiras exatas das faixas)
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]   // logo acima de 172.16/12
    [InlineData("100.128.0.1")]  // logo acima do CGNAT 100.64/10
    [InlineData("169.253.0.1")]  // logo abaixo do link-local
    [InlineData("100.63.255.255")] // logo abaixo do CGNAT
    [InlineData("2606:4700::1111")]
    public void The_perimeter_admits_public_addresses(string address) =>
        Assert.False(SsrfGuard.Blocked(IPAddress.Parse(address)));

    [Fact] // uma resposta privada entre públicas recusa a rodada inteira do resolver
    public void One_private_answer_refuses_the_whole_resolver_round() =>
        Assert.False(SsrfGuard.Allowed(
            [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("127.0.0.1")]));

    [Fact] // sem resposta não há endereço a validar — e nada é buscado
    public void An_empty_resolver_round_is_refused() =>
        Assert.False(SsrfGuard.Allowed([]));

    [Fact] // a regra MVP: só o conteúdo que É uma URL única vira alvo de unfurl
    public void Only_a_lone_http_url_is_a_unfurl_target()
    {
        Assert.Equal(new Uri("https://example.com/x"), LinkUnfurler.SingleUrl("https://example.com/x"));
        Assert.Equal(new Uri("http://example.com/x"), LinkUnfurler.SingleUrl("  http://example.com/x \n"));
        Assert.Null(LinkUnfurler.SingleUrl("veja https://example.com/x agora")); // URL embutida: sem unfurl
        Assert.Null(LinkUnfurler.SingleUrl("https://example.com/x\nsegunda linha"));
        Assert.Null(LinkUnfurler.SingleUrl("file:///etc/passwd"));               // esquema fora de http/https
        Assert.Null(LinkUnfurler.SingleUrl("ftp://example.com/x"));
        Assert.Null(LinkUnfurler.SingleUrl("example.com/x"));                    // sem esquema
        Assert.Null(LinkUnfurler.SingleUrl("https://"));                        // sem host
        Assert.Null(LinkUnfurler.SingleUrl(null));
        Assert.Null(LinkUnfurler.SingleUrl("   "));
    }

    [Fact] // os três og:*, em qualquer ordem e com aspas simples, primeira ocorrência vencendo
    public void The_opengraph_reader_takes_the_three_properties()
    {
        var og = OpenGraph.Parse("""
            <html><head>
            <meta content="Primeira &amp; melhor" property="og:title">
            <meta name='og:description' content='um resumo'>
            <meta property="og:image" content="https://cdn.test/capa.png">
            <meta property="og:title" content="segunda não deve vencer">
            <meta property="og:site_name" content="fora do escopo">
            </head></html>
            """);

        Assert.Equal("Primeira & melhor", og.Title);
        Assert.Equal("um resumo", og.Description);
        Assert.Equal("https://cdn.test/capa.png", og.Image);
    }

    [Fact] // página sem og:* — o card sai vazio, sem inventar nada
    public void Missing_properties_come_out_empty()
    {
        var og = OpenGraph.Parse("<html><head><title>só título</title></head></html>");
        Assert.Equal(string.Empty, og.Title);
        Assert.Equal(string.Empty, og.Description);
        Assert.Null(og.Image);
    }

    [Fact] // os defaults do spec (3 s, 1 MB) e a configuração por cima deles
    public void The_options_default_to_the_spec_and_override_from_configuration()
    {
        var defaults = UnfurlOptions.From(new ConfigurationBuilder().Build());
        Assert.Equal(3000, defaults.TimeoutMs);
        Assert.Equal(1024 * 1024, defaults.MaxBytes);

        var configured = UnfurlOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Unfurl:TimeoutMs"] = "250",
                ["Unfurl:MaxBytes"] = "16",
            })
            .Build());
        Assert.Equal(250, configured.TimeoutMs);
        Assert.Equal(16, configured.MaxBytes);
    }
}
