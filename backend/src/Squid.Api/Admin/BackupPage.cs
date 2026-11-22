using System.Globalization;
using System.Text.Json;
using Squid.Grains.Backup;

namespace Squid.Api.Admin;

/// <summary>
/// The thin HTML view of SPEC-0012 section G (decision 3 — opção A): one inline document this
/// backend renders and serves — no front build, no asset on disk, no package (AC-04) — showing
/// exactly the fields contract backup-recovery section 6 publishes. Every value is escaped for
/// HTML and every instant is serialized the way the JSON route serializes it, so the page and
/// <c>GET /api/admin/backup/status</c> can never disagree about the state they read. The P-08
/// front replaces this view only; the endpoint behind it stays.
/// </summary>
public static class BackupPage
{
    /// <summary>Renders <paramref name="state"/> plus the redundancy score as one HTML document.</summary>
    public static string Render(BackupStatus state, BackupRedundancy redundancy)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(redundancy);

        var verifiedAt = Escape(Iso(state.LastSuccessAt));
        var envelopes = state.EnvelopesCount.ToString(CultureInfo.InvariantCulture);
        var bytes = state.BytesTotal.ToString(CultureInfo.InvariantCulture);
        var failures = state.VerifyFailures.ToString(CultureInfo.InvariantCulture);
        var score = Escape(redundancy.Score);
        var drillAt = Escape(Iso(state.LastDrillAt));
        var drillResult = Escape(state.LastDrillResult ?? "—");

        return $$"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Backup e redundância</title>
            <style>
              body { font-family: system-ui, sans-serif; margin: 2rem auto; max-width: 36rem; color: #16181d; }
              h1 { font-size: 1.15rem; }
              dl { margin: 0; }
              dt { font-weight: 600; margin-top: .85rem; }
              dd { margin: .15rem 0 0; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; overflow-wrap: anywhere; }
            </style>
            </head>
            <body>
            <h1>Backup e redundância</h1>
            <dl>
            <dt>Último verifiedAt (backup.last_success_at)</dt>
            <dd>{{verifiedAt}}</dd>
            <dt>backup.envelopes_count</dt>
            <dd>{{envelopes}}</dd>
            <dt>backup.bytes_total</dt>
            <dd>{{bytes}}</dd>
            <dt>backup.verify_failures</dt>
            <dd>{{failures}}</dd>
            <dt>redundancy.score</dt>
            <dd>{{score}}</dd>
            <dt>restore.last_drill_at</dt>
            <dd>{{drillAt}}</dd>
            <dt>restore.last_drill_result</dt>
            <dd>{{drillResult}}</dd>
            </dl>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// An instant exactly as <c>GET /api/admin/backup/status</c> writes it (System.Text.Json,
    /// the same serializer both surfaces use), so a value read off the JSON route is found
    /// verbatim on the page — the §G wording "leitura do endpoint JSON acima", made checkable.
    /// </summary>
    private static string Iso(DateTimeOffset? value) =>
        value is null ? "—" : JsonSerializer.Serialize(value.Value).Trim('"');

    /// <summary>
    /// Escapes the four characters HTML parses specially, and nothing else: every value here is
    /// this backend's own text, and <c>System.Net.WebUtility.HtmlEncode</c> would turn each
    /// accented letter of "Redundância" into a numeric entity no reader of the page could match.
    /// </summary>
    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
