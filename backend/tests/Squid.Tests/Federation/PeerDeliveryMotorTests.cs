using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Crypto;
using Squid.Api.Federation;
using Squid.Grains.Actors;
using Squid.Grains.Peers;
using Squid.PeerClient;
using Squid.ReadModels.Federation;
using Squid.Service;
using Squid.Tests.Containers;
using Squid.Tests.Posts;

namespace Squid.Tests.Federation;

/// <summary>
/// The host of the TASK-0046 motor suite: <see cref="SquidServiceHost"/> with the PRODUCTION
/// sender — deliveries really leave through <c>HttpPeerSender</c> and really land on a
/// <see cref="PeerScenarioServer"/> — the boot <c>Squid:RecoveryKey</c> of one signable actor, a
/// hand-moved clock (GAP-17) and a 400 ms send budget so the timeout and slowness scenarios fail
/// inside a test's patience. It is built but left UNSTARTED by default: no route is called (the
/// queue is fed through <c>IOutboundPipeline</c> itself), no silo port binds and no hosted loop
/// ticks — every pass is the suite's own <c>RunOnceAsync</c>, and no fact can race a background
/// tick. The one fact that must prove the loop itself passes <c>hostedLoop: true</c> and gets the
/// started application. Each fact starts its own scenario server, so each fact talks to a
/// different peer authority and no peer state (circuit, quota, block) leaks between facts.
/// </summary>
public sealed class MotorHost : IAsyncDisposable
{
    /// <summary>The origin every payload actor of this suite carries — nothing serves it; ids are only minted from it.</summary>
    public const string Origin = "https://local.example";

    private readonly bool _started;

    public MotorHost(bool hostedLoop = false)
    {
        StoragePath = Path.Combine(Path.GetTempPath(), $"squid-motor-{Guid.NewGuid():N}.db");
        App = SquidServiceHost.Build(
            [
                "--Storage:Path=" + StoragePath,
                "--Logging:LogLevel:Default=Warning",
                // The budget the timeout scenario has to lose against (AC-05).
                "--Federation:SendTimeoutMs=400",
                // The scenario peer IS loopback: the exact allowlist of SPEC-0011 section C
                // names it the way the two-silo harness and the runbook name their local pairs.
                "--Federation:TrustedPeerHosts=127.0.0.1",
                "--Squid:RecoveryKey=" + Convert.ToBase64String(RuntimeKey.AsSpan()),
            ],
            builder => builder.Services.AddSingleton<TimeProvider>(Clock));
        App.Urls.Add("http://127.0.0.1:0");

        Handle = PostRoutes.Handle("motor");
        var keys = KeyState.Create(RuntimeKey, PostRoutes.Password);
        var material = new ActorKeyMaterial(
            [
                new ActorPublicKey("RSA-2048", "#main-key", Convert.ToBase64String(keys.RsaPublicKey)),
                new ActorPublicKey("Ed25519", "#ed25519", Convert.ToBase64String(keys.Ed25519PublicKey)),
            ],
            Convert.ToBase64String(keys.WrappedRsaPrivateKey));
        App.Services.GetRequiredService<ActorGrain>()
            .CreateAsync(Handle, "Motor de entrega", material).GetAwaiter().GetResult();

        if (hostedLoop)
        {
            App.StartAsync().GetAwaiter().GetResult();
            _started = true;
        }
    }

    public WebApplication App { get; }

    public FakeClock Clock { get; } = new();

    public RootKey RuntimeKey { get; } = RootKey.Create();

    public string StoragePath { get; }

    /// <summary>The signable local actor every delivery of this suite is signed with.</summary>
    public string Handle { get; }

    public PeerDeliveryPump Pump => App.Services.GetRequiredService<PeerDeliveryPump>();

    public IOutboundPipeline Outbound => App.Services.GetRequiredService<IOutboundPipeline>();

    public IPeerDeliveryStore Store => App.Services.GetRequiredService<IPeerDeliveryStore>();

