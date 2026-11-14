using Squid.Grains.Peers;
using Squid.Tests.Containers;

namespace Squid.Tests.Federation;

/// <summary>
/// AC-01/AC-03/AC-04 of TASK-0046 at the grain itself (SPEC-0011 section G, data-model §2.9/§3.4):
/// the backoff formula every row and every circuit speaks, and the admission that decides whether
/// a domain may be talked to at all — blocked pair, open circuit, per-pair quota. No host, no
/// socket: the store is a dictionary and the clock is the suite's own hand.
/// </summary>
public sealed class PeerGrainTests
{
    [Fact] // AC-01 + AC-03 (backoff crescente, teto 6 h, jitter)
    public void Backoff_doubles_with_jitter_and_hits_the_six_hour_ceiling_exactly()
    {
        var grain = new PeerGrain(new MemoryPeerState(), new FakeClock());
        var delays = Enumerable.Range(1, PeerGrain.MaxAttempts)
            .Select(attempt => grain.Backoff(attempt))
            .ToList();

        // Exponential and strictly growing through the attempts that stay under the ceiling…
        for (var i = 0; i < 6; i++)
        {
            Assert.True(delays[i] < delays[i + 1],
                $"attempt {i + 1} must wait less than attempt {i + 2}: {delays[i]} vs {delays[i + 1]}");
            Assert.True(delays[i] >= TimeSpan.FromMinutes(5 * (1 << i)),
                $"the jitter may only inflate attempt {i + 1}: {delays[i]}");
        }

        // …and capped: no jitter may lift any delay above 6 h, and the eighth attempt IS the cap.
        Assert.All(delays, delay => Assert.True(delay <= PeerGrain.BackoffCap));
        Assert.Equal(PeerGrain.BackoffCap, delays[^1]);

        // The jitter must actually jitter: repeated draws of one attempt are not one fixed number.
        Assert.True(
            Enumerable.Range(0, 32).Select(_ => grain.Backoff(1).Ticks).Distinct().Count() > 1,
            "the backoff must be randomized, not a constant");
    }

    [Fact] // AC-03 (circuito aberto) e o bem-sucedido que o fecha
    public async Task An_open_circuit_defers_to_its_window_and_a_success_heals_the_peer()
    {
        var store = new MemoryPeerState();
        var clock = new FakeClock();
        var grain = new PeerGrain(store, clock);

        await grain.RecordFailureAsync("peer.example");
        var open = await grain.AdmitAsync("peer.example");
        Assert.False(open.Send);
        Assert.NotNull(open.DeferUntil);
        Assert.True(open.DeferUntil > clock.GetUtcNow());

        // After the window the pair admits exactly as a probe, visibly degraded.
        clock.Advance(open.DeferUntil!.Value - clock.GetUtcNow() + TimeSpan.FromSeconds(1));
        Assert.True((await grain.AdmitAsync("peer.example")).Send);
        Assert.Equal(PeerStatus.Degraded, (await store.FindAsync("peer.example"))!.Status);

        await grain.RecordSuccessAsync("peer.example");
        var healed = await store.FindAsync("peer.example");
        Assert.NotNull(healed);
        Assert.Equal(PeerStatus.Healthy, healed!.Status);
        Assert.Equal(0, healed.FailureCount);
        Assert.Null(healed.BackoffUntil);
        Assert.NotNull(healed.LastSuccessAt);
        Assert.True((await grain.AdmitAsync("peer.example")).Send);
    }

    [Fact] // AC-04 (par Blocked)
    public async Task A_blocked_pair_is_never_admitted()
    {
        var store = new MemoryPeerState();
        var grain = new PeerGrain(store, new FakeClock());
        await store.SaveAsync(new PeerState(
            "peer.example", PeerStatus.Blocked, null, 0, null, PeerState.DefaultRateLimitPerMinute));

        var admission = await grain.AdmitAsync("peer.example");
        Assert.False(admission.Send);
        Assert.Null(admission.DeferUntil); // not a deferral — a refusal that leaves the row untouched
    }

    [Fact] // AC-04 (RateLimitPerMinute por par)
    public async Task The_per_pair_rate_limit_admits_only_the_quota_inside_one_minute()
    {
        var store = new MemoryPeerState();
        var clock = new FakeClock();
        var grain = new PeerGrain(store, clock);
        await store.SaveAsync(new PeerState("peer.example", PeerStatus.Healthy, null, 0, null, 2));

        Assert.True((await grain.AdmitAsync("peer.example")).Send);
        Assert.True((await grain.AdmitAsync("peer.example")).Send);
        Assert.False((await grain.AdmitAsync("peer.example")).Send); // quota spent for this minute

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await grain.AdmitAsync("peer.example")).Send); // the window rolled
    }

    /// <summary>The port's simplest honest stand-in: the peer rows live in a dictionary.</summary>
    private sealed class MemoryPeerState : IPeerStateStore
    {
        private readonly Dictionary<string, PeerState> _rows = new(StringComparer.Ordinal);

        public Task<PeerState?> FindAsync(string domain) =>
            Task.FromResult(_rows.GetValueOrDefault(domain));

        public Task SaveAsync(PeerState state)
        {
            _rows[state.Domain] = state;
            return Task.CompletedTask;
        }
    }
}
