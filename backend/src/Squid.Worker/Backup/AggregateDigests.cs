using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Squid.Api.Posts;

namespace Squid.Worker.Backup;

/// <summary>One aggregate's shape after a restore: how many events it holds and their fingerprint.</summary>
/// <param name="Count">Events of the aggregate.</param>
/// <param name="Hash">SHA-256 of the aggregate's canonical JSON, in <c>Seq</c> order.</param>
public sealed record AggregateDigest(long Count, string Hash);

/// <summary>
/// Contract backup-recovery section 4 step 7 — counts and hashes <b>per aggregate</b>: the events
/// grouped by the object they belong to, each group fingerprinted over its canonical JSON in Seq
/// order, so one lost or altered event changes exactly that aggregate's hash. SC-009's "100% dos
/// hashes idênticos" is then the single comparison <see cref="Matches"/> makes, on both sides of a
/// restore: what the payload claims and what the destination holds.
/// </summary>
public static class AggregateDigests
{
    /// <summary>The digests of <paramref name="events"/>, keyed by the aggregate (object) each belongs to.</summary>
    public static IReadOnlyDictionary<string, AggregateDigest> Of(IEnumerable<ActivityEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events
            .GroupBy(activity => activity.ObjectId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group.OrderBy(activity => activity.Seq).ToList();
                    var canonical = string.Join('\n', ordered.Select(
                        activity => JsonSerializer.Serialize(activity, BackupEnvelope.JsonOptions)));
                    var hash = Convert.ToHexStringLower(
                        SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
                    return new AggregateDigest(ordered.Count, hash);
                },
                StringComparer.Ordinal);
    }

    /// <summary>True when both sides hold the same aggregates with the same counts and hashes.</summary>
    public static bool Matches(
        IReadOnlyDictionary<string, AggregateDigest> expected,
        IReadOnlyDictionary<string, AggregateDigest> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (expected.Count != actual.Count) return false;
        return expected.All(pair =>
            actual.TryGetValue(pair.Key, out var found) && found == pair.Value);
    }
}
