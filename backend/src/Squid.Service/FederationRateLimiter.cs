using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace Squid.Service;

/// <summary>
/// Contract activitypub §3 step 5: the per-domain quota of the inbox —
/// <c>Federation:RateLimitPerMinute</c> (default 60) accepted arrivals per fixed minute window,
/// the over-quota delivery refused with <c>429</c> and the seconds left of its window in
/// <c>Retry-After</c>. State is in-memory on purpose: the window only steers how fast THIS
/// instance is asked, and the per-peer override (<c>Peer.RateLimitPerMinute</c>, "configurável por
/// par") arrives with the PeerGrain of TASK-0046.
/// shortcut: fixed window, not sliding — a sender may burst across a boundary; upgrade to a
/// sliding log when 0046 makes the per-peer value live.
/// </summary>
public sealed class FederationRateLimiter
{
    /// <summary>The window itself: one minute, the contract's own unit.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Default quota when the configuration says nothing (contract §3 step 5).</summary>
    public const int DefaultLimit = 60;

    private readonly ConcurrentDictionary<string, DomainWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;
    private readonly int _limit;

    /// <summary>Reads the quota once: <c>Federation:RateLimitPerMinute</c>, default 60.</summary>
    public FederationRateLimiter(TimeProvider time, IConfiguration configuration)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentNullException.ThrowIfNull(configuration);
        _limit = int.TryParse(configuration["Federation:RateLimitPerMinute"], out var configured)
            && configured > 0
            ? configured
            : DefaultLimit;
    }

    /// <summary>
    /// One arrival against <paramref name="domain"/>'s quota: true when it counts and the delivery
    /// continues; false with the whole seconds the sender must wait before its window resets.
    /// </summary>
    public bool TryAcquire(string domain, out string retryAfterSeconds)
    {
        var now = _time.GetUtcNow();
        var start = now.UtcTicks - now.UtcTicks % Window.Ticks;
        var window = _windows.GetOrAdd(domain, _ => new DomainWindow(start));

        lock (window)
        {
            if (window.Start != start)
            {
                window.Start = start;
                window.Count = 0;
            }

            if (window.Count >= _limit)
            {
                var remaining = new DateTimeOffset(start + Window.Ticks, TimeSpan.Zero) - now;
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                return false;
            }

            window.Count++;
            retryAfterSeconds = "0";
            return true;
        }
    }

    /// <summary>One domain's counters inside one window — mutated only under its own lock.</summary>
    private sealed class DomainWindow(long start)
    {
        public long Start = start;

        public int Count;
    }
}
