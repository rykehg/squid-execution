using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Persistence;

namespace Squid.Tests.Storage;

/// <summary>
/// AC-01..AC-05 and AC-07 of TASK-0003: the suite runs against <c>IGrainStorage</c> (GAP-05), so
/// the same cases can later run against another provider without a rewrite. Every test gets its
/// own real SQLite file in the temp folder — never a shared in-memory database — and the file is
/// deleted when the test ends (AC-07).
/// </summary>
public sealed class SqliteGrainStorageTests : IDisposable
{
    private readonly string _database =
        Path.Combine(Path.GetTempPath(), $"squid-grainstate-{Guid.NewGuid():N}.db");

    private readonly SqliteGrainStorage _storage;

    public SqliteGrainStorageTests() =>
        _storage = new SqliteGrainStorage($"Data Source={_database};Pooling=False");

    /// <summary>AC-07: no temp file survives its test.</summary>
    public void Dispose()
    {
        if (File.Exists(_database)) File.Delete(_database);
    }

    /// <summary>State type of the suite; public so the JSON serializer can construct it.</summary>
    public sealed class CounterState
    {
        public int Value { get; set; }
    }

    private static GrainId NewGrainId(string key) => GrainId.Create("counter", key);

    private async Task<GrainState<CounterState>> ReadAsync(string store, GrainId grainId)
    {
        var state = new GrainState<CounterState>();
        await _storage.ReadStateAsync(store, grainId, state);
        return state;
    }

    [Fact]
    public async Task Write_WithTheSameExpectedETag_SecondThrowsAndTheFirstStateStaysOnDisk()
    {
        var grainId = NewGrainId("etag-race");
        var seed = new GrainState<CounterState>(new CounterState { Value = 1 });
        await _storage.WriteStateAsync("actors", grainId, seed);

        var expected = seed.ETag;
        Assert.False(string.IsNullOrEmpty(expected));

        var first = new GrainState<CounterState>(new CounterState { Value = 2 }, expected!);
        await _storage.WriteStateAsync("actors", grainId, first);
        var afterFirst = first.ETag;
        Assert.NotEqual(expected, afterFirst);

        // AC-01: the second write carries the very same expected ETag the first one consumed.
        var second = new GrainState<CounterState>(new CounterState { Value = 3 }, expected!);
        await Assert.ThrowsAsync<InconsistentStateException>(
            () => _storage.WriteStateAsync("actors", grainId, second));

        var readBack = await ReadAsync("actors", grainId);
        Assert.Equal(2, readBack.State!.Value);
        Assert.Equal(afterFirst, readBack.ETag);
    }

    [Fact]
    public async Task Write_WithNullETagOnAnExistingRow_ThrowsAndDoesNotOverwrite()
    {
        var grainId = NewGrainId("null-etag");
        await _storage.WriteStateAsync("actors", grainId, new GrainState<CounterState>(new CounterState { Value = 1 }));

        var withoutEtag = new GrainState<CounterState>(new CounterState { Value = 99 });
        Assert.Null(withoutEtag.ETag);
        await Assert.ThrowsAsync<InconsistentStateException>(
            () => _storage.WriteStateAsync("actors", grainId, withoutEtag));

        var readBack = await ReadAsync("actors", grainId);
        Assert.Equal(1, readBack.State!.Value);
    }

    [Fact]
    public async Task Write_WithAnUnexpectedETagOnAMissingRow_Throws()
    {
        var grainId = NewGrainId("write-ghost");
        var ghost = new GrainState<CounterState>(new CounterState { Value = 1 }, "MQ==");

        await Assert.ThrowsAsync<InconsistentStateException>(
            () => _storage.WriteStateAsync("actors", grainId, ghost));

        var readBack = await ReadAsync("actors", grainId);
        Assert.Null(readBack.State);
    }