    public IPeerStateStore Peers => App.Services.GetRequiredService<IPeerStateStore>();

    public SqlitePeerQueue Queue => App.Services.GetRequiredService<SqlitePeerQueue>();

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await App.StopAsync();
        }

        await App.DisposeAsync();
        try
        {
            File.Delete(StoragePath);
        }
        catch (IOException)
        {
            // A leftover temp file is not a product failure; the OS cleans it up.
        }
    }
}

/// <summary>
/// AC-01..AC-06 of TASK-0046 against the motor end to end: a real signed POST to a scripted peer
/// (SPEC-0011 section G), the row machine of data-model §3.4 driven by the suite's own clock, the
/// circuit of <c>PeerGrain</c>, the blocked pair and the per-pair quota of AC-04, the DLQ with
/// motive and alert of AC-02, and the <c>peer.domain</c> trace attribute of AC-06. AC-05's three
/// scenarios — 500, timeout and slowness — are the servers the sends really talk to.
/// </summary>
[Collection("ServiceHost")]
public sealed class PeerDeliveryMotorTests : IAsyncLifetime
{
    // One host per FACT: fresh queue, fresh clock, fresh peer state. A shared host made every
    // pass count other facts' still-due rows (the pump cannot know a row belongs to a finished
    // fact), so pass counts became a lottery — isolation is what a pass count can be asserted on.
    private readonly MotorHost _host = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    [Fact] // AC-01 + AC-05 (cenário 500)
    public async Task Ac01_a_500_goes_back_to_pending_with_attempts_and_a_growing_backoff()
    {
        await using var peer = PeerScenarioServer.Start(PeerScenario.ServerError);
        var row = await EnqueueAsync($"{peer.Origin}/users/alice");

        Assert.Equal(1, await _host.Pump.RunOnceAsync());
        var first = await CurrentAsync(row);
        Assert.NotNull(first);
        Assert.Equal(PeerDeliveryState.Pending, first!.State); // AC-01: volta para Pending
        Assert.Equal(1, first.Attempts);                       // attempts++
        Assert.Equal("send_failed", first.Reason);
        Assert.True(first.NextAttemptAt > _host.Clock.GetUtcNow(), "a retry is never due on the spot");
        var firstWindow = first.NextAttemptAt - _host.Clock.GetUtcNow();

        await SettleAsync(peer.Origin.Authority, first);
        Assert.Equal(1, await _host.Pump.RunOnceAsync());

        var second = await CurrentAsync(row);
        Assert.NotNull(second);
        Assert.Equal(2, second!.Attempts);
        var secondWindow = second.NextAttemptAt - _host.Clock.GetUtcNow();
        Assert.True(secondWindow > firstWindow,
            $"the backoff must grow: {firstWindow} then {secondWindow}");
        Assert.Equal(2, peer.Requests);
    }

    [Fact] // AC-02 (N = 8) + AC-05 (cenário 500)
    public async Task Ac02_after_eight_attempts_the_delivery_hits_the_dlq_with_motive_and_alert()
    {
        await using var peer = PeerScenarioServer.Start(PeerScenario.ServerError);
        var row = await EnqueueAsync($"{peer.Origin}/users/alice");
        using var alert = new MeterTap(FederationMetrics.MeterName, FederationMetrics.DeliveryDlqCounter);
        var before = alert.Count;

        PeerDelivery? current = row;
        for (var attempt = 0; attempt < 30 && current!.State == PeerDeliveryState.Pending; attempt++)
        {
            await SettleAsync(peer.Origin.Authority, current);
            await _host.Pump.RunOnceAsync();
            current = await CurrentAsync(row);
        }

        Assert.NotNull(current);
        Assert.Equal(PeerDeliveryState.Failed, current!.State); // DLQ
        Assert.Equal(PeerGrain.MaxAttempts, current.Attempts);
        Assert.Equal("max_attempts", current.Reason);           // com motivo
        Assert.True(alert.Count > before, "the DLQ alert counter must move"); // alerta

        var dead = Assert.Single(
            await _host.Queue.DlqAsync(10), queued => queued.ActivityId == row.ActivityId);
        Assert.Equal("max_attempts", dead.Reason);
        Assert.Equal(8, dead.Attempts);
    }

