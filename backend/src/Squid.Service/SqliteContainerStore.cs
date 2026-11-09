using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Containers;
using Squid.Domain.Containers;

namespace Squid.Service;

/// <summary>
/// <see cref="IContainerStore"/> over the same SQLite <c>IGrainStorage</c> the credential rows use
/// (pattern <c>SqliteAuthStore</c>: no new table and no migration — the schema of TASK-0003
/// already stores any keyed document). One row per container keyed by its slug, one row of
/// memberships per container beside it. The write guard is TASK-0003's rule verbatim: a write
/// only lands when the stored ETag still matches, compared in <c>WHERE Version = @Version</c>.
/// One deliberate difference from <c>SqliteAuthStore</c>: a container write never retries over a
/// stale expectation — AC-05 needs the rejected write to report the conflict and leave the state
/// exactly as it was.
/// </summary>
public sealed class SqliteContainerStore : IContainerStore
{
    /// <summary>Store name of the container rows (one document per slug).</summary>
    public const string StoreName = "containers";

    /// <summary>Store name of the membership rows (one document per container).</summary>
    public const string MembersStoreName = "container-members";

    /// <summary>Re-reads before giving up when an unconditional write is raced by another writer.</summary>
    private const int Attempts = 3;

    private readonly IGrainStorage _storage;

    /// <summary>The same file the storage writes, for the one read IGrainStorage cannot make (FR-016).</summary>
    private readonly string _connectionString;

    /// <summary>Binds the store to the storage the host already created (schema exists with it).</summary>
    public SqliteContainerStore(IGrainStorage storage, string connectionString)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    private static GrainId ContainerRow(string slug) => GrainId.Create("container", slug);

    private static GrainId MembersRow(string slug) => GrainId.Create("members", slug);

    /// <inheritdoc />
    public async Task<ContainerState?> FindAsync(string slug)
    {
        var state = new GrainState<Container>();
        await _storage.ReadStateAsync(StoreName, ContainerRow(slug), state);
        return state.State is null ? null : new ContainerState(state.State, Version(state.ETag));
    }

    /// <inheritdoc />
    public async Task<ContainerState?> CreateAsync(Container container)
    {
        ArgumentNullException.ThrowIfNull(container);

        // A null ETag only ever inserts (SqliteGrainStorage): an existing slug row is the
        // duplicate of GAP-16, reported as a refusal with nothing overwritten.
        var state = new GrainState<Container>(container, null);
        try
        {
            await _storage.WriteStateAsync(StoreName, ContainerRow(container.Slug), state);
        }
        catch (InconsistentStateException)
        {
            return null;
        }

        return new ContainerState(state.State, Version(state.ETag));
    }

    /// <inheritdoc />
    public async Task<ContainerState?> UpdateAsync(
        string slug, int? expectedVersion, Func<Container, Container> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<Container>();
            await _storage.ReadStateAsync(StoreName, ContainerRow(slug), read);
            if (read.State is null) return null;

            // The expectation is checked against the state version before anything is written:
            // a stale If-Match changes nothing, which is the hash-before/hash-after of AC-05.
            if (expectedVersion is not null && expectedVersion.Value != Version(read.ETag))
            {
                return null;
            }

            var write = new GrainState<Container>(mutate(read.State), read.ETag);
            try
            {
                await _storage.WriteStateAsync(StoreName, ContainerRow(slug), write);
                return new ContainerState(write.State, Version(write.ETag));
            }
            catch (InconsistentStateException)
            {
                // Unconditional: re-read and apply again (SqliteAuthStore's convergence). With an
                // expectation the row moved under the caller's feet — that is the conflict.
                if (expectedVersion is not null) return null;
            }
        }