    [Fact]
    public async Task TwoStores_SameGrainId_KeepSeparateRows_ReadInBothDirections()
    {
        var grainId = NewGrainId("shared-key");
        await _storage.WriteStateAsync("actors", grainId, new GrainState<CounterState>(new CounterState { Value = 11 }));
        await _storage.WriteStateAsync("containers", grainId, new GrainState<CounterState>(new CounterState { Value = 22 }));

        var fromActors = await ReadAsync("actors", grainId);
        var fromContainers = await ReadAsync("containers", grainId);

        // Cross-read in both directions: neither store ever returns the other one's state.
        Assert.Equal(11, fromActors.State!.Value);
        Assert.Equal(22, fromContainers.State!.Value);
    }

    [Fact]
    public async Task Read_OfAMissingKey_ReturnsNullStateAndNullETagWithoutThrowing()
    {
        var readBack = await ReadAsync("actors", NewGrainId("never-written"));

        Assert.Null(readBack.State);
        Assert.Null(readBack.ETag);
        Assert.False(readBack.RecordExists);
    }

    [Fact]
    public async Task Clear_WithADivergentETag_ThrowsAndKeepsTheRow()
    {
        var grainId = NewGrainId("clear-stale");
        var seed = new GrainState<CounterState>(new CounterState { Value = 1 });
        await _storage.WriteStateAsync("actors", grainId, seed);
        var stale = seed.ETag!;

        var bumped = new GrainState<CounterState>(new CounterState { Value = 2 }, stale);
        await _storage.WriteStateAsync("actors", grainId, bumped);

        await Assert.ThrowsAsync<InconsistentStateException>(
            () => _storage.ClearStateAsync("actors", grainId, new GrainState<CounterState>(new CounterState { Value = 2 }, stale)));

        var readBack = await ReadAsync("actors", grainId);
        Assert.Equal(2, readBack.State!.Value);
        Assert.Equal(bumped.ETag, readBack.ETag);
    }

    [Fact]
    public async Task Clear_WithTheMatchingETag_RemovesTheRow_ThenReadIsAbsent()
    {
        var grainId = NewGrainId("clear-ok");
        var seed = new GrainState<CounterState>(new CounterState { Value = 1 });
        await _storage.WriteStateAsync("actors", grainId, seed);

        await _storage.ClearStateAsync("actors", grainId, new GrainState<CounterState>(new CounterState { Value = 1 }, seed.ETag));

        var readBack = await ReadAsync("actors", grainId);
        Assert.Null(readBack.State);
        Assert.Null(readBack.ETag);
        Assert.False(readBack.RecordExists);
    }

    [Fact]
    public async Task Clear_WithNullETagOnAnExistingRow_ThrowsAndKeepsTheRow()
    {
        var grainId = NewGrainId("clear-null");
        await _storage.WriteStateAsync("actors", grainId, new GrainState<CounterState>(new CounterState { Value = 1 }));

        await Assert.ThrowsAsync<InconsistentStateException>(
            () => _storage.ClearStateAsync("actors", grainId, new GrainState<CounterState>(new CounterState { Value = 1 })));

        var readBack = await ReadAsync("actors", grainId);
        Assert.Equal(1, readBack.State!.Value);
    }

    [Fact]
    public async Task Clear_OnAMissingRow_WithNullETag_IsANoOp()
    {
        var grainId = NewGrainId("clear-absent");
        var cleared = new GrainState<CounterState>(new CounterState { Value = 7 });

        await _storage.ClearStateAsync("actors", grainId, cleared);

        Assert.False(cleared.RecordExists);
        Assert.Null(cleared.ETag);
        var readBack = await ReadAsync("actors", grainId);
        Assert.Null(readBack.State);
    }

    [Fact]
    public async Task Storage_PersistsToARealTemporaryFile_NotInMemory()
    {
        var grainId = NewGrainId("file-check");
        await _storage.WriteStateAsync("actors", grainId, new GrainState<CounterState>(new CounterState { Value = 7 }));

        Assert.True(File.Exists(_database), "the database must be a real file on disk");
        Assert.True(new FileInfo(_database).Length > 0);
    }
}
