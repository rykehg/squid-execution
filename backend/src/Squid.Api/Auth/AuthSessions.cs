using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Squid.Api.Auth;

/// <summary>
/// Session mechanics of SPEC-0007 B-05/B-06: the two cookies, the tokens they carry and the
/// sliding 30-day lifetime. The session cookie is <c>HttpOnly; Secure; SameSite=Lax</c> and holds
/// <c>&lt;handle&gt;.&lt;id&gt;</c> — the handle only routes the lookup, the id is what authorizes.
/// The csrf cookie is readable by scripts on purpose: double submission needs it (R-09).
/// </summary>
public static class AuthSessions
{
    /// <summary>Cookie carrying the session id (AC-01).</summary>
    public const string SessionCookie = "squid_session";

    /// <summary>Cookie carrying the CSRF token; never HttpOnly.</summary>
    public const string CsrfCookie = "squid_csrf";

    /// <summary>Header every mutation has to carry (B-06, AC-04).</summary>
    public const string CsrfHeader = "X-CSRF-Token";

    /// <summary>Lifetime of a session, renewed by every authenticated request (30 dias deslizantes).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>Key of the resolved session in <see cref="HttpContext.Items"/>.</summary>
    public static readonly object ResolvedKey = new();

    /// <summary>A session the middleware proved valid; endpoints read it instead of re-checking.</summary>
    public sealed record Resolved(string Handle, SessionRecord Session);

    /// <summary>The session of this request, or null when there is none (endpoint answers 401).</summary>
    public static Resolved? Current(HttpContext context) =>
        context.Items.TryGetValue(ResolvedKey, out var value) ? value as Resolved : null;

    /// <summary>A fresh session with a fresh CSRF token, born valid for <see cref="Lifetime"/>.</summary>
    public static SessionRecord Issue(DateTimeOffset now) => new(
        Id: Token(24),
        CsrfToken: Token(32),
        CreatedAt: now,
        ExpiresAt: now.Add(Lifetime));

    /// <summary>Value of the session cookie: the handle routes the row, the id authorizes the caller.</summary>
    public static string CookieValue(string handle, string sessionId) => handle + "." + sessionId;

    /// <summary>Splits the cookie back into handle and id; a malformed value is no session at all.</summary>
    public static (string Handle, string Id)? ParseCookie(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var separator = value.IndexOf('.');
        if (separator <= 0 || separator == value.Length - 1) return null;
        return (value[..separator], value[(separator + 1)..]);
    }

    /// <summary>
    /// The double-submission rule: the header has to carry exactly the token this session was born
    /// with (B-06). Compared in fixed time — the token is the only secret it guards.
    /// </summary>
    public static bool CsrfMatches(string? header, string expected)
    {
        if (string.IsNullOrEmpty(header)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(header), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>The slide: this session lives until now + <see cref="Lifetime"/>, the others keep their expiry.</summary>
    public static List<SessionRecord> Slide(IReadOnlyList<SessionRecord> sessions, string sessionId, DateTimeOffset now)
    {
        var expiresAt = now.Add(Lifetime);
        var slid = new List<SessionRecord>(sessions.Count);
        foreach (var session in sessions)
        {
            slid.Add(session.Id == sessionId ? session with { ExpiresAt = expiresAt } : session);
        }

        return slid;
    }

    /// <summary>Sends both cookies of a new session; <c>HttpOnly</c> only on the session one (B-05, R-09).</summary>
    public static void WriteCookies(HttpContext context, string handle, SessionRecord session)
    {
        // 30 days is the cookie's own window too (B-05): the value outlives the browser session
        // and is pushed out again by every authenticated request that follows.
        var expires = DateTimeOffset.UtcNow.Add(Lifetime);
        context.Response.Cookies.Append(SessionCookie, CookieValue(handle, session.Id), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
            Expires = expires,
        });
        context.Response.Cookies.Append(CsrfCookie, session.CsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
            Expires = expires,
        });
    }

    /// <summary>Expires both cookies client-side; the row is what actually revokes (AC-05).</summary>
    public static void ClearCookies(HttpContext context)
    {
        context.Response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/" });
        context.Response.Cookies.Delete(CsrfCookie, new CookieOptions { Path = "/" });
    }

    private static string Token(int size) =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(size));
}
