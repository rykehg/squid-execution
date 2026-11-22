using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Api.Federation;
using Squid.Protocols.ActivityPub;
using Squid.Tests.Auth;
using Squid.Tests.Backup;
using Squid.Tests.Federation;
using Squid.Tests.Posts;
using Squid.Tests.Projections;
using ActorStore = Squid.Tests.Actors.SqliteActorEventStore;

namespace Squid.Tests.Recovery;

/// <summary>
/// AC-01..AC-04 of TASK-0059 — SPEC-0012 section F closes S-42 on the import: with
/// <c>Squid:RecoveryKey</c> configured a restored kit births the <c>0009</c> instance copy of the
/// actor RSA and the identity federates outbound with no registration at all; without the
/// configuration the answer is the byte-identical one of before (no copy, no error); the copy
/// reaches no row, route or log in clear; and importing the same kit twice answers the same with
/// exactly one row (B-14). The kit is built straight from <see cref="KeyState.Create"/> — the very
/// shape registration persists — so the actor exists nowhere before the import under test creates
/// it. Hosts never overlap (localhost clustering fixes the silo ports), hence the ServiceHost
/// collection. Console capture follows <c>DoubleWrapTests</c>: the Serilog console sink writes to
/// the current <c>Console.Out</c>.
/// </summary>
[Collection("ServiceHost")]
public sealed class ImportInstanceCopyTests
{
    private const string Password = "senha-forte-1234";

