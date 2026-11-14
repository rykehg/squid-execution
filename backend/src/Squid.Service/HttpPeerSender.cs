using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Squid.Api.Federation;

namespace Squid.Service;

/// <summary>
/// The production <see cref="IPeerSender"/>: one signed POST to the recipient's inbox, contract
/// activitypub §5/§6 — the body as UTF-8 under <c>application/activity+json</c> and the three
/// headers the pipeline already signed. Any refusal (unreachable, timeout, 4xx/5xx) answers
/// instead of throwing, so a hostile peer can never take the process down (FR-040) and the
/// row simply stays Pending for the motor to schedule. TASK-0046 adds the status half
/// (<see cref="IPeerStatusSender"/>) so the motor can tell a permanent 4xx from a retryable
/// failure, and the budget reads <c>Federation:SendTimeoutMs</c> (default 15 s — the fixed
/// ceiling this class always had) so a suite can exercise the timeout scenario without sleeping.
/// shortcut: no SSRF perimeter here — <c>Federation:TrustedPeerHosts</c> gates the delivery
/// client in TASK-0050.
/// </summary>
public sealed class HttpPeerSender : IPeerSender, IPeerStatusSender
{
    private const string ActivityJson = "application/activity+json";

    /// <summary>The budget one send may take when the configuration says nothing.</summary>
    public const int DefaultSendTimeoutMs = 15_000;

    private readonly HttpClient _http;
    private readonly TimeSpan _budget;

    /// <summary>Binds the sender to the host's HTTP client and to <c>Federation:SendTimeoutMs</c>.</summary>
    public HttpPeerSender(HttpClient http, IConfiguration configuration)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        ArgumentNullException.ThrowIfNull(configuration);
        var budgetMs = int.TryParse(
            configuration["Federation:SendTimeoutMs"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured)
            && configured > 0
            ? configured
            : DefaultSendTimeoutMs;
        _budget = TimeSpan.FromMilliseconds(budgetMs);
    }

    /// <inheritdoc />
    public async Task<bool> SendAsync(
        string inboxUri, string signature, string? digest, string date, string body,
        CancellationToken cancellationToken) =>
        (await SendWithStatusAsync(inboxUri, signature, digest, date, body, cancellationToken)).Accepted;

    /// <inheritdoc />
    public async Task<PeerSendAnswer> SendWithStatusAsync(
        string inboxUri, string signature, string? digest, string date, string body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_budget);

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
            return new PeerSendAnswer(response.IsSuccessStatusCode, (int)response.StatusCode);
        }
        catch (Exception)
        {
            return new PeerSendAnswer(false, null); // fail-open: the queue records the attempt, nothing here may throw
        }
    }
}
