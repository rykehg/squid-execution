using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Squid.Service;

namespace Squid.Tests.Federation;

/// <summary>
/// The two halves of TASK-0044's acceptance criteria the DEV scenarios did not reach, written
/// and executed by QA attempt 1/2: AC-03 spells "assinatura inválida" (not only a MISSING
/// <c>Signature</c> header — the tampered case of the DEV test is a Digest refusal, never a
/// failed verification) and AC-04 spells the quota as <c>RateLimitPerMinute</c> with the default
/// 60, while the DEV suite only proves the 429 at an explicitly lowered quota.
/// </summary>
[Collection("ServiceHost")]
public sealed class QaTask0044InboxGapTests : IClassFixture<InboxHostFixture>
{
    private readonly InboxHostFixture _host;

    public QaTask0044InboxGapTests(InboxHostFixture host) => _host = host;

    [Fact] // AC-03 (assinatura invalida: assinada com outra chave, nao apenas ausente)
    public async Task A_signature_made_with_a_foreign_key_is_refused_with_401()
    {
        var (_, _, slug) = await InboxHarness.CommunityAsync(_host, "q44s");
        var (actor, keyId, _) = InboxHarness.Remote(_host, "q44alice");
        using var forged = RSA.Create(2048); // a key the published keyId never had
        var inbox = $"/c/{slug}/inbox";
        var activity = $$"""
            {
              "@context": "{{InboxHarness.ActivityStreams}}",
              "id": "https://remote.example/activities/{{InboxHarness.Unique("q44s")}}",
              "type": "Create",
              "actor": "{{actor}}",
              "object": { "id": "https://remote.example/o/{{InboxHarness.Unique("q44s")}}", "type": "Note", "content": "assinatura forjada", "attributedTo": "{{actor}}" }
            }
            """;
        var logBefore = await InboxHarness.LogCountAsync(_host);

        var response = await InboxHarness.SendSignedAsync(_host.Http, inbox, activity, forged, keyId);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Refused at step 1: the log keeps exactly the events it had (state intact, §7.5).
        Assert.Equal(logBefore, await InboxHarness.LogCountAsync(_host));
        using var outbox = JsonDocument.Parse(
            await (await _host.Http.GetAsync($"/c/{slug}/outbox?page=1")).Content.ReadAsStringAsync());
        Assert.Empty(outbox.RootElement.GetProperty("orderedItems").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await _host.Http.GetAsync("/health")).StatusCode);
    }

    [Fact] // AC-04 (padrao 60 quando Federation:RateLimitPerMinute nao e configurado)
    public void The_default_quota_is_sixty_per_domain_without_configuration()
    {
        var configuration = new ConfigurationBuilder().Build(); // no key at all
        var limiter = new FederationRateLimiter(TimeProvider.System, configuration);
        var sender = "qa0044-default.example";

        Assert.Equal(60, FederationRateLimiter.DefaultLimit);
        for (var arrival = 1; arrival <= 60; arrival++)
        {
            Assert.True(
                limiter.TryAcquire(sender, out _),
                $"a chegada {arrival} deve caber no padrao de 60 por minuto");
        }

        Assert.False(limiter.TryAcquire(sender, out var retryAfter), "a 61a chegada excede o padrao");
        Assert.True(
            int.TryParse(retryAfter, out var seconds) && seconds >= 1,
            "a recusa deve devolver os segundos da janela no Retry-After");

        // The quota is per sender domain: another domain keeps its own 60 (§3 step 5).
        Assert.True(limiter.TryAcquire("qa0044-other.example", out _), "a taxa e por dominio");
    }
}
