using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Squid.Api.Containers;

namespace Squid.Service;

/// <summary>
/// <see cref="IPolicyAuditStore"/> over the same SQLite <c>IGrainStorage</c> the credential and
/// container rows use (pattern <c>SqliteContainerStore</c>): no new table and no migration — one
/// keyed document holds the trail, and the append is the same read-modify-write under the ETag
/// every store here does. The interface offers Append and Query and nothing that could rewrite a
/// row, which is what "append-only" means at the storage edge (data-model §2.11).
/// </summary>
public sealed class SqlitePolicyAuditStore : IPolicyAuditStore
{
    /// <summary>Store name of the trail rows (one document with every decision).</summary>
    public const string StoreName = "policy-audit";

    /// <summary>Re-reads before giving up when the append races another writer.</summary>
    private const int Attempts = 3;

    private readonly IGrainStorage _storage;

    /// <summary>Binds the trail to the storage the host already created (schema exists with it).</summary>
    public SqlitePolicyAuditStore(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    private static GrainId Row() => GrainId.Create("policyaudit", "trail");

    /// <inheritdoc />
    public async Task AppendAsync(PolicyAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var read = new GrainState<TrailDocument>();
            await _storage.ReadStateAsync(StoreName, Row(), read);

            // shortcut: the whole trail rides in one document, so an append rewrites it and the
            // query sorts in memory — enough for this phase's volume and the only shape the
            // storage offers (IGrainStorage has no scan). Shard the trail by period when the
            // document outgrows a comfortable rewrite; the interface will not change.
            var document = read.State ?? new TrailDocument([]);
            document.Entries.Add(entry);

            var write = new GrainState<TrailDocument>(document, read.ETag);
            try
            {
                await _storage.WriteStateAsync(StoreName, Row(), write);
                return;
            }
            catch (InconsistentStateException)
            {
                // Another decision landed in between: re-read the trail and append to it again.
            }
        }

        throw new IOException($"the policy audit trail kept conflicting after {Attempts} attempts");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PolicyAuditEntry>> QueryAsync(PolicyAuditFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var state = new GrainState<TrailDocument>();
        await _storage.ReadStateAsync(StoreName, Row(), state);
        var entries = state.State?.Entries ?? [];

        // Newest first, insertion line as the stable tiebreak (task gaps table): two decisions in
        // the same instant keep the order they were written in, so paging never skips nor repeats.
        return entries
            .Select((entry, line) => (entry, line))
            .Where(match => Matches(match.entry, filter))
            .OrderByDescending(match => match.entry.At)
            .ThenByDescending(match => match.line)
            .Skip(filter.Offset)
            .Take(filter.Limit)
            .Select(match => match.entry)
            .ToList();
    }

    /// <summary>
    /// The contract's three filters: <c>actor</c> matches the canonical id or the handle behind
    /// it (the caller has either spelling in hand), <c>action</c> is exact, <c>from</c> is an
    /// inclusive instant.
    /// </summary>
    private static bool Matches(PolicyAuditEntry entry, PolicyAuditFilter filter)
    {
        if (filter.Actor is not null
            && !string.Equals(entry.ActorId, filter.Actor, StringComparison.OrdinalIgnoreCase)
            && !entry.ActorId.EndsWith("/users/" + filter.Actor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filter.Action is not null
            && !string.Equals(entry.Action, filter.Action, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filter.From is not null && entry.At < filter.From.Value) return false;

        return true;
    }

    /// <summary>The one row of the trail: a growing list under a single ETag (see the shortcut).</summary>
    private sealed record TrailDocument(List<PolicyAuditEntry> Entries);
}