    [Fact] // AC-02 (4xx permanente)
    public async Task Ac02_a_permanent_404_dead_letters_the_delivery_on_the_first_attempt()
    {
        await using var peer = PeerScenarioServer.Start(PeerScenario.NotFound);
        var row = await EnqueueAsync($"{peer.Origin}/users/alice");
        using var alert = new MeterTap(FederationMetrics.MeterName, FederationMetrics.DeliveryDlqCounter);
        var before = alert.Count;

        await _host.Pump.RunOnceAsync();

        var stored = await CurrentAsync(row);
        Assert.NotNull(stored);
        Assert.Equal(PeerDeliveryState.Failed, stored!.State); // no backoff for a refusal
        Assert.Equal("peer_rejected", stored.Reason);
        Assert.Equal(1, stored.Attempts);
        Assert.True(alert.Count > before, "the DLQ alert counter must move");
    }

    [Fact] // AC-02 + SPEC-0011 section C (assinatura rejeitada)
    public async Task Ac02_a_401_is_dead_lettered_as_signature_rejected()
    {
        await using var peer = PeerScenarioServer.Start(PeerScenario.Unauthorized);
        var row = await EnqueueAsync($"{peer.Origin}/users/alice");

        await _host.Pump.RunOnceAsync();

        var stored = await CurrentAsync(row);
        Assert.NotNull(stored);
        Assert.Equal(PeerDeliveryState.Failed, stored!.State);
        Assert.Equal("signature_rejected", stored.Reason); // never a blind retry with the same key
        Assert.Equal(1, stored.Attempts);
    }

    [Fact] // AC-03 (circuito aberto)
    public async Task Ac03_an_open_circuit_pushes_a_new_delivery_straight_to_pending()
    {
        await using var peer = PeerScenarioServer.Start(PeerScenario.ServerError);
        var first = await EnqueueAsync($"{peer.Origin}/users/alice");
        await _host.Pump.RunOnceAsync(); // one failure opens the pair's circuit

        var state = await _host.Peers.FindAsync(peer.Origin.Authority);
        Assert.NotNull(state);
        Assert.Equal(PeerStatus.Down, state!.Status);
        Assert.NotNull(state.BackoffUntil);

        // A NEW delivery, due right now, while the circuit is open.
        var second = await EnqueueAsync($"{peer.Origin}/users/bob");
        Assert.Equal(1, await _host.Pump.RunOnceAsync()); // only bob's row is due

        var stored = await CurrentAsync(second);
        Assert.NotNull(stored);
        Assert.Equal(PeerDeliveryState.Pending, stored!.State); // direto para Pending…
        Assert.Equal(0, stored.Attempts);                       // …sem tentar
        Assert.Equal(state.BackoffUntil.Value, stored.NextAttemptAt); // …com NextAttemptAt futuro
        Assert.True(stored.NextAttemptAt > _host.Clock.GetUtcNow());
        Assert.Equal(1, peer.Requests); // nunca falha na hora: só a primeira entrega chegou ao par
        Assert.NotNull(await CurrentAsync(first)); // and the failed row kept its schedule
    }

