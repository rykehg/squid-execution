using System.Net;
using Squid.Domain.Objects;

namespace Squid.Api.Posts;

/// <summary>
/// The fetch seam behind a remote attachment (SPEC-0011 section E, GAP-44): bytes inside the SSRF
/// perimeter, or null for every reason the download may fail — the post is never rejected for it.
/// Declared at the edge so a test can stand in for the socket; the production implementation below
/// is the guarded fetch the unfurl path already proved.
/// </summary>
public interface IRemoteMediaClient
{
    /// <summary>The bytes of <paramref name="url"/>, or null when the perimeter, the budget or the peer refuses.</summary>
    Task<byte[]?> FetchAsync(string url, CancellationToken cancellationToken);
}

/// <summary>
/// GAP-44's download: the very fetch rules of <see cref="LinkUnfurler"/> — http(s) only, every
/// redirect hop re-validated against <see cref="SsrfGuard"/> before it is asked, one
/// <c>Unfurl:TimeoutMs</c> budget, a body cut at <c>Unfurl:MaxBytes</c> — answering the bytes or
/// null, never an exception (FR-040: a federated payload may not take the process down).
/// shortcut: validation without pinning, the same known limit the unfurl carries — pin the
/// connection to the validated address when GAP-26 grows a requirement for it.
/// </summary>
public sealed class RemoteMediaClient : IRemoteMediaClient
{
    /// <summary>Redirect hops the download may follow before giving up (each hop re-validated).</summary>
    public const int MaxRedirects = 3;

    private readonly HttpClient _http;
    private readonly IUnfurlResolver _resolver;
    private readonly UnfurlOptions _options;

    /// <summary>Binds the download to the host's client, resolver seam and fetch budget.</summary>
    public RemoteMediaClient(HttpClient http, IUnfurlResolver resolver, UnfurlOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<byte[]?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target)) return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.TimeoutMs);

            var current = target;
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (current.Scheme is not ("http" or "https")) return null;
                if (!await AllowedAsync(current, timeout.Token)) return null;

                using var response = await _http.GetAsync(
                    current, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (Redirects(response.StatusCode))
                {
                    if (response.Headers.Location is not { } location) return null;
                    current = new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode) return null;
                return await ReadBoundedAsync(response, timeout.Token);
            }

            return null; // more than MaxRedirects hops — the chain is not followed further
        }
        catch (Exception)
        {
            // Fail-open by contract (GAP-44): timeout, refused connection or a broken response is
            // an absent attachment, never a failed post.
            return null;
        }
    }

    /// <summary>True for the five redirect statuses the unfurl follows by hand.</summary>
    private static bool Redirects(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>The SSRF check of one hop: a literal is judged directly, a name must pass on EVERY answer.</summary>
    private async Task<bool> AllowedAsync(Uri url, CancellationToken cancellationToken)
    {
        var host = url.DnsSafeHost;
        if (host.Length > 1 && host[0] == '[' && host[^1] == ']') host = host[1..^1];
        if (IPAddress.TryParse(host, out var literal)) return !SsrfGuard.Blocked(literal);

        var addresses = await _resolver.ResolveAsync(host, cancellationToken);
        return SsrfGuard.Allowed(addresses);
    }

    /// <summary>The body up to <c>Unfurl:MaxBytes</c>, or null when it does not fit.</summary>
    private async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var declared = response.Content.Headers.ContentLength;
        if (declared > _options.MaxBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var eof = false;
        while (true)
        {
            var room = _options.MaxBytes - buffer.Length;
            if (room <= 0) break; // filled to the cap without seeing the end
            var read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, room)), cancellationToken);
            if (read == 0)
            {
                eof = true;
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        // shortcut: a chunked body of exactly Unfurl:MaxBytes cannot be told from a truncated one
        // and loses its attachment — probe one byte past the cap if a real file ever hits this.
        if (!eof && declared != buffer.Length) return null;
        return buffer.ToArray();
    }
}
