using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace Squid.Worker.Backup;

/// <summary>One recorded drill, exactly as the status route publishes it (contract section 6).</summary>
/// <param name="Drill">The drill's name — `restore` for a restore that passed its own verification.</param>
/// <param name="Result">`passed` — a refusal is never recorded as anything else.</param>
/// <param name="RanAt">When it ran, as `restore.last_drill_at` answers.</param>
public sealed record RestoreDrillRecord(string Drill, string Result, DateTimeOffset RanAt);

/// <summary>
/// The ledger behind <c>restore.last_drill_at</c>/<c>restore.last_drill_result</c> (contract
/// backup-recovery section 6: "prova pública de que o backup existe"). It is ONE row of the
/// document store the activity log already owns — no new table, no migration, nothing outside this
/// task's <c>touches</c> — overwritten by the last drill, which is all section 6 publishes.
/// <see cref="RestorePipeline"/> writes it after step 7 passes and only then: a refused restore
/// claims nothing, so the destination a wrong key left intact stays byte-identical (AC-04).
/// </summary>
public sealed class SqliteRestoreDrillLedger
{
    /// <summary>Store name of the ledger row — a parameter of every statement, never inline.</summary>
    public const string StoreName = "restore-drill";

    /// <summary>The single row every drill overwrites (last drill wins, exactly what section 6 reads).</summary>
    public static readonly GrainId Row = GrainId.Create("drill", "latest");

    private readonly IGrainStorage _storage;

    /// <summary>Binds the ledger to the state store of the database the drills run against.</summary>
    public SqliteRestoreDrillLedger(IGrainStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    /// <summary>Records one passed drill as the current one.</summary>
    public async Task RecordAsync(string drill, string result, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(drill);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        ct.ThrowIfCancellationRequested();

        var state = new GrainState<DrillDocument>();
        await _storage.ReadStateAsync(StoreName, Row, state);
        state.State = new DrillDocument(drill, result, DateTimeOffset.UtcNow);
        try
        {
            await _storage.WriteStateAsync(StoreName, Row, state);
        }
        catch (InconsistentStateException)
        {
            // Last drill wins even under a race: re-read the row that moved and write once more.
            await _storage.ReadStateAsync(StoreName, Row, state);
            state.State = new DrillDocument(drill, result, DateTimeOffset.UtcNow);
            await _storage.WriteStateAsync(StoreName, Row, state);
        }
    }

    /// <summary>The last recorded drill, or null when this database has never run one.</summary>
    public async Task<RestoreDrillRecord?> LatestAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = new GrainState<DrillDocument>();
        await _storage.ReadStateAsync(StoreName, Row, state);
        var document = state.State;
        return document is null
            ? null
            : new RestoreDrillRecord(document.Drill, document.Result, document.RanAt);
    }

    /// <summary>The stored document of the row — the shape the status route deserializes.</summary>
    private sealed record DrillDocument(string Drill, string Result, DateTimeOffset RanAt);
}
