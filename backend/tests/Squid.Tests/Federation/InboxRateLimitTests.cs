using System.Net;

namespace Squid.Tests.Federation;

/// <summary>
/// AC-04 of TASK-0044: a sender domain above <c>Federation:RateLimitPerMinute</c> (contract §3
/// step 5) is refused with <c>429</c> and a <c>Retry-After</c>, while the deliveries that fit the
/// quota went through. The suite builds its OWN host at 2/min so the assertion is exact and no
/// other federation test can ever meet the limiter (its fixture runs at 10 000/min).
/// </summary>
[Collection("ServiceHost")]
public sealed class InboxRateLimitTests : IDisposable
{
    private readonly InboxHostFixture _host = new(rateLimit: 2);

    public void Dispose() => _host.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Fact] // AC-04
    public async Task Over_the_per_domain_quota_the_inbox_answers_429_with_retry_after()
    {
        var (_, _, slug) = await InboxHarness.CommunityAsync(_host, "ac04");
        var (actor, keyId, rsa) = InboxHarness.Remote(_host, "oliviaac04");
        var inbox = $"/c/{slug}/inbox";
        var logBefore = await InboxHarness.LogCountAsync(_host);

        // Duas entregas cabem na cota do domínio (2/min)…
        for (var delivery = 0; delivery < 2; delivery++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await InboxHarness.SendSignedAsync(
                _host.Http, inbox, Activity($"ac04-{delivery}"), rsa, keyId)).StatusCode);
        }

        // …a terceira não: 429 com os segundos da janela no Retry-After (§3 passo 5).
        var rejected = await InboxHarness.SendSignedAsync(
            _host.Http, inbox, Activity("ac04-terceira"), rsa, keyId);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(
            rejected.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.Single(), out var seconds)
            && seconds >= 1,
            "o 429 deve carregar um Retry-After em segundos legível");

        // E a entrega recusada não tocou estado: só as duas que cabiam chegaram ao log.
        Assert.Equal(logBefore + 2, await InboxHarness.LogCountAsync(_host));
        Assert.Equal(HttpStatusCode.OK, (await _host.Http.GetAsync("/health")).StatusCode);

        string Activity(string suffix) => $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique(suffix)}}",
              "type": "Create",
              "actor": "{{actor}}",
              "object": { "id": "https://remote.example/o/{{InboxHarness.Unique(suffix)}}", "type": "Note", "content": "limite por domínio", "attributedTo": "{{actor}}" }
            }
            """;
    }
}