    [Fact] // AC-04 (par Blocked)
    public async Task Ac04_a_blocked_peer_receives_nothing()
    {
        await using var peer = PeerScenarioServer.Start(PeerScenario.Ok);
        await _host.Peers.SaveAsync(new PeerState(
            peer.Origin.Authority, PeerStatus.Blocked, null, 0, null,
            PeerState.DefaultRateLimitPerMinute));
        var row = await EnqueueAsync($"{peer.Origin}/users/alice");

        await _host.Pump.RunOnceAsync();

        var stored = await CurrentAsync(row);
        Assert.NotNull(stored);
        Assert.Equal(PeerDeliveryState.Pending, stored!.State);
        Assert.Equal(0, stored.Attempts);
        Assert.Equal(row.NextAttemptAt, stored.NextAttemptAt); // the row was not even touched
        Assert.Equal(0, peer.Requests); // nada é enviado
    }

    [Fact] // AC-04 (RateLimitPerMinute por par)
    public async Task Ac04_the_per_pair_rate_limit_decides_who_leaves()
    {
        await using var peer = PeerScenarioServer.Start(PeerScenario.Ok);
        await _host.Peers.SaveAsync(new PeerState(
            peer.Origin.Authority, PeerStatus.Healthy, null, 0, null, 2));
        var rows = new List<PeerDelivery>();
        foreach (var user in new[] { "a", "b", "c", "d" })
        {
            rows.Add(await EnqueueAsync($"{peer.Origin}/users/{user}"));
        }

        Assert.Equal(4, await _host.Pump.RunOnceAsync()); // all four are due
        Assert.Equal(2, peer.Requests);                   // the quota is the whole say

        Assert.Equal(PeerDeliveryState.Sent, (await CurrentAsync(rows[0]))!.State);
        Assert.Equal(PeerDeliveryState.Sent, (await CurrentAsync(rows[1]))!.State);
        var held = await CurrentAsync(rows[2]);
        Assert.Equal(PeerDeliveryState.Pending, held!.State);
        Assert.Equal(0, held.Attempts);
        Assert.Equal(PeerDeliveryState.Pending, (await CurrentAsync(rows[3]))!.State);

        // Same minute: a second pass admits nobody else — and the held rows keep their place.
        await _host.Pump.RunOnceAsync();
        Assert.Equal(2, peer.Requests);
        Assert.Equal(PeerDeliveryState.Pending, (await CurrentAsync(rows[2]))!.State);
    }

    [Fact] // AC-05 (cenário timeout)
    public async Task Ac05_the_timeout_scenario_fails_the_send_and_keeps_the_row_scheduled()
    {
        await using var peer = PeerScenarioServer.Start(PeerScenario.Timeout);
        var row = await EnqueueAsync($"{peer.Origin}/users/alice");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // never hang a suite

        var started = DateTimeOffset.UtcNow;
        await _host.Pump.RunOnceAsync(budget.Token);
        var elapsed = DateTimeOffset.UtcNow - started;

        var stored = await CurrentAsync(row);
        Assert.NotNull(stored);
        Assert.Equal(PeerDeliveryState.Pending, stored!.State);
        Assert.Equal(1, stored.Attempts);
        Assert.True(elapsed < TimeSpan.FromSeconds(5),
            $"the 400 ms budget must expire long before the never-answering peer: {elapsed}");
        Assert.Equal(1, peer.Requests); // the peer GOT the delivery — it simply never answered
    }

    [Fact] // AC-05 (cenário lentidão)
    public async Task Ac05_the_slow_scenario_loses_to_the_send_budget()
    {
        await using var peer = PeerScenarioServer.Start(
            PeerScenario.Slow, TimeSpan.FromSeconds(5));
        var row = await EnqueueAsync($"{peer.Origin}/users/alice");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var started = DateTimeOffset.UtcNow;
        await _host.Pump.RunOnceAsync(budget.Token);
        var elapsed = DateTimeOffset.UtcNow - started;

        var stored = await CurrentAsync(row);
        Assert.NotNull(stored);
        Assert.Equal(PeerDeliveryState.Pending, stored!.State); // slowness is a failure, rescheduled
        Assert.Equal(1, stored.Attempts);
        Assert.True(elapsed < TimeSpan.FromSeconds(4),
            $"the budget must give up before the 5 s answer arrives: {elapsed}");
        Assert.Equal(1, peer.Requests);
    }

