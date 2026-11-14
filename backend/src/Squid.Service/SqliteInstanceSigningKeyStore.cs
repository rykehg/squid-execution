using Microsoft.Data.Sqlite;
using Squid.Api.Crypto;

namespace Squid.Service;

/// <summary>
/// <see cref="IInstanceSigningKeyStore"/> over the <c>instance_signing_keys</c> table of
/// <c>0009_instance_signing_keys.sql</c> (SPEC-0011 section B, TASK-0051): one row per local
/// handle holding only the wrapped copy the boot secret opens — written by registration, read at
/// activation, never served by a route. The raw ADO pattern of <see cref="SqlitePeerDeliveryStore"/>
/// over the same migrated file.
/// </summary>
public sealed class SqliteInstanceSigningKeyStore : IInstanceSigningKeyStore
{
    private readonly string _connectionString;

    /// <summary>Binds the store to the connection the host already migrates and opens.</summary>
    public SqliteInstanceSigningKeyStore(string connectionString) =>
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    /// <inheritdoc />
    public async Task SaveAsync(string handle, string wrappedSigningKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentNullException.ThrowIfNull(wrappedSigningKey);

        await using var connection = await OpenAsync();
        await using var insert = connection.CreateCommand();
        // INSERT OR REPLACE: one row per handle, and a registration that somehow races its own
        // copy leaves the fresh wrap — never two rows and never a half-updated one.
        insert.CommandText =
            "INSERT OR REPLACE INTO instance_signing_keys (handle, wrapped_signing_key) "
            + "VALUES (@Handle, @Wrapped)";
        insert.Parameters.AddWithValue("@Handle", handle);
        insert.Parameters.AddWithValue("@Wrapped", wrappedSigningKey);
        await insert.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task<string?> FindAsync(string handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        await using var connection = await OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT wrapped_signing_key FROM instance_signing_keys WHERE handle = @Handle";
        query.Parameters.AddWithValue("@Handle", handle);
        return (string?)await query.ExecuteScalarAsync();
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
