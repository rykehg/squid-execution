using System.Text;
using Squid.Api.Federation;

namespace Squid.Service;

/// <summary>
/// The production <see cref="IPeerSender"/>: one signed POST to the recipient's inbox, contract
/// activitypub §5/§6 — the body as UTF-8 under <c>application/activity+json</c> and the three
/// headers the pipeline already signed. Any refusal (unreachable, timeout, 4xx/5xx) answers
/// false instead of throwing, so a hostile peer can never take the process down (FR-040) and the
/// row simply stays Pending for the motor to schedule.
/// shortcut: no SSRF perimeter here — <c>Federation:TrustedPeerHosts</c> gates the delivery
/// client in TASK-0050, and the per-pair budget/backoff belongs to TASK-0046; the fixed 15 s
/// below only keeps one send from hanging a caller forever.
/// </summary>
public sealed class HttpPeerSender : IPeerSender
{
    private const string ActivityJson = "application/activity+json";

    private static readonly TimeSpan SendBudget = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;

    /// <summary>Binds the sender to the host's HTTP client (the one LinkUnfurler and the actor resolver share).</summary>
    public HttpPeerSender(HttpClient http) =>
        _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <inheritdoc />
    public async Task<bool> SendAsync(
        string inboxUri, string signature, string? digest, string date, string body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendBudget);

            using var request = new HttpRequestMessage(HttpMethod.Post, inboxUri)
            {
                Content = new StringContent(body, Encoding.UTF8, ActivityJson),
            };
            request.Headers.TryAddWithoutValidation("Signature", signature);
            if (digest is not null)
            {
                request.Headers.TryAddWithoutValidation("Digest", digest);
            }

            request.Headers.TryAddWithoutValidation("Date", date);
            using var response = await _http.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false; // fail-open: the queue records the attempt, nothing here may throw
        }
    }
}
