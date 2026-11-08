using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Grains.Actors;

namespace Squid.Api.Recovery;

/// <summary>The import body of contracts/http-api.md §3: the kit the client exported, plus who it belongs to and the password that opens it.</summary>
public sealed record ImportRequest(
    string? Handle,
    string? Password,
    string? RecoveryCredential,
    string? EncryptedRootKey,
    string? WrappedSigningKey,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Recovery surface of SPEC-0007 B-12..B-15 (FR-005..FR-007): the kit once over the wire, the
/// verification that silences the FR-007 notice, and the import that rehydrates an identity on a
/// clean instance. The clear `RK` exists only between the Argon2id open and the two derivations
/// below it — no response, row or log line ever sees it (B-03), and the wrong password fails at a
/// typed refusal that has read nothing but the sealed blob (B-15).
/// The routes are mapped from here so <c>Recovery/**</c> owns the whole slice; the composition
/// root calls this single method next to the auth and discovery ones.
/// </summary>
public static class RecoveryEndpoints
{
    /// <summary>Maps the three kit routes onto the application.</summary>
    public static WebApplication MapSquidRecovery(this WebApplication app)
    {
        app.MapPost("/api/recovery/kit", KitAsync);
        app.MapPost("/api/recovery/kit/verify", VerifyAsync);
        app.MapPost("/api/recovery/import", ImportAsync);
        return app;
    }

    /// <summary>
    /// <c>POST /api/recovery/kit</c> (B-12): the kit of the session's own account, in the §3 shape
    /// — 201 with the credential, the 24 words and the sealed `RK`. The words are re-derived from
    /// the `RC` the row keeps, so the route reproduces the very mnemonic registration issued and
    /// no read route exists that could hand any of it out. <c>wrappedSigningKey</c> joins the four
    /// documented fields because data-model §2.12 puts it in the kit and B-14 has no other path to
    /// the RSA private key on a clean instance.
    /// </summary>
    private static async Task<IResult> KitAsync(HttpContext context, IAuthStore store, ActorGrain actors)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var account = await store.FindAsync(current.Handle);
        var state = await actors.GetStateAsync(current.Handle);
        if (account?.Kit is null || state is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status404NotFound,
                "not_found", "kit de recuperação inexistente.");
        }

        var recoveryCode = Convert.FromBase64String(account.Kit.RecoveryCode);
        return Results.Json(new
        {
            recoveryCredential = RecoveryCredential.Format(recoveryCode),
            mnemonic = Mnemonic.FromRecoveryCode(recoveryCode),
            encryptedRootKey = account.Kit.EncryptedRootKey,
            createdAt = account.Kit.CreatedAt,
            wrappedSigningKey = state.Keys.WrappedSigningKey,
        }, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// <c>POST /api/recovery/kit/verify</c> (B-13): the caller proved it kept the kit, so the
    /// timestamp of that proof is recorded and <c>GET /api/auth/me</c> stops warning about it.
    /// </summary>
    private static async Task<IResult> VerifyAsync(
        HttpContext context, IAuthStore store, IRecoveryStore recovery)
    {
        var current = AuthSessions.Current(context);
        if (current is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
                "not_authenticated", "sessão inválida ou expirada.");
        }

        var account = await store.FindAsync(current.Handle);
        if (account?.Kit is null)
        {
            return AuthErrors.Write(context, StatusCodes.Status404NotFound,
                "not_found", "kit de recuperação inexistente.");
        }

        var now = DateTimeOffset.UtcNow;
        await recovery.MarkVerifiedAsync(current.Handle, now);
        return Results.Ok(new { handle = current.Handle, verifiedAt = now });
    }

    /// <summary>
    /// <c>POST /api/recovery/import</c> (B-14): opens the sealed `RK` with the password, rebuilds
    /// both public keys from it, and re-creates the identity — same handle, same Ed25519, same
    /// RSA (FR-006). The AEAD is the only oracle for kit and password: it answers before anything
    /// is written, so a wrong password changes no row (B-15), and importing what is already there
    /// returns the same identity instead of failing (idempotent, B-14).
    /// </summary>
    private static async Task<IResult> ImportAsync(
        ImportRequest request, ActorGrain actors, IAuthStore store, IRecoveryStore recovery,
        HttpContext context)
    {
        var handle = request.Handle ?? string.Empty;
        if (!ActorHandle.IsValid(handle))
        {
            return AuthErrors.Write(context, StatusCodes.Status422UnprocessableEntity,
                "validation_failed", $"handle inválido: a regra é {ActorHandle.Rule}");
        }

        // One refusal for everything the kit is made of — format, seal and password travel in the
        // same envelope and the caller learns nothing about which half it got wrong (like login).
        byte[] encrypted, wrapped, recoveryCode;
        try
        {
            encrypted = Convert.FromBase64String(request.EncryptedRootKey ?? string.Empty);
            wrapped = Convert.FromBase64String(request.WrappedSigningKey ?? string.Empty);
            recoveryCode = ParseRecoveryCredential(request.RecoveryCredential);
        }
        catch (FormatException)
        {
            return RecoveryDenied(context);
        }

        RootKey rootKey;
        try
        {
            rootKey = RootKeyCipher.Decrypt(encrypted, request.Password ?? string.Empty);
        }
        catch (Exception denied) when (denied is CryptographicException or ArgumentException)
        {
            return RecoveryDenied(context);
        }

        ActorKeyMaterial material;
        string wrappedSigningKey;
        try
        {
            using var rsa = KeyDerivation.UnwrapRsaPrivateKey(rootKey, wrapped);
            wrappedSigningKey = Convert.ToBase64String(wrapped);
            material = new ActorKeyMaterial(
                [
                    new ActorPublicKey("RSA-2048", "#main-key",
                        Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())),
                    new ActorPublicKey("Ed25519", "#ed25519",
                        Convert.ToBase64String(KeyDerivation.DeriveEd25519PublicKey(rootKey))),
                ],
                wrappedSigningKey);
        }
        catch (CryptographicException)
        {
            // The right RK over a foreign wrapping: the kit does not belong together (nothing written).
            return RecoveryDenied(context);
        }

        ActorState state;
        try
        {
            state = await actors.CreateAsync(handle, displayName: null, material);
        }
        catch (Exception taken) when (taken is ActorHandleTakenException or ActorStoreConflictException)
        {
            var existing = await actors.GetStateAsync(handle);
            if (existing is null) throw; // a refused create implies the stream; nothing is half-written
            if (!material.Equals(existing.Keys))
            {
                return AuthErrors.Write(context, StatusCodes.Status409Conflict,
                    "handle_taken", $"handle '{handle}' já está em uso");
            }

            state = existing; // importing the same kit again answers with the same identity (B-14)
        }

        var account = await store.FindAsync(handle);
        if (account is null)
        {
            // Same shortcut registration already takes (TASK-0014): the actor row exists at this
            // point and a failure here leaves a taken handle without a credential row — only a real
            // transaction fixes that, and it is still out of reach of this composition root.
            await store.CreateAsync(handle, new AuthAccount
            {
                PasswordHash = PasswordHasher.Hash(request.Password ?? string.Empty),
                Sessions = [],
                Kit = new KitRecord(
                    Convert.ToBase64String(recoveryCode),
                    Convert.ToBase64String(encrypted),
                    request.CreatedAt ?? DateTimeOffset.UtcNow),
            });
        }

        // Holding the kit with the password is the proof FR-007 asks for: the import marks it verified.
        await recovery.MarkVerifiedAsync(handle, DateTimeOffset.UtcNow);

        return Results.Json(
            new { handle, displayName = state.DisplayName },
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>The wire `RC-1A2B3C4D-…` back into the bytes it was formatted from — the inverse of <see cref="RecoveryCredential.Format"/>.</summary>
    private static byte[] ParseRecoveryCredential(string? value)
    {
        if (value is null || !value.StartsWith("RC-", StringComparison.Ordinal))
        {
            throw new FormatException("recoveryCredential fora do formato RC-");
        }

        var hex = value["RC-".Length..].Replace("-", string.Empty, StringComparison.Ordinal);
        var bytes = Convert.FromHexString(hex); // FormatException em hex inválido
        if (bytes.Length != KeyState.RecoveryCodeSizeInBytes)
        {
            throw new FormatException("recoveryCredential com tamanho inesperado");
        }

        return bytes;
    }

    /// <summary>The one typed refusal of an import attempt: no detail of which half failed (contract §7).</summary>
    private static IResult RecoveryDenied(HttpContext context) =>
        AuthErrors.Write(context, StatusCodes.Status401Unauthorized,
            "recovery_failed", "kit ou senha inválidos.");
}
