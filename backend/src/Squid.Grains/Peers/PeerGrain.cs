using System.Collections.Concurrent;

namespace Squid.Grains.Peers;

/// <summary>
/// The per-domain delivery motor of SPEC-0011 section G (FR-036, data-model §2.9/§3.4): one plain
/// class in this project's own grain tradition (like <c>InboxGrain</c> — no Orleans package here),
/// holding the circuit breaker, the backoff formula and the per-pair rate window a remote domain
/// earns. The pump of <c>Squid.Service</c> asks <see cref="AdmitAsync"/> before every send, reports
/// the outcome back through <see cref="RecordSuccessAsync"/>/<see cref="RecordFailureAsync"/>, and
/// the row machine itself (Pending → Sent | Failed | Cancelled) stays with the queue — this grain
/// decides whether a domain may be spoken to, never what happens to a row.
/// </summary>
public sealed class PeerGrain
{
    /// <summary>N of data-model §3.4: eight attempts, then the row goes to the DLQ (AC-02).</summary>
    public const int MaxAttempts = 8;

    /// <summary>The first retry waits this long; every further attempt doubles it (spec: exponencial).</summary>
    public static readonly TimeSpan BackoffBase = TimeSpan.FromMinutes(5);

    /// <summary>The 6 h ceiling of §3.4 — no jitter may lift a delay above it (AC-03).</summary>
    public static readonly TimeSpan BackoffCap = TimeSpan.FromHours(6);

    private readonly IPeerStateStore _store;
    private readonly TimeProvider _time;

    /// <summary>Send counts per pair inside the current minute — memory on purpose, like the inbox limiter's windows.</summary>
    private readonly ConcurrentDictionary<string, MinuteWindow> _quotas = new(StringComparer.Ordinal);

    /// <summary>Binds the grain to the peer row store and the injectable clock (GAP-17).</summary>
    public PeerGrain(IPeerStateStore store, TimeProvider time)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// The delay after <paramref name="attempts"/> failed attempts: <c>base · 2^(attempts−1)</c>
    /// with up to +20 % jitter, clamped to <see cref="BackoffCap"/>. The jitter only ever inflates,
    /// so consecutive attempts stay strictly growing (AC-01's "backoff crescente") and the cap is
    /// exact — the eighth delay is always 6 h (AC-03).
    /// </summary>
    public TimeSpan Backoff(int attempts)
    {
        var exponent = Math.Max(1, attempts) - 1;
        var raw = TimeSpan.FromTicks((long)Math.Min(
            BackoffCap.Ticks,
            BackoffBase.Ticks * Math.Pow(2, Math.Min(exponent, 30))));
        var jittered = (long)(raw.Ticks * (1 + Random.Shared.NextDouble() * 0.2));
        return TimeSpan.FromTicks(Math.Min(BackoffCap.Ticks, jittered));
    }

    /// <summary>
    /// Whether a delivery to <paramref name="domain"/> may go out right now (AC-03/AC-04):
    /// a blocked pair never sends; an open circuit defers the delivery to its window — never a
    /// failure on the spot, the row goes straight back to Pending with a future NextAttemptAt;
    /// an expired window flips the peer to Degraded and admits exactly as a probe; the rest is the
    /// per-pair quota, consumed only when the send is actually admitted.
    /// </summary>
    public async Task<PeerAdmission> AdmitAsync(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var now = _time.GetUtcNow();
        var state = await LoadAsync(domain);
        if (state.Status == PeerStatus.Blocked)
        {
            return new PeerAdmission(false, null);
        }

        if (state.BackoffUntil is { } until && now < until)
        {
            return new PeerAdmission(false, until); // circuit open: the pump defers the row to here
        }

        if (state.Status == PeerStatus.Down)
        {
            // Half-open: the window passed, so the next send is the probe that heals or reopens.
            await _store.SaveAsync(state with { Status = PeerStatus.Degraded });
        }

        return TryAcquire(domain, state.RateLimitPerMinute, now)
            ? new PeerAdmission(true, null)
            : new PeerAdmission(false, null);
    }

    /// <summary>An accepted delivery: the peer is healthy again and the circuit closes (AC-01's success half).</summary>
    public async Task RecordSuccessAsync(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var state = await LoadAsync(domain);
        await _store.SaveAsync(state with
        {
            Status = PeerStatus.Healthy,
            BackoffUntil = null,
            FailureCount = 0,
            LastSuccessAt = _time.GetUtcNow(),
        });
    }

    /// <summary>
    /// A failed attempt: the counter grows and the circuit opens for this domain's own backoff
    /// window, doubling per consecutive failure (AC-03) — Blocked is never reached from here,
    /// the pump only reports attempts it was admitted to make.
    /// </summary>
    public async Task RecordFailureAsync(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var state = await LoadAsync(domain);
        var failures = state.FailureCount + 1;
        await _store.SaveAsync(state with
        {
            Status = PeerStatus.Down,
            FailureCount = failures,
            BackoffUntil = _time.GetUtcNow() + Backoff(failures),
        });
    }

    /// <summary>The stored state, or the state a peer nobody ever touched is born with.</summary>
    private async Task<PeerState> LoadAsync(string domain) =>
        await _store.FindAsync(domain)
        ?? new PeerState(domain, PeerStatus.Healthy, null, 0, null, PeerState.DefaultRateLimitPerMinute);

    /// <summary>
    /// One pair's quota inside one fixed minute (the contract §3 step 5 unit): true when the send
    /// counts. An exhausted window answers false and the pump leaves the row due — the next pass,
    /// or the next minute, picks it up without an attempt burned.
    /// </summary>
    private bool TryAcquire(string domain, int limitPerMinute, DateTimeOffset now)
    {
        if (limitPerMinute <= 0)
        {
            return false; // a pair configured to zero receives nothing (AC-04's literal reading)
        }

        var start = now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute;
        var window = _quotas.GetOrAdd(domain, _ => new MinuteWindow(start));
        lock (window)
        {
            if (window.Start != start)
            {
                window.Start = start;
                window.Count = 0;
            }

            if (window.Count >= limitPerMinute)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    /// <summary>One pair's counters inside one minute — mutated only under its own lock.</summary>
    private sealed class MinuteWindow(long start)
    {
        public long Start = start;

        public int Count;
    }
}

/// <summary>
/// What the grain answered for one delivery: <see cref="Send"/> admits it; a non-null
/// <see cref="DeferUntil"/> is an open circuit (the row goes Pending with that future instant);
/// neither is a refusal — blocked pair or exhausted quota — and the row stays untouched.
/// </summary>
public sealed record PeerAdmission(bool Send, DateTimeOffset? DeferUntil);
