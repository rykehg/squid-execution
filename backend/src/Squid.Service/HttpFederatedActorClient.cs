using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Squid.Api.Posts;
using Squid.Protocols.ActivityPub;

namespace Squid.Service;

/// <summary>
/// Resolution of foreign key material and senders (contract activitypub §3 step 1 and step 7,
/// SPEC-0011 section C): a remote <c>keyId</c> answers through the actor document it points at,
/// and "the sender is resolvable" is the same document answering at all. Declared here so a test
/// can stand in for the network the way the unfurl seam does.
/// </summary>
public interface IFederatedActorClient
{
    /// <summary>The key published at <paramref name="keyId"/>, or null — never throws (FR-040).</summary>
    Task<FederatedKey?> ResolveKeyAsync(string keyId, CancellationToken cancellationToken);

    /// <summary>True when <paramref name="actorUri"/>'s actor document answers with its own id (§3 step 7).</summary>
    Task<bool> ExistsAsync(string actorUri, CancellationToken cancellationToken);
}

/// <summary>
/// The production resolver: fetch the actor document inside the SSRF perimeter (scheme, then every
/// resolved address, then every redirect hop — the unfurl rules, GAP-44's family), releasing a
/// host named by <see cref="TrustedPeerHosts"/> first (SPEC-0011 section C, GAP-49), keep it for a
/// short TTL so one delivery never fetches twice, and read the key the <c>keyId</c> names out of
/// it — RSA as the PEM of contract §1.1 (SPKI DER, what the verifier imports), Ed25519 through the
/// <c>squid:ed25519Key</c> multibase of R-02. Any refusal — unreachable, oversized, hostile, not
/// our document — answers null/false: the inbox refuses, the process never falls (FR-040).
/// shortcut: the TTL is a constant (5 min), not configuration; per-peer tuning is TASK-0046's
/// Peer.RateLimitPerMinute family.
/// </summary>
public sealed class HttpFederatedActorClient : IFederatedActorClient
{
    /// <summary>How long one actor document may be reused (§3 step 7's "cache com TTL").</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private const string ActivityJson = "application/activity+json, application/ld+json";

    private readonly ConcurrentDictionary<string, CachedDocument> _cache = new(StringComparer.Ordinal);

    private readonly HttpClient _http;
    private readonly IUnfurlResolver _resolver;
    private readonly UnfurlOptions _options;
    private readonly TimeProvider _time;
    private readonly TrustedPeerHosts _trusted;

