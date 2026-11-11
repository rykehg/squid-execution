using Dapper;
using Microsoft.Data.Sqlite;

namespace Squid.Tests.Projections;

/// <summary>
/// Direct reads of the artifacts of TASK-0024 — the projection tables, the counter, the ledger and
/// <c>schema_version</c> — straight from the host's SQLite file: a test asserts what actually
/// landed on disk, never what the object under test reports about itself.
/// </summary>
internal static class ProjectionDb
{
    /// <summary>The connection the host itself uses (Pooling off, so the file can be deleted after).</summary>
    public static string ConnectionOf(string storagePath) => $"Data Source={storagePath};Pooling=False";

    /// <summary>A brand-new empty database file path; the caller owns deleting it.</summary>
    public static string NewFile() =>
        Path.Combine(Path.GetTempPath(), $"squid-projections-{Guid.NewGuid():N}.db");

    /// <summary>Best effort removal of a temp file (a leftover is not a product failure).</summary>
    public static void TryDelete(string storagePath)
    {
        try
        {
            File.Delete(storagePath);
        }
        catch (IOException)
        {
            // The OS cleans it up.
        }
    }

    /// <summary>One scalar of <paramref name="sql"/>; <c>param</c> is the anonymous object of its placeholders.</summary>
    public static async Task<T?> ScalarAsync<T>(string storagePath, string sql, object? param = null)
    {
        await using var connection = await OpenAsync(storagePath);
        return await connection.ExecuteScalarAsync<T>(sql, param);
    }

    /// <summary>All rows of <paramref name="sql"/> (Dapper binds the columns to the record's constructor).</summary>
    public static async Task<List<T>> QueryAsync<T>(string storagePath, string sql, object? param = null)
    {
        await using var connection = await OpenAsync(storagePath);
        return (await connection.QueryAsync<T>(sql, param)).ToList();
    }

    /// <summary>Whether a table exists in the file (sqlite_master is the only source that cannot lie).</summary>
    public static Task<bool> TableExistsAsync(string storagePath, string name) =>
        ExistsAsync(storagePath, "table", name);

    /// <summary>Whether an index exists — AC-01 pins one per projection table, on seq.</summary>
    public static Task<bool> IndexExistsAsync(string storagePath, string name) =>
        ExistsAsync(storagePath, "index", name);

    private static async Task<bool> ExistsAsync(string storagePath, string type, string name)
    {
        await using var connection = await OpenAsync(storagePath);
        var found = await connection.ExecuteScalarAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = @Type AND name = @Name",
            new { Type = type, Name = name });
        return found is not null;
    }

    private static async Task<SqliteConnection> OpenAsync(string storagePath)
    {
        var connection = new SqliteConnection(ConnectionOf(storagePath));
        await connection.OpenAsync();
        return connection;
    }
}
