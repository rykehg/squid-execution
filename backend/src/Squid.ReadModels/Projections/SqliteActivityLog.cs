using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Orleans.Runtime;

namespace Squid.ReadModels.Projections;

/// <summary>
/// The projection pump's window on the activity log: same file, same row, same delta semantics as
/// <c>IActivityStore.ReadAsync(afterSeq)</c> — every event with <c>Seq &gt; afterSeq</c>, in Seq
/// order. It reads through Dapper because <c>Squid.ReadModels</c> may not reference <c>Squid.Api</c>
/// (storage layer) and <c>Squid.Worker</c> may not reference <c>Squid.Service</c> (a root-to-root
/// edge in the other direction would close a cycle — the dependency rule proves
/// <c>Squid.Service → Squid.Worker</c> the legal way round). One reader per layer, one document on
/// disk; nothing here writes.
/// </summary>
public sealed class SqliteActivityLog
{
    /// <summary>Store name of the log document — the value <c>Squid.Service.SqliteActivityStore</c> writes.</summary>
    public const string StoreName = "activities";

    /// <summary>
    /// The single grain row of the log, derived from the SAME type the store writes with: Orleans
    /// decides the key's shape (measured: <c>activity/log</c>), so a reader never spells it by
    /// hand — the two sides cannot drift apart on a format nobody owns.
    /// </summary>
    private static readonly string GrainKey = GrainId.Create("activity", "log").ToString();

    private readonly string _connectionString;

    /// <summary>Reads the log of the same file the host writes (its schema exists by then).</summary>
    public SqliteActivityLog(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>The delta read of FR-025/R-16: strictly after <paramref name="afterSeq"/>, in order.</summary>
    public async Task<IReadOnlyList<ActivityLogEntry>> ReadAsync(long afterSeq = 0)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var state = await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT State FROM GrainState WHERE StoreName = @StoreName AND GrainId = @GrainId",
            new { StoreName, GrainId = GrainKey });
        if (string.IsNullOrEmpty(state)) return [];

        var document = JsonSerializer.Deserialize<ActivityLogDocument>(state);
        var events = document?.Events;
        return events is null
            ? []
            : events.Where(activity => activity.Seq > afterSeq)
                .OrderBy(activity => activity.Seq)
                .ToList();
    }
}