    /// <summary>Binds the resolver to the host's client, DNS seam, fetch budget, clock and trusted peers.</summary>
    public HttpFederatedActorClient(
        HttpClient http, IUnfurlResolver resolver, UnfurlOptions options, TimeProvider time,
        TrustedPeerHosts trusted)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _trusted = trusted ?? throw new ArgumentNullException(nameof(trusted));
    }

    /// <inheritdoc />
    public async Task<FederatedKey?> ResolveKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(keyId, UriKind.Absolute, out var key) || key.Scheme is not ("http" or "https"))
        {
            return null; // a keyId this instance can fetch is an absolute http(s) URI, nothing else
        }

        var fragment = keyId.IndexOf('#');
        var actorUri = fragment >= 0 ? keyId[..fragment] : keyId; // the document, never the fragment
        var document = await FetchAsync(actorUri, cancellationToken);
        if (document is null) return null;

        return KeyOf(document.Root, keyId);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string actorUri, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(actorUri, UriKind.Absolute, out var actor) || actor.Scheme is not ("http" or "https"))
        {
            return false;
        }

        return await FetchAsync(actorUri, cancellationToken) is not null;
    }

    /// <summary>The actor document under <paramref name="actorUri"/>, cached for the TTL — null when it never answers.</summary>
    private async Task<ParsedDocument?> FetchAsync(string actorUri, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(actorUri, out var cached) && cached.Until > _time.GetUtcNow())
        {
            return cached.Document;
        }

        var json = await DownloadAsync(actorUri, cancellationToken);
        var document = Parse(actorUri, json);
        _cache[actorUri] = new CachedDocument(document, _time.GetUtcNow().Add(CacheTtl));
        return document;
    }

    /// <summary>The bytes of the actor document, hop by hop — null on every refusal (never throws).</summary>
    private async Task<string?> DownloadAsync(string actorUri, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.TimeoutMs);

            var current = new Uri(actorUri);
            for (var hop = 0; hop <= RemoteMediaClient.MaxRedirects; hop++)
            {
                if (current.Scheme is not ("http" or "https")) return null;
                if (!await AllowedAsync(current, timeout.Token)) return null;

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.TryAddWithoutValidation("Accept", ActivityJson);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (Redirects(response.StatusCode))
                {
                    if (response.Headers.Location is not { } location) return null;
                    current = new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode) return null;
                return await ReadBoundedAsync(response, timeout.Token);
            }

            return null; // more hops than the budget carries
        }
        catch (Exception)
        {
            return null; // fail-open: a hostile peer may not take the process down
        }
    }

    /// <summary>Parses a fetched document only when it answers with the id we asked for.</summary>
    private static ParsedDocument? Parse(string actorUri, string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var id = root.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            return string.Equals(id, actorUri, StringComparison.Ordinal)
                ? new ParsedDocument(root.Clone())
                : null;
        }
        catch (JsonException)
        {
            return null; // not the JSON-LD we speak
        }
    }

    /// <summary>The key the <paramref name="keyId"/> names inside the document, or null.</summary>
    private static FederatedKey? KeyOf(JsonElement root, string keyId)
    {
        try
        {
            if (root.TryGetProperty("publicKey", out var rsa)
                && rsa.ValueKind == JsonValueKind.Object
                && string.Equals(Text(rsa, "id"), keyId, StringComparison.Ordinal))
            {
                var der = ProfileJsonLd.TryDecodePublicKeyPem(Text(rsa, "publicKeyPem"));
                if (der is null) return null;
                using var key = RSA.Create();
                key.ImportSubjectPublicKeyInfo(der, out _); // validate here so a hostile PEM is a verdict, not a throw
                return new FederatedKey(der, null);
            }

            if (root.TryGetProperty("squid:ed25519Key", out var ed)
                && ed.ValueKind == JsonValueKind.Object
                && string.Equals(Text(ed, "id"), keyId, StringComparison.Ordinal))
            {
                var raw = ProfileJsonLd.TryDecodeEd25519Multibase(Text(ed, "publicKeyMultibase"));
                return raw is null ? null : new FederatedKey(null, raw);
            }
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
        {
            return null; // published material that will not decode is no material at all
        }

        return null;

        static string? Text(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool Redirects(System.Net.HttpStatusCode status) => status is
        System.Net.HttpStatusCode.MovedPermanently or System.Net.HttpStatusCode.Found
        or System.Net.HttpStatusCode.SeeOther or System.Net.HttpStatusCode.TemporaryRedirect
        or System.Net.HttpStatusCode.PermanentRedirect;

    /// <summary>The SSRF check of one hop: a literal judged directly, a name on EVERY answer of the round.</summary>
    private async Task<bool> AllowedAsync(Uri url, CancellationToken cancellationToken)
    {
        var host = url.DnsSafeHost;
        if (host.Length > 1 && host[0] == '[' && host[^1] == ']') host = host[1..^1];
        if (_trusted.Contains(host)) return true; // SPEC-0011 §C: the exact allowlist releases the perimeter
        if (System.Net.IPAddress.TryParse(host, out var literal)) return !SsrfGuard.Blocked(literal);

        var addresses = await _resolver.ResolveAsync(host, cancellationToken);
        return SsrfGuard.Allowed(addresses);
    }

    /// <summary>The document body up to <c>Unfurl:MaxBytes</c>, or null when it does not fit.</summary>
    private async Task<string?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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
            if (room <= 0) break;
            var read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, room)), cancellationToken);
            if (read == 0)
            {
                eof = true;
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        if (!eof && declared != buffer.Length) return null;
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>One cached actor document and the instant it stops being an answer.</summary>
    private sealed record CachedDocument(ParsedDocument? Document, DateTimeOffset Until);

    /// <summary>A parsed actor document — its root already proven to be the id we asked for.</summary>
    private sealed record ParsedDocument(JsonElement Root);
}
