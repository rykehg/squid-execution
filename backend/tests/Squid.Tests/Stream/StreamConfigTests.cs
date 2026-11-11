using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Squid.Api.Posts;

namespace Squid.Tests.Stream;

/// <summary>
/// The two settings the stream parses: the heartbeat cadence (<c>Stream:PingSeconds</c>, default
/// 15 s of contract §5, fractional so a test never sleeps) and the reconnect cursor the client
/// echoes back (<c>Last-Event-ID</c>, which must never invent a position it cannot parse).
/// </summary>
public sealed class StreamConfigTests
{
    [Fact]
    public void Ping_interval_defaults_to_the_contract_15_seconds_and_accepts_the_fraction_a_test_needs()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), StreamEndpoints.PingInterval(Config()));
        Assert.Equal(TimeSpan.FromSeconds(0.25), StreamEndpoints.PingInterval(Config(("Stream:PingSeconds", "0.25"))));
        Assert.Equal(TimeSpan.FromSeconds(30), StreamEndpoints.PingInterval(Config(("Stream:PingSeconds", "30"))));

        // Unusable values fall back to the contract cadence instead of stalling the heartbeat.
        Assert.Equal(TimeSpan.FromSeconds(15), StreamEndpoints.PingInterval(Config(("Stream:PingSeconds", "0"))));
        Assert.Equal(TimeSpan.FromSeconds(15), StreamEndpoints.PingInterval(Config(("Stream:PingSeconds", "-2"))));
        Assert.Equal(TimeSpan.FromSeconds(15), StreamEndpoints.PingInterval(Config(("Stream:PingSeconds", "banana"))));
    }

    [Fact]
    public void Last_event_id_reads_the_reconnect_header_and_never_trusts_a_cursor_it_cannot_parse()
    {
        var context = new DefaultHttpContext();
        Assert.Equal(0, StreamEndpoints.LastEventId(context)); // ausente: do começo

        context.Request.Headers["Last-Event-ID"] = "1042";
        Assert.Equal(1042, StreamEndpoints.LastEventId(context));

        context.Request.Headers["Last-Event-ID"] = "-1";
        Assert.Equal(0, StreamEndpoints.LastEventId(context)); // negativo: nunca uma posição atrás do zero

        context.Request.Headers["Last-Event-ID"] = "abc";
        Assert.Equal(0, StreamEndpoints.LastEventId(context)); // malformado: replay do início, nunca um erro
    }

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();
}
