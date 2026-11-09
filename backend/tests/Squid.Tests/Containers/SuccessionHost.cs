using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Containers;

/// <summary>A clock the test moves by hand — GAP-17's injectable TimeProvider; no test sleeps.</summary>
public sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward: the 90 days of succession and the 30 of grace, instantly.</summary>
    public void AdvanceDays(int days) => _now += TimeSpan.FromDays(days);
}

/// <summary>
/// The host of the section G suite: <see cref="SquidServiceHost"/> with its own temp storage, the
/// instance operator fixed by configuration (GAP-14) and the production <c>TimeProvider</c> swapped
/// for <see cref="Clock"/> through the build seam — one host per test class, never overlapping
/// (the <c>ServiceHost</c> collection serializes them, localhost clustering fixes the silo ports).
/// </summary>
public class SuccessionHostFixture : IAsyncDisposable
{
    /// <summary>The handle <c>Instance:Operators</c> names: registered by the test that needs the gate.</summary>
    public const string OperatorHandle = "qa0021operator";

    public FakeClock Clock { get; } = new();

    public WebApplication App { get; }

    public HttpClient Http { get; }

    /// <summary>SQLite file behind the host, so a test can read the rows themselves.</summary>
    public string StoragePath { get; }

    public SuccessionHostFixture() : this([]) { }

    protected SuccessionHostFixture(IReadOnlyList<string> extraArgs)
    {
        StoragePath = Path.Combine(Path.GetTempPath(), $"squid-succession-{Guid.NewGuid():N}.db");
        var args = new List<string>
        {
            "--Storage:Path=" + StoragePath,
            "--Logging:LogLevel:Default=Warning",
            "--Instance:Operators=" + OperatorHandle,
        };
        args.AddRange(extraArgs);

        App = SquidServiceHost.Build([.. args], builder => builder.Services.AddSingleton<TimeProvider>(Clock));
        App.Urls.Add("http://127.0.0.1:0");
        App.StartAsync().GetAwaiter().GetResult();

        Http = new HttpClient { BaseAddress = new Uri(App.Urls.Single()) };
    }

    public AuthClient NewClient() => new(Http);

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await App.StopAsync();
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
/// The same host with both GAP-17 timers set — the only way to tell "configuration is read" from
/// "the default happens to match": claim opens at day 2 (default 90) and the grace window closes
/// at day 3 (default 30).
/// </summary>
public sealed class TimersHostFixture : SuccessionHostFixture
{
    public TimersHostFixture() : base([
        "--Containers:SuccessorInactivityDays=2",
        "--Containers:DeleteGraceDays=3",
    ]) { }
}
