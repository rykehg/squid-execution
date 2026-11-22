using Microsoft.Data.Sqlite;
using Squid.Api.Admin;

namespace Squid.Service;

/// <summary>
/// <see cref="IBackupRedundancySource"/> over the <c>backup_envelope</c> registry of data-model
/// section 2.10 (SPEC-0012 section G): the numerator comes from the ROWS — the honesty rule
/// S-47 established, because the header a destination stores was serialized before the re-read
/// could know the result — and the denominator is the destinations this host is wired to write.
/// <para>
/// One run registers one row PER destination (TASK-0057's fanout), each with its own envelope
/// id, so "the most recent VERIFIED envelope" is found first — the newest <c>verified_at</c>,
/// which names a seq window — and a destination counts when it holds a verified row IN that
/// window. The one exception is its own NEWEST row sitting in that same window unverified: a
/// later run can collide with the window whenever <c>seqTo</c> does not advance (no new
/// activity), and there a stale verified row must not claim what the last attempt failed to
/// deliver — the score says what the registry proves, never what a header claims.
/// </para>
/// It lives in the composition root beside <see cref="SqliteKeyEpochStore"/>: only this layer may
/// open the database (ADR-35), and the edge reaches it through the port Squid.Api declares (the
/// precedent of <see cref="SqliteBackupStateSource"/>).
/// </summary>
public sealed class SqliteBackupRedundancySource : IBackupRedundancySource
{
    private readonly string _connectionString;

    private readonly string _primaryTargetId;

    private readonly string? _offsiteTargetId;

    /// <summary>
    /// Binds the source to the migrated file and to the destinations the host wired:
    /// <paramref name="primaryTargetId"/> is always configured (the local volume), and
    /// <paramref name="offsiteTargetId"/> is null exactly when <c>Backup:S3:*</c> names none.
    /// </summary>
    public SqliteBackupRedundancySource(
        string connectionString, string primaryTargetId, string? offsiteTargetId)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryTargetId);
        _primaryTargetId = primaryTargetId;
        _offsiteTargetId = offsiteTargetId;
    }

    /// <inheritdoc />
    public async Task<BackupRedundancy> ReadAsync(CancellationToken ct)
    {
        var configured = _offsiteTargetId is null
            ? new[] { _primaryTargetId }
            : new[] { _primaryTargetId, _offsiteTargetId };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        // The window of the most recent verified envelope: every verified_at is written in the
        // same "O" spelling, so the text order IS the chronological order, and the seq columns
        // tie-break the two destinations of ONE run, which verify milliseconds apart.
        long windowFrom = 0;
        long windowTo = 0;
        var hasWindow = false;
        await using (var window = connection.CreateCommand())
        {
            window.CommandText =
                "SELECT seq_from, seq_to FROM backup_envelope WHERE verified_at IS NOT NULL "
                + "ORDER BY verified_at DESC, seq_from DESC, seq_to DESC LIMIT 1";
            await using var reader = await window.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                windowFrom = reader.GetInt64(0);
                windowTo = reader.GetInt64(1);
                hasWindow = true;
            }
        }

        if (!hasWindow)
        {
            // Nothing verified anywhere: zero destinations hold it — the honest number, never a
            // guessed 1/1 (contract section 6 publishes what the registry proves).
            return new BackupRedundancy(0, configured.Length, false);
        }

        // Every row of the configured destinations, newest first — created_at decides, rowid
        // breaks the tie of two rows written in the same tick.
        var byTarget = new Dictionary<string, List<Row>>(StringComparer.Ordinal);
        await using (var rows = connection.CreateCommand())
        {
            rows.CommandText =
                "SELECT target_id, seq_from, seq_to, verified_at FROM backup_envelope "
                + "WHERE target_id IN (" + (_offsiteTargetId is null ? "@Primary" : "@Primary, @Offsite") + ") "
                + "ORDER BY created_at DESC, rowid DESC";
            rows.Parameters.AddWithValue("@Primary", _primaryTargetId);
            if (_offsiteTargetId is not null)
            {
                rows.Parameters.AddWithValue("@Offsite", _offsiteTargetId);
            }

            await using var reader = await rows.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var target = reader.GetString(0);
                if (!byTarget.TryGetValue(target, out var list))
                {
                    list = [];
                    byTarget[target] = list;
                }

                list.Add(new Row(reader.GetInt64(1), reader.GetInt64(2), !reader.IsDBNull(3)));
            }
        }

        var verified = 0;
        var offsiteVerified = false;
        foreach (var target in configured)
        {
            if (!byTarget.TryGetValue(target, out var list) || list.Count == 0)
            {
                continue; // configured but never written: it holds no envelope at all
            }

            var newest = list[0]; // ORDER BY created_at DESC put this destination's last attempt here
            var holdsWindow = list.Any(row =>
                row.From == windowFrom && row.To == windowTo && row.Verified);
            var lastAttemptFailedThere = newest.From == windowFrom
                && newest.To == windowTo && !newest.Verified;
            if (!holdsWindow || lastAttemptFailedThere)
            {
                continue;
            }

            verified++;
            if (string.Equals(target, _offsiteTargetId, StringComparison.Ordinal))
            {
                offsiteVerified = true;
            }
        }

        return new BackupRedundancy(verified, configured.Length, offsiteVerified);
    }

    /// <summary>One registry row as this read needs it: the seq window and whether it verified.</summary>
    private sealed record Row(long From, long To, bool Verified);
}
