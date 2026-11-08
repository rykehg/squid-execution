using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Squid.Api.Crypto;
using Squid.Api.Recovery;
using Squid.Grains.Actors;

namespace Squid.Api.Auth;

/// <summary>The registration body of contracts/http-api.md §2.</summary>
public sealed record RegisterRequest(string? Handle, string? Password, string? DisplayName);

/// <summary>The login body of contracts/http-api.md §2.</summary>
public sealed record LoginRequest(string? Handle, string? Password);

/// <summary>
/// The auth surface of SPEC-0007: <c>POST /api/auth/register</c> (B-01..B-04), <c>login</c>
/// (B-05), <c>logout</c> and <c>logout-all</c> (B-07), plus the session/CSRF middleware they all
/// depend on (B-06). FR-062 shows in what these routes never ask for: no e-mail, no phone, no
/// password reset — the kit is the only recovery path.
/// The routes are mapped from here rather than from <c>SquidApi.MapSquidApi</c> because that file
/// is outside this task's touches; the composition root calls this single method.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Maps the auth routes and the session/CSRF middleware onto the application.</summary>
    public static WebApplication MapSquidAuth(this WebApplication app)
    {
        app.UseMiddleware<AuthSessionMiddleware>();

        app.MapPost("/api/auth/register", RegisterAsync);
        app.MapPost("/api/auth/login", LoginAsync);
        app.MapPost("/api/auth/logout", LogoutAsync);
        app.MapPost("/api/auth/logout-all", LogoutAllAsync);
        // The actor plus its pending notices (contract §2): the route adjudicated into this file
        // for TASK-0016, so B-13/FR-007 answers where the contract tables it.
        app.MapGet("/api/auth/me", MeAsync);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, ActorGrain actors, IAuthStore store, HttpContext context)
    {
        var handle = request.Handle ?? string.Empty;
        if (!ActorHandle.IsValid(handle))
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "validation_failed", $"handle inválido: a regra é {ActorHandle.Rule}");
        }

        var password = request.Password ?? string.Empty;
        if (password.Length < PasswordHasher.MinLength)
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "validation_failed", $"senha inválida: mínimo de {PasswordHasher.MinLength} caracteres");
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (displayName is { Length: > 80 })
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "validation_failed", "displayName inválido: máximo de 80 caracteres");
        }

        // Validation is over: nothing below writes a row until the actor stream accepts the handle,
        // so a refusal at this point or at the grain leaves the instance untouched (AC-02).
        var passwordHash = PasswordHasher.Hash(password);
        var rootKey = RootKey.Create();
        var keys = KeyState.Create(rootKey, password);

        var material = new ActorKeyMaterial(
            [
                new ActorPublicKey("RSA-2048", "#main-key", Convert.ToBase64String(keys.RsaPublicKey)),
                new ActorPublicKey("Ed25519", "#ed25519", Convert.ToBase64String(keys.Ed25519PublicKey)),
            ],
            Convert.ToBase64String(keys.WrappedRsaPrivateKey));

        ActorState state;
        try
        {
            state = await actors.CreateAsync(handle, displayName, material);
        }
        catch (Exception taken) when (taken is ActorHandleTakenException or ActorStoreConflictException)
        {
            // The second of two simultaneous registrations loses the row insert instead of the
            // existence check; both refusals are the same clear 409 (FR-001).
            return AuthErrors.Write(context, StatusCodes.Status409Conflict,
                "handle_taken", $"handle '{handle}' já está em uso");
        }

        var now = DateTimeOffset.UtcNow;
        var session = AuthSessions.Issue(now);
        var account = new AuthAccount
        {
            PasswordHash = passwordHash,
            Sessions = [session],
            Kit = new KitRecord(
                Convert.ToBase64String(keys.RecoveryCode),
                Convert.ToBase64String(keys.EncryptedRootKey),
                now),
        };
        // shortcut: the actor row is already written at this point, so a failure here leaves a
        // handle that is taken with no credential row — upgrade to a compensating delete when
        // IActorEventStore grows one, and nothing shorter of a real transaction will do.
        await store.CreateAsync(handle, account);

        AuthSessions.WriteCookies(context, handle, session);

        // The kit travels once, here, in the shape contracts/http-api.md §3 gives it (AC-01).
        return Results.Json(new
        {
            handle,
            displayName = state.DisplayName,
            kit = new
            {
                recoveryCredential = RecoveryCredential.Format(keys.RecoveryCode),
                mnemonic = Mnemonic.FromRecoveryCode(keys.RecoveryCode),
                encryptedRootKey = account.Kit!.EncryptedRootKey,
                createdAt = now,
            },
        }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, IAuthStore store, HttpContext context)
    {
        var handle = request.Handle ?? string.Empty;
        var password = request.Password ?? string.Empty;

        // A handle the rule refuses never reaches the store (the row key is the handle), and a
        // handle nobody has is verified against the dummy hash: same work, same body, no account
        // enumeration (B-05, AC-03).
        var account = ActorHandle.IsValid(handle) ? await store.FindAsync(handle) : null;
        var valid = PasswordHasher.Verify(password, account?.PasswordHash ?? PasswordHasher.DummyHash);
        if (!valid || account is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "credenciais inválidas.", withTraceId: false);
        }

        var now = DateTimeOffset.UtcNow;
        var session = AuthSessions.Issue(now);
        var stored = await store.UpdateAsync(handle, current => current with
        {
            Sessions = [.. current.Sessions, session],
        });
        if (!stored)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "credenciais inválidas.", withTraceId: false);
        }

        AuthSessions.WriteCookies(context, handle, session);
        return Results.Ok(new { handle });
    }

    private static async Task<IResult> LogoutAsync(IAuthStore store, HttpContext context)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        // The row is the revocation: the cookie is cleared too, but replaying the old value has to
        // find no session (AC-05), and it finds none because this write removed it.
        await store.UpdateAsync(current.Handle, account => account with
        {
            Sessions = [.. account.Sessions.Where(s => s.Id != current.Session.Id)],
        });
        AuthSessions.ClearCookies(context);
        return Results.Ok();
    }

    private static async Task<IResult> LogoutAllAsync(IAuthStore store, HttpContext context)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        await store.UpdateAsync(current.Handle, account => account with { Sessions = [] });
        AuthSessions.ClearCookies(context);
        return Results.Ok();
    }

    /// <summary>
    /// <c>GET /api/auth/me</c> (contract §2, B-13): the authenticated actor and the notices
    /// FR-007 keeps pending — the kit unverified, or older than 12 months. Neither half ever
    /// carries kit material: the words, the credential and the `RK` stay on their own routes.
    /// </summary>
    private static async Task<IResult> MeAsync(
        HttpContext context, IAuthStore store, IRecoveryStore recovery, ActorGrain actors)
    {
        var current = AuthSessions.Current(context);
        var account = current is null ? null : await store.FindAsync(current.Handle);
        if (current is null || account is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var state = await actors.GetStateAsync(current.Handle);
        var verifiedAt = await recovery.ReadVerifiedAtAsync(current.Handle);
        var warnings = RecoveryWarnings.For(account.Kit, verifiedAt, DateTimeOffset.UtcNow);

        return Results.Ok(new
        {
            handle = current.Handle,
            displayName = state?.DisplayName,
            warnings = warnings.Select(warning => new { code = warning.Code, message = warning.Message }),
        });
    }
}
