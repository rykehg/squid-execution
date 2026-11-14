using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Squid.PeerClient;

/// <summary>The answers a stand-in peer gives (SPEC-0011 section G, AC-05).</summary>
public enum PeerScenario
{
    /// <summary>202 — the peer accepts every delivery.</summary>
    Ok,

    /// <summary>500 — a retryable failure: the queue goes back to Pending with a backoff (AC-01).</summary>
    ServerError,

    /// <summary>401 — the peer refuses our signature: permanent, DLQ with <c>signature_rejected</c> (SPEC-0011 section C).</summary>
    Unauthorized,

    /// <summary>404 — any other permanent 4xx: DLQ with motive, no blind retry (AC-02).</summary>
    NotFound,

    /// <summary>No answer at all: the send budget expires against a peer that took the request (AC-05).</summary>
    Timeout,

    /// <summary>An answer that comes after the send budget: slowness as a failure (AC-05).</summary>
    Slow,
}

/// <summary>
/// The remote instance without one (SPEC-0011 section G): a loopback HTTP server that plays one
/// <see cref="PeerScenario"/> per run, so the delivery motor is exercised end to end — real
/// <c>HttpPeerSender</c>, real signatures, real budget — without a real peer. Raw sockets instead
/// of a web framework because the interesting scenarios are "never answers" and "answers too
/// late", which a request pipeline cannot express without a timeout of its own; one connection per
/// request, closed with <c>Connection: close</c> so a client never reuses a dead socket.
/// </summary>
public sealed class PeerScenarioServer : IAsyncDisposable
{
    /// <summary>How long the <see cref="PeerScenario.Slow"/> scenario makes a client wait by default.</summary>
    public const int DefaultSlowDelayMs = 3_000;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly TimeSpan _slowDelay;

    private PeerScenarioServer(TcpListener listener, PeerScenario scenario, TimeSpan slowDelay)
    {
        _listener = listener;
        Scenario = scenario;
        _slowDelay = slowDelay;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Origin = new Uri($"http://127.0.0.1:{Port}");
        _loop = AcceptAsync();
    }

    /// <summary>The scenario this instance plays for every request it receives.</summary>
    public PeerScenario Scenario { get; }

    /// <summary>The loopback port the server bound — zero would not be reachable by a delivery.</summary>
    public int Port { get; }

    /// <summary>The origin a test addresses its recipient at (<c>{origin}/users/…</c>).</summary>
    public Uri Origin { get; }

    /// <summary>Requests fully received — how a test proves a pair received nothing (AC-04).</summary>
    public int Requests;

    /// <summary>Starts a scenario on a fresh loopback port — every run is a different peer instance.</summary>
    public static PeerScenarioServer Start(PeerScenario scenario, TimeSpan? slowDelay = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new PeerScenarioServer(
            listener, scenario, slowDelay ?? TimeSpan.FromMilliseconds(DefaultSlowDelayMs));
    }

    /// <summary>Accepts connections until disposal; each one is served on its own so budgets run in parallel.</summary>
    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = ServeAsync(client);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal ends the loop; the in-flight handlers swallow their own hangs.
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
            // Stop() closes the listener under the pending accept — the same end of the loop.
        }
    }

    /// <summary>One request through its scenario: read it fully, then answer, wait, or never answer.</summary>
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                if (!await ReadRequestAsync(stream))
                {
                    return; // the client vanished mid-body — it is already gone
                }

                Interlocked.Increment(ref Requests);
                switch (Scenario)
                {
                    case PeerScenario.Ok:
                        await ReplyAsync(stream, "202 Accepted");
                        break;
                    case PeerScenario.ServerError:
                        await ReplyAsync(stream, "500 Internal Server Error");
                        break;
                    case PeerScenario.Unauthorized:
                        await ReplyAsync(stream, "401 Unauthorized");
                        break;
                    case PeerScenario.NotFound:
                        await ReplyAsync(stream, "404 Not Found");
                        break;
                    case PeerScenario.Timeout:
                        await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token); // the answer that never comes
                        break;
                    case PeerScenario.Slow:
                        await Task.Delay(_slowDelay, _stop.Token);
                        await ReplyAsync(stream, "202 Accepted");
                        break;
                }
            }
            catch (Exception)
            {
                // A client that hung up (or timed out) mid-scenario is an answer, not a crash (FR-040).
            }
        }
    }

    /// <summary>Reads the request head and body; false only when the peer's answer would be guesswork.</summary>
    private static async Task<bool> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        using var head = new MemoryStream();
        var headerEnd = -1;
        var contentLength = 0;
        var received = 0;
        while (headerEnd < 0 || received < headerEnd + 4 + contentLength)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                return headerEnd >= 0;
            }

            head.Write(buffer, 0, read);
            received += read;
            if (headerEnd < 0)
            {
                var bytes = head.GetBuffer();
                headerEnd = IndexOfHeaderEnd(bytes, received);
                if (headerEnd >= 0)
                {
                    contentLength = ContentLengthOf(Encoding.ASCII.GetString(bytes, 0, headerEnd));
                }
            }
        }

        return true;
    }

    /// <summary>Where the blank line that ends the request head starts, or −1 while it has not arrived.</summary>
    private static int IndexOfHeaderEnd(byte[] bytes, int count)
    {
        for (var i = 0; i <= count - 4; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The body length the client announced, 0 when it announced none.</summary>
    private static int ContentLengthOf(string head)
    {
        foreach (var line in head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator > 0
                && line[..separator].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(
                    line[(separator + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var length)
                    ? length
                    : 0;
            }
        }

        return 0;
    }

    /// <summary>One status answer with no body — closed right after, so nothing reuses the socket.</summary>
    private static async Task ReplyAsync(NetworkStream stream, string status)
    {
        var bytes = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception)
        {
            // The accept loop already treats its own shutdown as the normal end.
        }

        _stop.Dispose();
    }
}
