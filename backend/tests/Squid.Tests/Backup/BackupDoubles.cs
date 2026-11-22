using Microsoft.Extensions.Logging;
using Squid.Api.Admin;
using Squid.Api.Crypto;
using Squid.Grains.Backup;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>The payload a fake state source hands over — the Worker serializes whatever it gets.</summary>
internal sealed record StateSnapshot(string Marker, int Items);

/// <summary>
/// The aggregate side behind <see cref="IBackupStateSource"/>, scripted so a test can assert the
/// ORDER of the contract section 3 flow the coordinator must run: sequence first, snapshot second.
/// </summary>
internal sealed class FakeStateSource : IBackupStateSource
{
    private readonly object _snapshot;

    public FakeStateSource(object snapshot) => _snapshot = snapshot;

    /// <summary>What <c>LastConsolidatedSeqAsync</c> answers; a test moves it between runs.</summary>
    public long SeqTo { get; set; }

    /// <summary>Every call in order, as <c>method:instanceId:window</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>When set, the next call throws it — the failure path of AC-06.</summary>
    public Exception? Failure { get; set; }

    public Task<long> LastConsolidatedSeqAsync(string instanceId, CancellationToken ct)
    {
        Calls.Add($"seq:{instanceId}:{SeqTo}");
        return Failure is null
            ? Task.FromResult(SeqTo)
            : Task.FromException<long>(Failure);
    }

    public Task<object> SnapshotAsync(string instanceId, long seqFrom, long seqTo, CancellationToken ct)
    {
        Calls.Add($"snapshot:{instanceId}:{seqFrom}..{seqTo}");
        return Failure is null
            ? Task.FromResult(_snapshot)
            : Task.FromException<object>(Failure);
    }
}

/// <summary>The instance `RK` a test supplies instead of the configured <c>Squid:RecoveryKey</c>.</summary>
internal sealed class TestKeySource : IBackupKeySource
{
    public RootKey? Key { get; set; } = RootKey.Create();

    public RootKey? Resolve() => Key;
}

/// <summary>A destination that is down: ListAsync answers "nothing sent", PutAsync fails.</summary>
internal sealed class BrokenTarget : IBackupTarget
{
    public string TargetId => "local-disk";

    public Task PutAsync(BackupEnvelope envelope, System.IO.Stream ciphertext, CancellationToken ct) =>
        throw new IOException("the configured backup volume is unavailable");

    public Task<System.IO.Stream> GetAsync(string envelopeId, CancellationToken ct) =>
        throw new IOException("the configured backup volume is unavailable");

    public Task<IReadOnlyList<BackupEnvelope>> ListAsync(long fromSeq, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BackupEnvelope>>([]);

    public Task<bool> ExistsAsync(string envelopeId, CancellationToken ct) => Task.FromResult(false);

    public Task DeleteAsync(string envelopeId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// A destination whose re-read comes back tampered — the divergence that marks `Corrupt`. It
/// forwards the S-47 state seam exactly as the production composition does (the real target IS
/// the writer), so the header on the volume carries the decided `Corrupt` state a restore reads
/// back — the tampering itself only exists between the send and the re-read.
/// </summary>
internal sealed class FlippingTarget : IBackupTarget, IEnvelopeStateWriter
{
    private readonly IBackupTarget _inner;

    public FlippingTarget(IBackupTarget inner) => _inner = inner;

    public string TargetId => _inner.TargetId;

    public Task PutAsync(BackupEnvelope envelope, System.IO.Stream ciphertext, CancellationToken ct) =>
        _inner.PutAsync(envelope, ciphertext, ct);

    public async Task<System.IO.Stream> GetAsync(string envelopeId, CancellationToken ct)
    {
        await using var stored = await _inner.GetAsync(envelopeId, ct);
        using var buffer = new MemoryStream();
        await stored.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        bytes[0] ^= 0xFF;
        return new MemoryStream(bytes, writable: false);
    }

    public Task<IReadOnlyList<BackupEnvelope>> ListAsync(long fromSeq, CancellationToken ct) =>
        _inner.ListAsync(fromSeq, ct);

    public Task<bool> ExistsAsync(string envelopeId, CancellationToken ct) =>
        _inner.ExistsAsync(envelopeId, ct);

    public Task DeleteAsync(string envelopeId, CancellationToken ct) =>
        _inner.DeleteAsync(envelopeId, ct);

    public Task WriteEnvelopeStateAsync(BackupEnvelope envelope, CancellationToken ct) =>
        _inner is IEnvelopeStateWriter writer
            ? writer.WriteEnvelopeStateAsync(envelope, ct)
            : Task.CompletedTask;
}

/// <summary>An <c>ILogger&lt;T&gt;</c> that keeps every formatted line, for the alerts of section 6.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _lines = [];

    /// <summary>A snapshot of everything logged so far.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return _lines.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var line = formatter(state, exception);
        lock (_lines)
        {
            _lines.Add(line);
        }
    }
}
