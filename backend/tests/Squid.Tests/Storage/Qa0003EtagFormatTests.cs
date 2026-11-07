using System.Text;
using Microsoft.Data.Sqlite;
using Orleans;
using Orleans.Runtime;
using Squid.Persistence;

namespace Squid.Tests.Storage;

/// <summary>
/// QA-0003 / GAP-04: the assumption in force is "<c>etag</c> = <c>GrainStateVersion</c> em base64,
/// comparação exata em <c>WHERE Version = @Version</c>". The encoding half is proven here by
/// reading the <c>Version</c> column straight out of the SQLite file and comparing it with what the
/// API hands back — a lie in one layer cannot hide behind the other. The comparison half is proven
/// by the mutation run recorded in docs/sdd/qa/QA-0003-storage-sqlite-etag.md.
/// </summary>
public sealed class Qa0003EtagFormatTests : IDisposable
{
    private readonly string _database =
        Path.Combine(Path.GetTempPath(), $"squid-grainstate-{Guid.NewGuid():N}.db");

    private readonly SqliteGrainStorage _storage;

    public Qa0003EtagFormatTests() =>
        _storage = new SqliteGrainStorage($"Data Source={_database};Pooling=False");

    /// <summary>AC-07: no temp file survives its test.</summary>
    public void Dispose()
    {
        if (File.Exists(_database)) File.Delete(_database);
    }

    public sealed class CounterState
    {
        public int Value { get; set; }
    }

    private static GrainId NewGrainId(string key) => GrainId.Create("counter", key);

    /// <summary>The Version column as the file itself holds it, read with a separate connection.</summary>
    private string ReadStoredVersion(string store, string grainKey)
    {
        using var connection = new SqliteConnection($"Data Source={_database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM GrainState WHERE StoreName = @StoreName AND GrainId = @GrainId";
        command.Parameters.AddWithValue("@StoreName", store);
        command.Parameters.AddWithValue("@GrainId", grainKey);
        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task ETag_IsTheStoredGrainStateVersionEncodedAsBase64()
    {
        var grainId = NewGrainId("qa-gap04");
        var store = "actors";
        var first = new GrainState<CounterState>(new CounterState { Value = 1 });
        await _storage.WriteStateAsync(store, grainId, first);

        var readBack = new GrainState<CounterState>();
        await _storage.ReadStateAsync(store, grainId, readBack);

        // What the artifact actually holds, decoded the way GAP-04 says it is encoded.
        var stored = ReadStoredVersion(store, grainId.ToString());
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(stored));

        Assert.Equal(stored, readBack.ETag);   // the API ETag is that column, not something else
        Assert.Equal(stored, first.ETag);
        Assert.Equal("1", decoded);            // GrainStateVersion starts at 1
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("1")), stored);

        // Second write bumps the same column, still base64 of the decimal version.
        var second = new GrainState<CounterState>(new CounterState { Value = 2 }, first.ETag);
        await _storage.WriteStateAsync(store, grainId, second);

        var storedAfter = ReadStoredVersion(store, grainId.ToString());
        Assert.Equal("2", Encoding.UTF8.GetString(Convert.FromBase64String(storedAfter)));
        Assert.Equal(storedAfter, second.ETag);

        var readAfterSecond = new GrainState<CounterState>();
        await _storage.ReadStateAsync(store, grainId, readAfterSecond);
        Assert.Equal(storedAfter, readAfterSecond.ETag);
        Assert.Equal(2, readAfterSecond.State!.Value);
    }
}
