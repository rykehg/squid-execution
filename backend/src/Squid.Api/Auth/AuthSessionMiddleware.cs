using Microsoft.AspNetCore.Http;
using Squid.Grains.Actors;

namespace Squid.Api.Auth;

/// <summary>
/// Session and CSRF of every request (SPEC-0007 B-05/B-06): resolves the session cookie once and
/// leaves the result in <see cref="HttpContext.Items"/>, slides the 30-day lifetime, and refuses a
/// mutation that does not carry the session's own CSRF token (403, double submission R-09).
/// CSRF runs only for a mutation that carries a **valid** session: a revoked or absent session is
/// the endpoint's own 401 (AC-05), and register/login are the pre-authentication mutations — there
/// is no cookie to double-submit before the first login (AC-02 keeps them answering 409/422).
/// </summary>
public sealed class AuthSessionMiddleware
{
    private static readonly string[] PreAuthenticationPaths =
    [
        "/api/auth/register",
        "/api/auth/login",
    ];

    private readonly RequestDelegate _next;

    /// <summary>Wraps the rest of the pipeline.</summary>
    public AuthSessionMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Resolves → gates CSRF → slides → next.</summary>
    public async Task InvokeAsync(HttpContext context, IAuthStore store)
    {
        var resolved = await ResolveAsync(context, store);
        if (resolved is not null)
        {
            if (IsMutation(context.Request.Method)
                && !IsPreAuthentication(context.Request.Path)
                && !AuthSessions.CsrfMatches(
                    context.Request.Headers[AuthSessions.CsrfHeader], resolved.Session.CsrfToken))
            {
                await AuthErrors.Write(context, StatusCodes.Status403Forbidden,
                    "csrf_failed", "CSRF ausente ou inválido.").ExecuteAsync(context);
                return;
            }

            context.Items[AuthSessions.ResolvedKey] = resolved;

            // Sliding (B-05): every authenticated request pushes the expiry out to now + 30 days,
            // and re-sends the cookies so the client's window slides with the server's. A route
            // that owns the cookie — register, login and the two logouts — writes its own
            // Set-Cookie right after this one, and the later header is the one a client keeps.
            await store.UpdateAsync(resolved.Handle, account => account with
            {
                Sessions = AuthSessions.Slide(account.Sessions, resolved.Session.Id, DateTimeOffset.UtcNow),
            });
            AuthSessions.WriteCookies(context, resolved.Handle, resolved.Session);
        }

        await _next(context);
    }

    private static async Task<AuthSessions.Resolved?> ResolveAsync(HttpContext context, IAuthStore store)
    {
        if (!context.Request.Cookies.TryGetValue(AuthSessions.SessionCookie, out var cookie)) return null;
        var parsed = AuthSessions.ParseCookie(cookie);
        // The handle is the row key, so a tampered cookie is rejected here instead of reaching the
        // store with whatever string it carries.
        if (parsed is null || !ActorHandle.IsValid(parsed.Value.Handle)) return null;

        var account = await store.FindAsync(parsed.Value.Handle);
        if (account is null) return null;

        var now = DateTimeOffset.UtcNow;
        var session = account.Sessions.Find(s => s.Id == parsed.Value.Id && s.ExpiresAt > now);

        // Sessions that ran out are dropped on the way, so the row of a long-lived account cannot
        // grow forever with the history of its logins.
        if (account.Sessions.Any(s => s.ExpiresAt <= now))
        {
            await store.UpdateAsync(parsed.Value.Handle, current => current with
            {
                Sessions = current.Sessions.Where(s => s.ExpiresAt > now).ToList(),
            });
        }

        return session is null ? null : new AuthSessions.Resolved(parsed.Value.Handle, session);
    }

    private static bool IsMutation(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);

    private static bool IsPreAuthentication(PathString path)
    {
        foreach (var candidate in PreAuthenticationPaths)
        {
            if (path.StartsWithSegments(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