        throw new IOException($"the container row of '{slug}' kept conflicting after {Attempts} attempts");
    }

    /// <inheritdoc />
    public async Task<ContainerMembership?> FindMembershipAsync(string slug, string handle)
    {
        var document = await ReadMembersAsync(slug);
        return document?.Members.Find(member => member.Handle == handle);
    }

    /// <inheritdoc />
    public async Task CreateMembershipAsync(ContainerMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<MembersDocument>();
            await _storage.ReadStateAsync(MembersStoreName, MembersRow(membership.Slug), read);

            var document = read.State ?? new MembersDocument(membership.Slug, []);
            document.Members.Add(membership);

            var write = new GrainState<MembersDocument>(document, read.ETag);
            try
            {
                await _storage.WriteStateAsync(MembersStoreName, MembersRow(membership.Slug), write);
                return;
            }
            catch (InconsistentStateException)
            {
                // Another writer appended in between: re-read its row and append again.
            }
        }

        throw new IOException(
            $"the membership row of '{membership.Slug}' kept conflicting after {Attempts} attempts");
    }

    /// <inheritdoc />
    public async Task<ContainerMembership> UpsertMembershipAsync(ContainerMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<MembersDocument>();
            await _storage.ReadStateAsync(MembersStoreName, MembersRow(membership.Slug), read);

            var document = read.State ?? new MembersDocument(membership.Slug, []);
            var index = document.Members.FindIndex(member => member.Handle == membership.Handle);
            if (index >= 0)
            {
                document.Members[index] = membership; // one link per (container, actor) pair: replace, never duplicate
            }
            else
            {
                document.Members.Add(membership);
            }

            var write = new GrainState<MembersDocument>(document, read.ETag);
            try
            {
                await _storage.WriteStateAsync(MembersStoreName, MembersRow(membership.Slug), write);
                return membership;
            }
            catch (InconsistentStateException)
            {
                // Another writer moved the row in between: re-read it and apply again.
            }
        }

        throw new IOException(
            $"the membership row of '{membership.Slug}' kept conflicting after {Attempts} attempts");
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMembershipAsync(string slug, string handle)
    {
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<MembersDocument>();
            await _storage.ReadStateAsync(MembersStoreName, MembersRow(slug), read);
            if (read.State is null) return false;

            var document = read.State;
            var index = document.Members.FindIndex(member => member.Handle == handle);
            if (index < 0) return false;

            document.Members.RemoveAt(index);

            var write = new GrainState<MembersDocument>(document, read.ETag);
            try
            {
                await _storage.WriteStateAsync(MembersStoreName, MembersRow(slug), write);
                return true;
            }
            catch (InconsistentStateException)
            {
                // Another writer moved the row in between: re-read it and remove again.
            }
        }

        throw new IOException(
            $"the membership row of '{slug}' kept conflicting after {Attempts} attempts");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContainerMembership>> ListMembershipsAsync(string slug)
    {
        var document = await ReadMembersAsync(slug);
        return document?.Members ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Container>> SearchAsync(string query, int limit, int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // shortcut: no SQL index over (Listed, Slug) — the match lives inside the JSON document,
        // so the row itself is read and the rule applied in memory; the store scans only its own
        // rows (StoreName seeks the primary key) and the page is cut after the filter, which is
        // what keeps offset honest. Upgrade to a real index when FR-016 outgrows the table (I-07).
        var matches = new List<Container>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT State FROM GrainState WHERE StoreName = @StoreName";
        command.Parameters.AddWithValue("@StoreName", StoreName);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var container = JsonSerializer.Deserialize<Container>(reader.GetString(0));
            if (container is not null
                && container.IsSearchable
                && (container.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || container.Slug.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(container);
            }
        }

        return matches
            .OrderBy(container => container.Slug, StringComparer.Ordinal)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    private async Task<MembersDocument?> ReadMembersAsync(string slug)
    {
        var state = new GrainState<MembersDocument>();
        await _storage.ReadStateAsync(MembersStoreName, MembersRow(slug), state);
        return state.State;
    }

    /// <summary>The state version behind an ETag: base64 of its decimal form (GAP-04).</summary>
    private static int Version(string? etag) =>
        etag is null
            ? 0
            : int.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(etag)), CultureInfo.InvariantCulture);

    /// <summary>
    /// One row of memberships per container. The list lives outside the aggregate on purpose:
    /// data-model §2.2 forbids a member list inside <see cref="Container"/>'s state.
    /// </summary>
    private sealed record MembersDocument(string Slug, List<ContainerMembership> Members);
}
