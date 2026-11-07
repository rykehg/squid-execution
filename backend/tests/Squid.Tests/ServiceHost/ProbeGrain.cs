using Orleans;
using Squid.Service;

namespace Squid.Tests.ServiceHost;

/// <summary>State persisted by <see cref="ProbeGrain"/> through the registered SQLite storage.</summary>
public sealed record ProbeState(string Value)
{
    /// <summary>Store name of the probe row (the stateName argument of PersistentState).</summary>
    public const string StateName = "probe";
}

/// <summary>Test grain of AC-03: every write goes through the silo's registered storage.</summary>
public interface IProbeGrain : IGrainWithStringKey
{
    /// <summary>Persists <paramref name="value"/> as the grain state.</summary>
    Task SaveAsync(string value);

    /// <summary>Returns the state the grain holds, or null while nothing was written.</summary>
    Task<string?> ReadAsync();

    /// <summary>The grain's own id as a string, so the test can address the same storage row.</summary>
    Task<string> IdAsync();
}

/// <summary>Grain whose state lives in the store the composition root registers as "sqlite".</summary>
public sealed class ProbeGrain(
    [PersistentState(ProbeState.StateName, SquidServiceHost.StorageProviderName)] IPersistentState<ProbeState> state)
    : Grain, IProbeGrain
{
    public async Task SaveAsync(string value)
    {
        state.State = new ProbeState(value);
        await state.WriteStateAsync();
    }

    public Task<string?> ReadAsync() => Task.FromResult(state.State?.Value);

    public Task<string> IdAsync() => Task.FromResult(GrainContext.GrainId.ToString());
}
