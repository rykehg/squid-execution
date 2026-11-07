using Microsoft.Data.Sqlite;

namespace Squid.Service;

/// <summary>
/// Readiness probe for the grain-state SQLite file (SPEC-0002): the file must open read-write
/// without being created and answer a trivial query. A missing, renamed or denied file is "not
/// ready"; no connection outlives the probe.
/// </summary>
public sealed class StorageReadiness(string connectionString)
{
    /// <summary>True when the storage file exists and answers a query right now.</summary>
    public bool IsAccessible()
    {
        try
        {
            var probe = new SqliteConnectionStringBuilder(connectionString)
            {
                // ReadWrite never creates the file: a vanished file has to stay 503.
                Mode = SqliteOpenMode.ReadWrite,
            };
            using var connection = new SqliteConnection(probe.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteScalar();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