    [Fact] // AC-01
    public async Task Ac01_with_the_secret_the_import_births_the_copy_and_the_identity_federates()
    {
        var bootKey = RootKey.Create();
        var storage = NewDb();
        var sender = new FakePeerSender();
        try
        {
            await using var host = await StartAsync(storage,
                Convert.ToBase64String(bootKey.AsSpan()),
                builder => builder.Services.AddSingleton<IPeerSender>(sender));
            var origin = $"{host.Http.BaseAddress!.Scheme}://{host.Http.BaseAddress.Authority}";
            var handle = PostRoutes.Handle("imp01");

            // Sem registro em lugar nenhum: o import É o caminho que cria esta identidade.
            using var restored = await new AuthClient(host.Http)
                .PostAsync("/api/recovery/import", NewKit(handle));
            Assert.Equal(HttpStatusCode.Created, restored.StatusCode);
            Assert.Null(AuthClient.SetCookie(restored, AuthSessions.SessionCookie)); // registro emite sessão; import, não

            // A linha 0009 nasce do import e abre só com o segredo de boot, na MESMA RSA que o
            // perfil restaurado publica (embrulho duplo da SPEC-0011 section B).
            var wrapped = await ProjectionDb.ScalarAsync<string>(storage,
                "SELECT wrapped_signing_key FROM instance_signing_keys WHERE handle = @Handle",
                new { Handle = handle });
            Assert.False(string.IsNullOrWhiteSpace(wrapped));
            using var opened = KeyDerivation.UnwrapRsaPrivateKey(
                bootKey, Convert.FromBase64String(wrapped!));
            var profile = ProfileJsonLd.Materialize(
                await host.Http.GetStringAsync($"/users/{handle}"));
            Assert.Equal(profile.RsaPublicKeyBase64,
                Convert.ToBase64String(opened.ExportSubjectPublicKeyInfo()));

            // A identidade restaurada federada de SAÍDA sem novo registro: o pipeline entrega e o
            // par independente aceita a assinatura rsa-sha256 feita com a chave do ator — a que o
            // segredo de boot desembrulhou da cópia.
            var outbound = host.App.Services.GetRequiredService<IOutboundPipeline>();
            var deliveries = host.App.Services.GetRequiredService<IPeerDeliveryStore>();
            var target = $"https://remote.example/users/{InboxHarness.Unique("imp01t")}";
            await outbound.EnqueueFollowAsync(origin, handle, target);
            var verdict = await outbound.SendAsync(
                Assert.Single(await deliveries.ForRecipientAsync(target)));
            Assert.Equal(PeerDeliveryState.Sent, verdict.State);

            var sent = Assert.Single(sender.Calls);
            Assert.Contains(
                $"keyId=\"{origin}/users/{handle}#main-key\"", sent.Signature,
                StringComparison.Ordinal);
            var uri = new Uri(sent.Inbox);
            var incoming = new IncomingRequest(
                "POST", uri.PathAndQuery, uri.Scheme, uri.Authority,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["signature"] = sent.Signature,
                    ["digest"] = sent.Digest!,
                    ["date"] = sent.Date,
                },
                Encoding.UTF8.GetBytes(sent.Body));
            var verified = await HttpSignatureVerifier.VerifyAsync(
                incoming, _ => Task.FromResult<FederatedKey?>(
                    new FederatedKey(Convert.FromBase64String(profile.RsaPublicKeyBase64), null)));
            Assert.Equal(SignatureRejection.None, verified.Rejection);
        }
        finally
        {
            Cleanup(storage);
        }
    }

    [Fact] // AC-02
    public async Task Ac02_without_the_secret_the_import_answers_as_before_and_births_no_copy()
    {
        var storage = NewDb();
        try
        {
            await using var host = await StartAsync(storage); // nenhum Squid:RecoveryKey no boot
            var handle = PostRoutes.Handle("imp02");

            using var restored = await new AuthClient(host.Http)
                .PostAsync("/api/recovery/import", NewKit(handle));
            Assert.Equal(HttpStatusCode.Created, restored.StatusCode);

            // A mesma resposta JSON de sempre, byte a byte — nem cópia, nem erro, nem campo novo.
            Assert.Equal(
                $$"""{"handle":"{{handle}}","displayName":null}""",
                await restored.Content.ReadAsStringAsync());
            Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(storage,
                "SELECT COUNT(*) FROM instance_signing_keys WHERE handle = @Handle",
                new { Handle = handle }));

            // E o caminho de antes segue intacto: o ator restaurado resolve no perfil.
            Assert.Equal(HttpStatusCode.OK,
                (await host.Http.GetAsync($"/users/{handle}")).StatusCode);
        }
        finally
        {
            Cleanup(storage);
        }
    }

    [Fact] // AC-03
    public async Task Ac03_the_copy_never_leaves_the_table_in_clear_nor_reaches_route_or_log()
    {
        var bootKey = RootKey.Create();
        var storage = NewDb();
        var tap = new LockedWriter();
        var console = Console.Out;
        Console.SetOut(tap);
        try
        {
            await using var host = await StartAsync(storage, Convert.ToBase64String(bootKey.AsSpan()));
            var client = new AuthClient(host.Http);
            var handle = PostRoutes.Handle("imp03");

            using var restored = await client.PostAsync("/api/recovery/import", NewKit(handle));
            Assert.Equal(HttpStatusCode.Created, restored.StatusCode);
            var importBody = await restored.Content.ReadAsStringAsync();
            Assert.True(importBody.Length > 0, "uma varredura sobre corpo vazio não provaria nada");

            var wrapped = await ProjectionDb.ScalarAsync<string>(storage,
                "SELECT wrapped_signing_key FROM instance_signing_keys WHERE handle = @Handle",
                new { Handle = handle });
            Assert.False(string.IsNullOrWhiteSpace(wrapped));
            using var opened = KeyDerivation.UnwrapRsaPrivateKey(
                bootKey, Convert.FromBase64String(wrapped!));

            // Os canais que a AC cobre: as rotas que respondem esta identidade (login → sessão →
            // perfil, me e kit), a própria linha do ator e o log do boot até o import.
            var profile = await host.Http.GetStringAsync($"/users/{handle}");
            Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(handle, Password)).StatusCode);
            using var meResponse = await client.SendAsync(HttpMethod.Get, "/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
            var me = await meResponse.Content.ReadAsStringAsync();
            using var kitResponse = await client.PostAsync("/api/recovery/kit",
                configure: request => request.Headers.TryAddWithoutValidation(
                    AuthSessions.CsrfHeader, client.Csrf!));
            Assert.Equal(HttpStatusCode.Created, kitResponse.StatusCode);
            var kitBody = await kitResponse.Content.ReadAsStringAsync();
            var actorRow = await new ActorStore($"Data Source={storage};Pooling=False")
                .ReadPersistedJsonAsync(handle);
            Assert.NotNull(actorRow);

            // Nunca em claro: nenhuma forma dos bytes privados (nem a própria RK de boot) aparece
            // em linha, resposta ou log — e a cópia embrulhada em si nenhuma rota devolve, pois a
            // rota do kit traz OUTRO valor (o embrulho do ator).
            string[] secrets =
            [
                Convert.ToBase64String(opened.ExportPkcs8PrivateKey()),
                Convert.ToHexString(opened.ExportPkcs8PrivateKey()),
                Convert.ToBase64String(opened.ExportRSAPrivateKey()),
                Convert.ToHexString(opened.ExportRSAPrivateKey()),
                Convert.ToBase64String(bootKey.AsSpan()),
            ];
            var logged = tap.Snapshot();
            var everything = string.Join('\n', importBody, profile, me, kitBody, actorRow!, logged);
            AssertAbsent(everything, secrets);
            foreach (var answer in new[] { importBody, profile, me, kitBody })
            {
                Assert.DoesNotContain(wrapped!, answer, StringComparison.Ordinal);
            }
        }
        finally
        {
            Console.SetOut(console);
            Cleanup(storage);
        }
    }

    [Fact] // AC-04
    public async Task Ac04_importing_the_same_kit_twice_answers_the_same_and_keeps_one_row()
    {
        var storage = NewDb();
        try
        {
            await using var host = await StartAsync(
                storage, Convert.ToBase64String(RootKey.Create().AsSpan()));
            var client = new AuthClient(host.Http);
            var handle = PostRoutes.Handle("imp04");
            var kit = NewKit(handle);

            using var first = await client.PostAsync("/api/recovery/import", kit);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var firstBody = await first.Content.ReadAsStringAsync();

            // B-14: o mesmo kit de novo responde a MESMA identidade — e a tabela 0009, que tem o
            // handle como chave primária e é upsert, fica com exatamente uma linha.
            using var second = await client.PostAsync("/api/recovery/import", kit);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            Assert.Equal(firstBody, await second.Content.ReadAsStringAsync());
            Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(storage,
                "SELECT COUNT(*) FROM instance_signing_keys WHERE handle = @Handle",
                new { Handle = handle }));
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
    private static Task<BackupHost> StartAsync(
        string storage, string? recoveryKey = null, Action<WebApplicationBuilder>? configure = null)
    {
        var args = new List<string>
        {
            "--Storage:Path=" + storage,
            "--Logging:LogLevel:Default=Warning",
            // Pump registered but idle (<= 0): the send of AC-01 is driven by hand, so no
            // background tick can race the single call the assertion counts.
            "--Federation:DeliveryPumpSeconds=0",
        };
        if (recoveryKey is not null)
        {
            args.Add("--Squid:RecoveryKey=" + recoveryKey);
        }

        return BackupHost.StartAsync(args.ToArray(), configure);
    }

    /// <summary>
    /// Fails when any seed appears in the text — raw and with the JSON <c>\uXXXX</c> leaves
    /// decoded (S-25: a base64 `+` travels both ways, so a raw scan alone can miss it).
    /// </summary>
    private static void AssertAbsent(string text, IEnumerable<string> seeds)
    {
        var decoded = Regex.Replace(text, @"\\u([0-9a-fA-F]{4})",
            static match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
        foreach (var seed in seeds.Where(seed => !string.IsNullOrEmpty(seed)))
        {
            Assert.DoesNotContain(seed, text, StringComparison.Ordinal);
            Assert.DoesNotContain(seed, decoded, StringComparison.Ordinal);
        }
    }

    private static string NewDb() => ProjectionDb.NewFile();

    private static void Cleanup(string storage) => ProjectionDb.TryDelete(storage);

    /// <summary>Console capture a live host keeps logging into: writes and reads share one lock.</summary>
    private sealed class LockedWriter : TextWriter
    {
        private readonly StringBuilder _buffer = new();
        private readonly object _gate = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_gate)
            {
                _buffer.Append(value);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_gate)
            {
                _buffer.Append(buffer, index, count);
            }
        }

        public override void Write(string? value)
        {
            lock (_gate)
            {
                _buffer.Append(value);
            }
        }

        public string Snapshot()
        {
            lock (_gate)
            {
                return _buffer.ToString();
            }
        }
    }
}
