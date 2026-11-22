using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Admin;
using Squid.Api.Crypto;
using Squid.Tests.Auth;
using Squid.Tests.Backup;
using Squid.Tests.Posts;
using Squid.Tests.Projections;

namespace Squid.Tests.Recovery;

/// <summary>
/// AC-05's runtime half of TASK-0058, added by QA-0058 (the M9 probe removed
/// <c>ring.RotateTo(newKey)</c> and the whole 640-test gate stayed green — no DEV scenario
/// could fail on the in-place signing-ring swap). The rotation re-wraps every <c>0009</c> copy
/// under the NEW key, so a <see cref="RuntimeSigningKeyRing"/> still holding the OLD one cannot
/// open them: outbound signing would break at the next activation, and a fresh registration
/// would wrap material the next rotation cannot unwrap. This is that can-fail scenario.
/// </summary>
[Collection("ServiceHost")]
public sealed class KeyRotationRingSwapTests
{
    private const string Password = PostRoutes.Password;

    [Fact] // AC-05 (runtime signing ring swapped in place, no restart)
    public async Task Ac05_after_rotation_the_runtime_ring_opens_the_rewrapped_copy_and_signs()
    {
        var operatorHandle = PostRoutes.Handle("rotring");
        var storage = NewDb();
        var volume = NewVolume();
        try
        {
            await using var host = await StartAsync(storage, volume, operatorHandle);
            var operatorClient = host.NewClient();
            Assert.Equal(HttpStatusCode.Created,
                (await operatorClient.RegisterAsync(operatorHandle, Password)).StatusCode);

            // One rotation behind the operator gate: every stored copy is re-wrapped under the
            // new RK, and the response is the one-shot kit (its keyring is not needed here).
            var rotate = await PostRoutes.SendAsync(
                operatorClient, HttpMethod.Post, "/api/admin/recovery/rotate", null,
                operatorClient.Csrf);
            Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);

            // The very singleton the routes resolved, swapped in place: it must open the copy
            // the rotation just re-wrapped under the NEW key. With the old root key still in
            // the ring this throws CryptographicException instead of returning the RSA.
            var ring = host.App.Services.GetRequiredService<RuntimeSigningKeyRing>();
            Assert.True(ring.OutputEnabled);
            using var opened = await ring.OpenInstanceCopyAsync(operatorHandle);
            Assert.NotNull(opened);

            // And it signs: the opened half verifies its own challenge (outbound signing lives
            // on exactly this path, with no restart beyond the normal configuration reload).
            var challenge = RandomNumberGenerator.GetBytes(32);
            var signature = opened!.SignData(
                challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.True(opened.VerifyData(
                challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            // A NEW registration after the rotation wraps with the NEW material: the ring's
            // wrap key must agree with the swapped backup key source — with the ring left on
            // the old key the two differ and this unwrap refuses (same AEAD failure the next
            // rotation would hit when it tries to re-wrap such a copy).
            var current = host.App.Services.GetRequiredService<IBackupKeySource>().Resolve();
            Assert.NotNull(current);
            using var fresh = RSA.Create(2048);
            var wrapped = ring.WrapInstanceCopy(fresh);
            Assert.NotNull(wrapped);
            using var roundtrip = KeyDerivation.UnwrapRsaPrivateKey(current!, wrapped!);
            Assert.Equal(fresh.ExportRSAPrivateKey(), roundtrip.ExportRSAPrivateKey());
        }
        finally
        {
            Cleanup(storage, volume);
        }
    }

    private static async Task<BackupHost> StartAsync(
        string storage, string volume, string operatorHandle)
    {
        var recoveryKey = Convert.ToBase64String(RootKey.Create().AsSpan());
        return await BackupHost.StartAsync(
        [
            "--Storage:Path=" + storage,
            "--Logging:LogLevel:Default=Warning",
            "--Instance:Operators=" + operatorHandle,
            "--Backup:Local:Path=" + volume,
            "--Backup:IntervalMinutes=0",
            "--Squid:RecoveryKey=" + recoveryKey,
        ]);
    }

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"squid-rotate-ring-{Guid.NewGuid():N}.db");

    private static string NewVolume() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"squid-rotate-ring-{Guid.NewGuid():N}")).FullName;

    private static void Cleanup(string storage, string volume)
    {
        ProjectionDb.TryDelete(storage);
        try
        {
            Directory.Delete(volume, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a product failure; the OS cleans it up.
        }
    }
}
