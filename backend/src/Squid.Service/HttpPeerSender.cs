using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Squid.Api.Federation;
using Squid.Api.Posts;

namespace Squid.Service;

/// <summary>
/// The production <see cref="IPeerSender"/>: one signed POST to the recipient's inbox, contract
/// activitypub §5/§6 — the body as UTF-8 under <c>application/activity+json</c> and the three
/// headers the pipeline already signed. The inbox host crosses the same SSRF perimeter the other
/// federation clients enforce (SPEC-0011 section C, GAP-49): a host <see cref="TrustedPeerHosts"/>
/// names is admitted, everything else is judged on every resolved answer, so the loopback pairs of
/// the harness and runbook are released ONLY by naming them. Any refusal (outside the perimeter,
/// unreachable, timeout, 4xx/5xx) answers instead of throwing, so a hostile peer can never take
/// the process down (FR-040) and the row simply stays Pending for the motor to schedule.
/// TASK-0046 adds the status half (<see cref="IPeerStatusSender"/>) so the motor can tell a
/// permanent 4xx from a retryable failure, and the budget reads <c>Federation:SendTimeoutMs</c>
/// (default 15 s — the fixed ceiling this class always had) so a suite can exercise the timeout
/// scenario without sleeping.
/// </summary>
public sealed class HttpPeerSender : IPeerSender, IPeerStatusSender
{
    private const string ActivityJson = "application/activity+json";

    /// <summary>The budget one send may take when the configuration says nothing.</summary>
    public const int DefaultSendTimeoutMs = 15_000;

    private readonly HttpClient _http;
    private readonly TimeSpan _budget;
    private readonly IUnfurlResolver _resolver;
    private readonly TrustedPeerHosts _trusted;

    /// <summary>Binds the sender to the host's HTTP client, <c>Federation:SendTimeoutMs</c>, DNS seam and trusted peers.</summary>
    public HttpPeerSender(
        HttpClient http, IConfiguration configuration, IUnfurlResolver resolver, TrustedPeerHosts trusted)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        ArgumentNullException.ThrowIfNull(configuration);
        var budgetMs = int.TryParse(
            configuration["Federation:SendTimeoutMs"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured)
            && configured > 0
            ? configured
            : DefaultSendTimeoutMs;
        _budget = TimeSpan.FromMilliseconds(budgetMs);
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _trusted = trusted ?? throw new ArgumentNullException(nameof(trusted));
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

            if (!await AllowedAsync(inboxUri, timeout.Token))
            {
                // Outside the perimeter and not a trusted peer: it can never be delivered, so the
                // answer is the same "no answer" an unreachable peer gives — Pending, backoff,
                // max_attempts to the DLQ — never a POST to an internal target.
                return new PeerSendAnswer(false, null);
            }

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

    /// <summary>The SSRF check of one delivery: the exact allowlist first, then the perimeter of the unfurl family.</summary>
    private async Task<bool> AllowedAsync(string inboxUri, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(inboxUri, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https"))
        {
            return false; // what the HttpRequestMessage below always refused, decided before any socket
        }

        var host = target.DnsSafeHost;
        if (host.Length > 1 && host[0] == '[' && host[^1] == ']') host = host[1..^1];
        if (_trusted.Contains(host)) return true; // SPEC-0011 §C: the exact allowlist releases the perimeter
        if (IPAddress.TryParse(host, out var literal)) return !SsrfGuard.Blocked(literal);

        var addresses = await _resolver.ResolveAsync(host, cancellationToken);
        return SsrfGuard.Allowed(addresses);
    }
}
