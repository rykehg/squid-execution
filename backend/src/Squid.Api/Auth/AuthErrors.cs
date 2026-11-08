using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Squid.Api.Auth;

/// <summary>
/// The error envelope of contracts/http-api.md §1: <c>{ "error": { code, message, traceId } }</c>
/// with no internal detail (FR-053). One exception — the login 401 drops <c>traceId</c>, because
/// AC-03 requires the two refusal bodies (unknown handle, wrong password) to be equal, and a
/// per-request id would make them differ for no security gain.
/// </summary>
public static class AuthErrors
{
    /// <summary>Writes the envelope with <paramref name="status"/>; always JSON, never a detail leak.</summary>
    public static IResult Write(HttpContext context, int status, string code, string message, bool withTraceId = true)
    {
        if (!withTraceId)
        {
            return Results.Json(new { error = new { code, message } }, statusCode: status);
        }

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        return Results.Json(new { error = new { code, message, traceId } }, statusCode: status);
    }
}
