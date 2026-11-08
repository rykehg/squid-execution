using Squid.Grains.Actors;

namespace Squid.Tests.Actors;

/// <summary>
/// AC-01..AC-04 of TASK-0013: a valid handle persists its event and replays into the same state;
/// malformed and duplicate handles are refused typed with nothing written; reaching the threshold
/// snapshots and truncates the log; a rebuild after truncation still equals the direct state.
/// Every test persists through <see cref="SqliteActorEventStore"/> into its own real SQLite file.
/// </summary>
public sealed class ActorGrainTests : IDisposable
{
    private readonly string _database =
        Path.Combine(Path.GetTempPath(), $"squid-actor-{Guid.NewGuid():N}.db");

    private readonly SqliteActorEventStore _store;

    public ActorGrainTests() =>
        _store = new SqliteActorEventStore($"Data Source={_database};Pooling=False");

    /// <summary>No temp file survives its test.</summary>
    public void Dispose()
    {
        if (File.Exists(_database)) File.Delete(_database);
    }

    /// <summary>AC-01: the event is persisted, and a replay of it equals the state the command returned.</summary>
    [Fact]
    public async Task Create_WithValidHandle_PersistsActorCreated_AndReplayEqualsDirectState()
    {
        var grain = new ActorGrain(_store);
        var direct = await grain.CreateAsync("alice_01", "Alice", ActorTestData.Material());

        var record = await _store.ReadAsync("alice_01");
        Assert.NotNull(record);
        var created = Assert.IsType<ActorCreated>(Assert.Single(record!.Stream.Events));
        Assert.Equal("alice_01", created.Handle);
        Assert.Equal("Alice", created.DisplayName);
        Assert.Null(record.Stream.Snapshot);   // below the threshold: the log still carries the event

        var replayed = await new ActorGrain(_store).GetStateAsync("alice_01");
        Assert.Equal(direct, replayed);        // round-trip: rebuilt state is identical to direct state

        // a handle nobody wrote answers nothing, which is the 404 path of the callers
        Assert.Null(await grain.GetStateAsync("ghost_01"));
    }

    /// <summary>
    /// AC-02 negative: a malformed handle is refused with the typed error and the rule in its
    /// message, and not one row appears. The positive control in the same test proves the refusal
    /// is specific to the malformed value.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]                                        // shorter than 3
    [InlineData("aaaaaaaaaabbbbbbbbbbcccccccccc1")]           // 31 characters, longer than 30
    [InlineData("Alice")]                                     // uppercase
    [InlineData("a-b")]                                       // separator outside the rule
    [InlineData("a.b")]
    [InlineData("ação")]                                      // non-ascii
    [InlineData("abc\n")]                                     // trailing newline is not part of a handle
    public async Task Create_WithMalformedHandle_ThrowsTypedError_AndPersistsNothing(string? handle)
    {
        var grain = new ActorGrain(_store);

        var ex = await Assert.ThrowsAsync<InvalidActorHandleException>(
            () => grain.CreateAsync(handle!, "Nope", ActorTestData.Material()));

        Assert.Contains(ActorHandle.Rule, ex.Message);   // the message cites [a-z0-9_]{3,30}
        Assert.Equal(0, await _store.CountRowsAsync());  // nothing persisted

        // positive control: the very same grain accepts a valid handle
        await grain.CreateAsync("valid_ok_01", "Fine", ActorTestData.Material());
        Assert.Equal(1, await _store.CountRowsAsync());
    }

    /// <summary>
    /// AC-02 duplicate: a fresh instance refuses the handle because the persisted state already
    /// holds it, and the existing row stays byte-for-byte untouched.
    /// </summary>
    [Fact]
    public async Task Create_WithTakenHandle_ThrowsHandleTaken_AndPersistsNothingMore()
    {
        await new ActorGrain(_store).CreateAsync("bob_01", "Bob", ActorTestData.Material());
        var before = await _store.ReadPersistedJsonAsync("bob_01");
        Assert.NotNull(before);

        var duplicate = new ActorGrain(_store);     // a new instance: the refusal comes from storage
        var ex = await Assert.ThrowsAsync<ActorHandleTakenException>(
            () => duplicate.CreateAsync("bob_01", "Bob again", ActorTestData.Material()));

        Assert.Equal("bob_01", ex.Handle);
        Assert.Equal(1, await _store.CountRowsAsync());
        Assert.Equal(before, await _store.ReadPersistedJsonAsync("bob_01"));
    }

    /// <summary>AC-03: crossing the threshold writes the snapshot and truncates the events it covers.</summary>
    [Fact]
    public async Task WhenLogReachesThreshold_SnapshotIsWritten_AndOldEventsAreTruncated()
    {
        var grain = new ActorGrain(_store);     // default threshold, reachable through the public API
        await grain.CreateAsync("carol_01", "Carol", ActorTestData.Material());

        var before = await _store.ReadAsync("carol_01");
        Assert.Null(before!.Stream.Snapshot);
        Assert.Single(before.Stream.Events);            // the log still carries ActorCreated

        for (var i = 0; i < ActorGrain.DefaultSnapshotThreshold - 1; i++)
        {
            await grain.ChangeDisplayNameAsync("carol_01", $"Carol {i}");
        }

        var after = await _store.ReadAsync("carol_01");
        Assert.NotNull(after!.Stream.Snapshot);
        Assert.Equal(ActorGrain.DefaultSnapshotThreshold, after.Stream.SnapshotVersion);
        Assert.Empty(after.Stream.Events);                       // TRUNCATED
        Assert.Equal("carol_01", after.Stream.Snapshot!.Handle); // the state the events folded into

        var json = await _store.ReadPersistedJsonAsync("carol_01");
        Assert.DoesNotContain("\"$type\"", json!);               // no event survives in the row
        Assert.Contains("\"SnapshotVersion\":", json!);
    }

    /// <summary>
    /// AC-04: after the truncation, snapshot + the events written since rebuild an identical
    /// state — this test fails if the truncation ever dropped information the state needs.
    /// </summary>
    [Fact]
    public async Task RebuildAfterTruncation_EqualsDirectState()
    {
        var grain = new ActorGrain(_store);
        await grain.CreateAsync("dave_01", "Before", ActorTestData.Material());
        for (var i = 0; i < ActorGrain.DefaultSnapshotThreshold - 1; i++)
        {
            await grain.ChangeDisplayNameAsync("dave_01", "Before");
        }

        var direct = await grain.ChangeDisplayNameAsync("dave_01", "After");   // event AFTER the snapshot

        var record = await _store.ReadAsync("dave_01");
        Assert.NotNull(record!.Stream.Snapshot);
        Assert.Equal(ActorGrain.DefaultSnapshotThreshold, record.Stream.SnapshotVersion);
        Assert.IsType<ActorDisplayNameChanged>(Assert.Single(record.Stream.Events));

        var rebuilt = await new ActorGrain(_store).GetStateAsync("dave_01");
        Assert.Equal(direct, rebuilt);              // would fail if truncation had lost information
        Assert.Equal("After", rebuilt!.DisplayName); // the post-snapshot event really was replayed
        Assert.Equal(direct.Keys, rebuilt.Keys);     // the keys survive inside the snapshot
    }
}
