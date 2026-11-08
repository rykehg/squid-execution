using Squid.Grains.Actors;

namespace Squid.Tests.Actors;

/// <summary>
/// AC-05 of TASK-0013: what the storage row holds is inspected as raw JSON, in both shapes the
/// stream takes (open log, and snapshot after truncation). Only wrapped/public material may be
/// there — the clear root key and the clear private key exist in this test and are never handed
/// to the grain, so their absence in the row is a property of the persisted schema (GAP-09).
/// </summary>
public sealed class ActorPersistedStateTests : IDisposable
{
    private static readonly byte[] ClearRootKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] ClearRsaPrivate = Enumerable.Range(1, 256).Select(i => (byte)(i % 256)).ToArray();

    private readonly string _database =
        Path.Combine(Path.GetTempPath(), $"squid-actor-material-{Guid.NewGuid():N}.db");

    private readonly SqliteActorEventStore _store;

    public ActorPersistedStateTests() =>
        _store = new SqliteActorEventStore($"Data Source={_database};Pooling=False");

    /// <summary>No temp file survives its test.</summary>
    public void Dispose()
    {
        if (File.Exists(_database)) File.Delete(_database);
    }

    /// <summary>The wrapped shape of the clear private key: what registration would store instead.</summary>
    private static byte[] Wrapped() => ClearRsaPrivate.Select(b => (byte)(b ^ 0xA5)).ToArray();

    [Fact]
    public async Task PersistedJson_ContainsOnlyWrappedAndPublicMaterial_InLogAndInSnapshot()
    {
        var wrapped = Wrapped();
        var ed25519Public = new byte[] { 7, 7, 7, 7 };
        var rsaPublic = new byte[] { 8, 8, 8, 8 };
        var material = new ActorKeyMaterial(
            [
                new ActorPublicKey("Ed25519", "#ed25519", ActorTestData.B64(ed25519Public)),
                new ActorPublicKey("RSA-2048", "#main-key", ActorTestData.B64(rsaPublic)),
            ],
            ActorTestData.B64(wrapped));

        var grain = new ActorGrain(_store);
        await grain.CreateAsync("eve_01", "Eve", material);

        // shape 1: the event sits in the open log
        AssertOnlyWrappedOrPublic(await _store.ReadPersistedJsonAsync("eve_01"), wrapped, ed25519Public, rsaPublic);

        // shape 2: the same material inside the snapshot, after the threshold truncates the log
        for (var i = 0; i < ActorGrain.DefaultSnapshotThreshold - 1; i++)
        {
            await grain.ChangeDisplayNameAsync("eve_01", "Eve");
        }

        var record = await _store.ReadAsync("eve_01");
        Assert.NotNull(record!.Stream.Snapshot);
        Assert.Empty(record.Stream.Events);
        AssertOnlyWrappedOrPublic(await _store.ReadPersistedJsonAsync("eve_01"), wrapped, ed25519Public, rsaPublic);
    }

    [Fact]
    public async Task Write_WithNullETagOverAnExistingRow_ThrowsTypedConflict_AndKeepsTheRow()
    {
        var stream = new ActorEventStream
        {
            Events = { new ActorCreated("frank_01", null, ActorTestData.Material()) },
        };
        await _store.WriteAsync("frank_01", stream, null);

        await Assert.ThrowsAsync<ActorStoreConflictException>(
            () => _store.WriteAsync("frank_01", stream, null));   // the port's typed ETag refusal

        var record = await _store.ReadAsync("frank_01");
        Assert.Single(record!.Stream.Events);                     // nothing was overwritten
    }

    private static void AssertOnlyWrappedOrPublic(string? json, byte[] wrapped, byte[] ed25519Public, byte[] rsaPublic)
    {
        Assert.NotNull(json);

        // Values are searched in their serialized form: the default encoder of System.Text.Json
        // escapes base64 characters like '+' and '/', so the raw string is not a substring of the row.
        static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

        // material that must be there: wrapped and public
        Assert.Contains(Json(ActorTestData.B64(wrapped)), json);
        Assert.Contains(Json(ActorTestData.B64(ed25519Public)), json);
        Assert.Contains(Json(ActorTestData.B64(rsaPublic)), json);
        Assert.Contains("\"WrappedSigningKey\":", json);

        // material that must never be there: the root key and the private key in the clear
        Assert.DoesNotContain(Json(ActorTestData.B64(ClearRootKey)), json);
        Assert.DoesNotContain(Json(Convert.ToHexString(ClearRootKey)), json);
        Assert.DoesNotContain(Json(ActorTestData.B64(ClearRsaPrivate)), json);
        Assert.DoesNotContain(Json(Convert.ToHexString(ClearRsaPrivate)), json);

        // and no property of the schema can hold them either
        Assert.DoesNotContain("\"RootKey\":", json);
        Assert.DoesNotContain("\"SigningKey\":", json);
        Assert.DoesNotContain("\"PrivateKey\":", json);
        Assert.DoesNotContain("\"Rk\":", json);
    }
}
