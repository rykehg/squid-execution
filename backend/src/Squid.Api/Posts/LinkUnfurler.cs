using System.Net;
using System.Text;
using Squid.Domain.Objects;

namespace Squid.Api.Posts;

/// <summary>
/// The synchronous, fail-open link unfurl of SPEC-0009 section G (GAP-26): a publish whose
/// content IS a single <c>http(s)</c> URL fetches that page inside the create path and turns
/// its OpenGraph triple into an <see cref="Attachment"/> of <see cref="AttachmentKind.Link"/> —
/// no blob, <c>og:image</c> kept as the external URL it is, never downloaded.
/// Every failure mode — refusal by the SSRF guard, DNS or connect error, the 3 s timeout, a
/// body above <c>Unfurl:MaxBytes</c>, a hop beyond the redirect limit — answers null: the post
/// goes out with just its URL (201, never blocked). Field mapping of the preview into §2.6's
/// fixed columns (the record is Domain and this task may not widen it): <c>Url</c> = og:image,
/// <c>Alt</c> = og:title, <c>BlurHash</c> = og:description (free for Link — §2.6 requires
/// neither there), <c>ContentHash</c> = empty because Link has no blob.
/// </summary>
public sealed class LinkUnfurler
{
    /// <summary>Redirect hops the fetch may follow before giving up (GAP-26: each hop re-validated).</summary>
    public const int MaxRedirects = 3;

    private readonly HttpClient _http;
    private readonly IUnfurlResolver _resolver;
    private readonly UnfurlOptions _options;

    /// <summary>Binds the fetcher to the host's client, resolver seam and configuration.</summary>
    public LinkUnfurler(HttpClient http, IUnfurlResolver resolver, UnfurlOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// The unfurl of <paramref name="content"/>: the Link attachment when the whole content is
    /// one http(s) URL and its page fetched inside the perimeter, null otherwise (not a lone URL,
    /// or any failure — fail-open, the publish is never blocked).
    /// </summary>
    public async Task<Attachment?> TryUnfurlAsync(string? content, CancellationToken cancellationToken)
    {
        var target = SingleUrl(content);
        if (target is null) return null;

        try
        {
            // One budget for the whole chain (Unfurl:TimeoutMs), over the caller's own token —
            // a client that hangs up cancels the fetch with it.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.TimeoutMs);

            var current = target;
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                // Every hop re-validated before it is asked: scheme here, resolved IPs next.
                if (current.Scheme is not ("http" or "https")) return null;
                if (!await AllowedAsync(current, timeout.Token)) return null;

                using var response = await _http.GetAsync(
                    current, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (Redirects(response.StatusCode))
                {
                    // The handler never follows on its own: the Location becomes the next hop,
                    // resolved against this one, and the loop re-checks scheme AND addresses.
                    if (response.Headers.Location is not { } location) return null;
                    current = new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode) return null;

                var body = await ReadBoundedAsync(response, timeout.Token);
                if (body is null) return null; // above Unfurl:MaxBytes — abort, fail-open

                return Build(current, response, body.Value);
            }

            return null; // more than MaxRedirects hops — the chain is not followed further
        }
        catch (Exception)
        {
            // Fail-open by contract (GAP-26): a preview may never take the publish down, so every
            // failure the fetch can throw — timeout, refused connection, broken location — is a
            // missing card, never an error to the caller.
            return null;
        }
    }

    /// <summary>
    /// The MVP rule of the task gaps: the unfurl fires only when the content IS one URL — the
    /// whole (trimmed) content parses as an absolute http(s) URI and carries no whitespace, so
    /// a text with an embedded link stays attachment-less. <c>file:</c>, <c>ftp:</c> and friends
    /// never fetch: they do not parse into this rule at all.
    /// </summary>
    public static Uri? SingleUrl(string? content)
    {
        var text = content?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Any(char.IsWhiteSpace)) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme is "http" or "https" && uri.Host.Length > 0 ? uri : null;
    }

    /// <summary>True for the five redirect statuses the unfurl follows by hand.</summary>
    private static bool Redirects(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// The SSRF check of one hop: a literal address is judged directly, a name goes through the
    /// resolver and ALL of its answers must sit inside the perimeter — one bad record refuses
    /// the hop before the handler is ever called.
    /// shortcut: validation without pinning — the client re-resolves the name to connect, so a
    /// rebinding answer between check and connect slips through; pin the connection to the
    /// validated address when GAP-26 grows a requirement for it.
    /// </summary>
    private async Task<bool> AllowedAsync(Uri url, CancellationToken cancellationToken)
    {
        var host = url.DnsSafeHost;
        if (host.Length > 1 && host[0] == '[' && host[^1] == ']') host = host[1..^1];
        if (IPAddress.TryParse(host, out var literal)) return !SsrfGuard.Blocked(literal);

        var addresses = await _resolver.ResolveAsync(host, cancellationToken);
        return SsrfGuard.Allowed(addresses);
    }

    /// <summary>
    /// The body up to <c>Unfurl:MaxBytes</c>, or null when it does not fit: a declared length
    /// above the cap stops before a single byte is read, a streaming body is cut at the cap and
    /// only a body that ended inside it (or matched its declared length exactly) is handed on.
    /// </summary>
    private async Task<(long Bytes, string Html)?> ReadBoundedAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
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

        // shortcut: a chunked body of exactly Unfurl:MaxBytes cannot be told from a truncated
        // one and loses its card — probe one byte past the cap if a real page ever hits this.
        if (!eof && declared != buffer.Length) return null;

        // shortcut: UTF-8 only — the spec fixes no charset; a latin-1 page may garble, never fail.
        return (buffer.Length, Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
    }

    /// <summary>The Link attachment out of the fetched page — §2.6's columns, og values in place.</summary>
    private static Attachment Build(Uri page, HttpResponseMessage response, (long Bytes, string Html) body)
    {
        var og = OpenGraph.Parse(body.Html);
        return new Attachment
        {
            Kind = AttachmentKind.Link,
            ContentHash = string.Empty, // §2.6: Link has no blob, so no hash to index
            MimeType = response.Content.Headers.ContentType?.MediaType ?? "text/html",
            Url = ImageOf(og.Image, page) ?? string.Empty, // og:image stays an external URL — never downloaded
            Alt = og.Title,
            BlurHash = og.Description is { Length: > 0 } description ? description : null,
            SizeBytes = body.Bytes,
        };
    }

    /// <summary>og:image as an absolute http(s) URL against the page that carried it, else null.</summary>
    private static string? ImageOf(string? image, Uri page)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        if (Uri.TryCreate(image, UriKind.Absolute, out var absolute))
            return absolute.Scheme is "http" or "https" ? absolute.ToString() : null;
        return Uri.TryCreate(page, image, out var relative) ? relative.ToString() : null;
    }
}
