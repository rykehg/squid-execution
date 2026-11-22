using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Protocols.ActivityPub;
using Squid.Tests.Auth;
using Squid.Tests.Backup;
using Squid.Tests.Posts;
using Squid.Tests.Projections;

namespace Squid.Tests.Recovery;

/// <summary>
/// QA-0059 extras beside the four AC scenarios of <see cref="ImportInstanceCopyTests"/>: the
/// instance copy is written only after the identity exists — a foreign kit for a taken handle is
/// a 409 that writes nothing at all (SPEC-0012 section F, the "never on the 409 path" half of
/// AC-01) — and the import wraps with the CURRENT <c>Squid:RecoveryKey</c> after a TASK-0058
/// rotation swapped the ring in memory (observation OBS-01, no AC covers it). The kit is built
/// straight from <see cref="KeyState.Create"/>, the very shape registration persists, so the
/// actor exists nowhere before the import under test creates it. Hosts never overlap
/// (localhost clustering fixes the silo ports), hence the ServiceHost collection.
/// </summary>
[Collection("ServiceHost")]
public sealed class ImportInstanceCopyQaTests
{
    private const string Password = "senha-forte-1234";

    [Fact] // AC-01 (the 409 path)
    public async Task Conflict_path_a_foreign_kit_409s_and_never_writes_its_copy()
    {
        var bootKey = RootKey.Create();
        var storage = NewDb();
        try
        {
            await using var host = await StartAsync(storage, Convert.ToBase64String(bootKey.AsSpan()));
            var handle = PostRoutes.Handle("imp409");
            var client = new AuthClient(host.Http);

            using var first = await client.PostAsync("/api/recovery/import", NewKit(handle));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var wrappedBefore = await ReadCopy(storage, handle);
            Assert.False(string.IsNullOrWhiteSpace(wrappedBefore), "the first import must birth the copy");

            // Another kit, same handle: the identity already exists and the material differs, so
            // the import refuses with 409 — and the row that exists must not be touched by it.
            using var second = await client.PostAsync("/api/recovery/import", NewKit(handle));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Contains(
                "handle_taken", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(storage,
                "SELECT COUNT(*) FROM instance_signing_keys WHERE handle = @Handle",
                new { Handle = handle }));
            var wrappedAfter = await ReadCopy(storage, handle);
            Assert.Equal(wrappedBefore, wrappedAfter); // byte-identical row: the refused kit never landed

            // …and that untouched row still opens into the RSA the profile of the identity serves.
            using var opened = KeyDerivation.UnwrapRsaPrivateKey(
                bootKey, Convert.FromBase64String(wrappedAfter!));
            var profile = ProfileJsonLd.Materialize(
                await host.Http.GetStringAsync($"/users/{handle}"));
            Assert.Equal(
                profile.RsaPublicKeyBase64,
                Convert.ToBase64String(opened.ExportSubjectPublicKeyInfo()));
        }
        finally
        {
            Cleanup(storage);
        }
    }

    [Fact] // OBS-01 (no AC covers the rotation × import interaction)
    public async Task Obs01_after_the_in_memory_rotation_an_import_wraps_with_the_current_key()
    {
        var oldKey = RootKey.Create();
        var newKey = RootKey.Create();
        var storage = NewDb();
        try
        {
            await using var host = await StartAsync(storage, Convert.ToBase64String(oldKey.AsSpan()));
            var client = new AuthClient(host.Http);
            var before = PostRoutes.Handle("impobs1");
            var after = PostRoutes.Handle("impobs2");

            using var one = await client.PostAsync("/api/recovery/import", NewKit(before));
            Assert.Equal(HttpStatusCode.Created, one.StatusCode);
            var wrappedBefore = await ReadCopy(storage, before);
            Assert.False(string.IsNullOrWhiteSpace(wrappedBefore));

            // The rotation swaps the ring in place (SPEC-0012 section E): no restart, the very
            // same singleton the import route resolves keeps signing/wrapping.
            host.App.Services.GetRequiredService<RuntimeSigningKeyRing>().RotateTo(newKey);

            using var two = await client.PostAsync("/api/recovery/import", NewKit(after));
            Assert.Equal(HttpStatusCode.Created, two.StatusCode);
            var wrappedAfter = await ReadCopy(storage, after);
            Assert.False(string.IsNullOrWhiteSpace(wrappedAfter));
            Assert.NotEqual(wrappedBefore, wrappedAfter); // different material: the swap really moved

            // The post-rotation copy opens ONLY with the current key and yields the very RSA the
            // profile publishes; the retired key must refuse it at the AEAD tag.
            Assert.ThrowsAny<CryptographicException>(() =>
                KeyDerivation.UnwrapRsaPrivateKey(oldKey, Convert.FromBase64String(wrappedAfter!)));
            using var opened = KeyDerivation.UnwrapRsaPrivateKey(
                newKey, Convert.FromBase64String(wrappedAfter!));
            var profile = ProfileJsonLd.Materialize(
                await host.Http.GetStringAsync($"/users/{after}"));
            Assert.Equal(
                profile.RsaPublicKeyBase64,
                Convert.ToBase64String(opened.ExportSubjectPublicKeyInfo()));

            // The copy written before the swap keeps opening with the key of its own epoch (R-20).
            using var openedOld = KeyDerivation.UnwrapRsaPrivateKey(
                oldKey, Convert.FromBase64String(wrappedBefore!));
            Assert.NotNull(openedOld);
        }
        finally
        {
            Cleanup(storage);
        }
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>The kit shape registration persists, built straight from <c>KeyState</c> — the actor exists nowhere before the import creates it.</summary>
    private static object NewKit(string handle)
    {
        var keys = KeyState.Create(RootKey.Create(), Password);
        return new
        {
            handle,
            password = Password,
            recoveryCredential = RecoveryCredential.Format(keys.RecoveryCode),
            encryptedRootKey = Convert.ToBase64String(keys.EncryptedRootKey),
            wrappedSigningKey = Convert.ToBase64String(keys.WrappedRsaPrivateKey),
            createdAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>One host under test on a dynamic port; the caller owns the storage file.</summary>
    private static Task<BackupHost> StartAsync(string storage, string? recoveryKey = null)
    {
        var args = new List<string>
        {
            "--Storage:Path=" + storage,
            "--Logging:LogLevel:Default=Warning",
            "--Federation:DeliveryPumpSeconds=0",
        };
        if (recoveryKey is not null)
        {
            args.Add("--Squid:RecoveryKey=" + recoveryKey);
        }

        return BackupHost.StartAsync(args.ToArray());
    }

    private static Task<string?> ReadCopy(string storage, string handle) =>
        ProjectionDb.ScalarAsync<string>(storage,
            "SELECT wrapped_signing_key FROM instance_signing_keys WHERE handle = @Handle",
            new { Handle = handle });

    private static string NewDb() => ProjectionDb.NewFile();

    private static void Cleanup(string storage) => ProjectionDb.TryDelete(storage);
}