    [Fact] // AC-06 (trace peer.domain, SC-015)
    public async Task Ac06_every_send_carries_the_peer_domain_trace_attribute_without_pii()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FederationTrace.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var peer = PeerScenarioServer.Start(PeerScenario.Ok);
        var target = $"{peer.Origin}/users/alice";
        var row = await EnqueueAsync(target);

        await _host.Pump.RunOnceAsync();

        var span = Assert.Single(spans, activity => activity.OperationName == "peer.send");
        var domain = (string?)span.GetTagItem("peer.domain");
        Assert.Equal(new Uri(target).IdnHost, domain);
        Assert.NotNull(domain);
        Assert.DoesNotContain("/", domain!, StringComparison.Ordinal);      // a domain, not a path
        Assert.DoesNotContain("users", domain!, StringComparison.Ordinal); // no actor, no handle
        Assert.DoesNotContain(_host.Handle, domain!, StringComparison.Ordinal);
        Assert.Equal(PeerDeliveryState.Sent, (await CurrentAsync(row))!.State);
    }

    [Fact] // o pump está hospedado (a task pede "o pump no Worker/Service"): o loop registrado drena sozinho
    public async Task The_registered_hosted_loop_drains_a_due_delivery_on_its_own()
    {
        // The only fact that needs the STARTED application: every other one drives RunOnceAsync,
        // which would pass even if the IHostedService registration went missing.
        await using var host = new MotorHost(hostedLoop: true);
        await using var peer = PeerScenarioServer.Start(PeerScenario.Ok);
        var target = $"{peer.Origin}/users/alice";
        await host.Outbound.EnqueueFollowAsync(MotorHost.Origin, host.Handle, target);
        var row = Assert.Single(await host.Store.ForRecipientAsync(target));

        PeerDelivery? stored = row;
        for (var i = 0; i < 50 && stored!.State == PeerDeliveryState.Pending; i++)
        {
            await Task.Delay(100); // the loop's own 1 s tick, no manual pass anywhere
            stored = await host.Store.FindAsync(row.ActivityId, row.RecipientActorId);
        }

        Assert.Equal(PeerDeliveryState.Sent, stored!.State);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal(1, peer.Requests);
    }

    // ---- harness ----------------------------------------------------------------------------

    /// <summary>One Follow enqueued through the production pipeline — the queue's own front door.</summary>
    private async Task<PeerDelivery> EnqueueAsync(string targetActorUri)
    {
        await _host.Outbound.EnqueueFollowAsync(MotorHost.Origin, _host.Handle, targetActorUri);
        return Assert.Single(await _host.Store.ForRecipientAsync(targetActorUri));
    }

    private Task<PeerDelivery?> CurrentAsync(PeerDelivery row) =>
        _host.Store.FindAsync(row.ActivityId, row.RecipientActorId);

    /// <summary>
    /// Moves the clock past BOTH the row's next attempt and the pair's circuit window: a due row
    /// met with an open circuit is deferred, not sent, so a test that wants the NEXT attempt has
    /// to cross both instants — exactly what a retrying pump waits for in production.
    /// </summary>
    private async Task SettleAsync(string authority, PeerDelivery row)
    {
        var target = row.NextAttemptAt;
        if (await _host.Peers.FindAsync(authority) is { BackoffUntil: { } until } peer
            && until > target)
        {
            target = until;
        }

        _host.Clock.Advance(target - _host.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));
    }

    /// <summary>Reads one counter of the federation meter while a pass runs — the DLQ alert of AC-02.</summary>
    private sealed class MeterTap : IDisposable
    {
        private readonly MeterListener _listener = new();

        private long _count;

        public MeterTap(string meterName, string instrumentName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, value, _, _) => Interlocked.Add(ref _count, value));
            _listener.Start();
        }

        public long Count => Interlocked.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
