using Squid.ReadModels.Quota;

namespace Squid.Worker.Quota;

/// <summary>
/// The reconcile-by-blob-sweep of SPEC-0009 section F (AC-02): the files under the blob volume ARE
/// the truth about how much this instance stores, so one pass overwrites <c>UsedBytes</c> with what
/// the volume really holds and stamps <c>UpdatedAt</c>. The optimistic increment of the upload gate
/// may drift between two sweeps — that drift is the accepted assumption of the task gaps and this
/// is what corrects it. Invoked directly by the tests and driven by
/// <see cref="QuotaReconcileService"/>; never from the upload path.
/// </summary>
public sealed class QuotaReconciler
{
    private readonly IQuotaLedger _ledger;

    private readonly string _blobRoot;

    private readonly TimeProvider _time;

    /// <summary>Wires the sweep to the ledger it writes and the volume it reads (tests pass their own clock).</summary>
    public QuotaReconciler(IQuotaLedger ledger, string blobRoot, TimeProvider? time = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _blobRoot = blobRoot ?? throw new ArgumentNullException(nameof(blobRoot));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Sweeps the volume and rewrites the ledger; returns the byte total it found — the pass the tests invoke.</summary>
    public async Task<long> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        long total = 0;
        if (Directory.Exists(_blobRoot))
        {
            foreach (var file in Directory.EnumerateFiles(_blobRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += new FileInfo(file).Length;
            }
        }

        await _ledger.SetUsedAsync(total, _time.GetUtcNow());
        return total;
    }
}
