using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Squid.Api.Auth;
using Squid.Api.Crypto;
using Squid.Api.Federation;
using Squid.Grains.Actors;
using Squid.Protocols.ActivityPub;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Federation;
using Squid.Tests.Posts;
using Squid.Tests.Projections;
using ActorStore = Squid.Tests.Actors.SqliteActorEventStore;

namespace Squid.Tests.ServiceHost;

/// <summary>
/// AC-01..AC-05 of TASK-0051 — the double wrap of SPEC-0011 section B against the host
/// <see cref="SquidServiceHost"/> builds: registration with <c>Squid:RecoveryKey</c> persists ONLY
/// the instance-wrapped copy of the SAME actor RSA in <c>0009_*</c>; an actor born over HTTP
/// federates signed with no workaround (an independent verifier accepts the pair); the kit path of
/// GAP-01 does not move; the copy reaches no row, route or log in clear; and an actor registered
/// before the configuration stays disabled with an auditable warning while one registered after is
/// born signable. Console capture follows <c>FederationRuntimeKeyTests</c> — the Serilog console
/// sink writes to the current <c>Console.Out</c>, and the class shares the ServiceHost collection
/// because localhost clustering fixes the silo ports: every host here starts only after the
/// previous one stopped.
/// </summary>
[Collection("ServiceHost")]
public sealed class DoubleWrapTests
{
    private const string DisabledWarning = "federation output disabled";

    private const string HeldWarning = "signing key unavailable for";

    [Fact] // AC-01
    public async Task Ac01_with_a_secret_the_registration_persists_only_the_wrapped_instance_copy()
    {
        var rootKey = RootKey.Create();
        await using var host = await StartAsync(TempFile(), Convert.ToBase64String(rootKey.AsSpan()));
        var handle = PostRoutes.Handle("ac01");

        using var registered = await new AuthClient(host.Http).RegisterAsync(handle, PostRoutes.Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        // A table of its own (0009), one row per actor, wrapped material only.
        var wrapped = await ProjectionDb.ScalarAsync<string>(host.StoragePath,
            "SELECT wrapped_signing_key FROM instance_signing_keys WHERE handle = @Handle",
            new { Handle = handle });
        Assert.False(string.IsNullOrWhiteSpace(wrapped));

        // The copy opens with the BOOT secret and yields the SAME RSA the actor publishes —
        // this is what makes an HTTP-registered actor signable at activation (AC-02).
        using var opened = KeyDerivation.UnwrapRsaPrivateKey(rootKey, Convert.FromBase64String(wrapped!));
        var state = await host.App.Services.GetRequiredService<ActorGrain>().GetStateAsync(handle);
        var published = Assert.Single(state!.Keys.PublicKeys, key => key.KeyId == "#main-key");
        Assert.Equal("RSA-2048", published.Algorithm);
        Assert.Equal(published.PublicKey, Convert.ToBase64String(opened.ExportSubjectPublicKeyInfo()));

        // And the row is ciphertext: the private bytes it hides appear nowhere in it.
        Assert.DoesNotContain(Convert.ToBase64String(opened.ExportRSAPrivateKey()), wrapped);
        Assert.DoesNotContain(Convert.ToBase64String(opened.ExportPkcs8PrivateKey()), wrapped);
        Assert.DoesNotContain(Convert.ToHexString(opened.ExportRSAPrivateKey()), wrapped);
    }

    [Fact] // AC-01 (sem a config)
    public async Task Ac01_without_the_secret_registration_births_no_copy_and_answers_as_before()
    {
        await using var host = await StartAsync(TempFile()); // no Squid:RecoveryKey anywhere
        var client = new AuthClient(host.Http);
        var handle = PostRoutes.Handle("ac01n");

        using var registered = await client.RegisterAsync(handle, PostRoutes.Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        // Nenhuma cópia nasce: the migration's table exists and holds nothing for this actor.
        Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(host.StoragePath,
            "SELECT COUNT(*) FROM instance_signing_keys WHERE handle = @Handle",
            new { Handle = handle }));

        // Registro = o de hoje: 201, kit completo na forma do contrato §3 e sessão emitida.
        using var body = JsonDocument.Parse(await registered.Content.ReadAsStringAsync());
        var kit = body.RootElement.GetProperty("kit");
        Assert.False(string.IsNullOrWhiteSpace(kit.GetProperty("recoveryCredential").GetString()));
        Assert.Equal(Mnemonic.WordCount, kit.GetProperty("mnemonic").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(kit.GetProperty("encryptedRootKey").GetString()));
        Assert.NotNull(AuthClient.SetCookie(registered, AuthSessions.SessionCookie));
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync($"/users/{handle}")).StatusCode);
    }

    [Fact] // AC-02
    public async Task Ac02_a_registered_actor_federates_signed_and_an_independent_verifier_accepts()
    {
        var sender = new FakePeerSender();
        await using var host = await StartAsync(
            TempFile(), Convert.ToBase64String(RootKey.Create().AsSpan()),
            services => services.AddSingleton<IPeerSender>(sender));
        var origin = $"{host.Http.BaseAddress!.Scheme}://{host.Http.BaseAddress.Authority}";
        var handle = PostRoutes.Handle("ac02");

        // O ator nasce do registro de verdade — nenhum teste escreve mais o ator direto.
        using var registered = await new AuthClient(host.Http).RegisterAsync(handle, PostRoutes.Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var outbound = host.App.Services.GetRequiredService<IOutboundPipeline>();
        var deliveries = host.App.Services.GetRequiredService<IPeerDeliveryStore>();
        var target = $"https://remote.example/users/{InboxHarness.Unique("ac02t")}";
        await outbound.EnqueueFollowAsync(origin, handle, target);
        var row = Assert.Single(await deliveries.ForRecipientAsync(target));

        var verdict = await outbound.SendAsync(row);
        Assert.Equal(PeerDeliveryState.Sent, verdict.State);

        // O que saiu: keyId #main-key do ATOR, rsa-sha256, e nada de contorno em volta.
        var sent = Assert.Single(sender.Calls);
        Assert.Contains($"keyId=\"{origin}/users/{handle}#main-key\"", sent.Signature, StringComparison.Ordinal);
        Assert.Contains("rsa-sha256", sent.Signature, StringComparison.Ordinal);

        // Verificador independente: ele vê só o fio e a chave que o PERFIL publica — consumida pelo
        // leitor canônico do documento, exatamente como um par leria o nosso.
        using var profileResponse = await host.Http.GetAsync($"/users/{handle}");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profileJson = await profileResponse.Content.ReadAsStringAsync();
        using var profile = JsonDocument.Parse(profileJson);
        var publicKey = profile.RootElement.GetProperty("publicKey");
        Assert.Equal($"{origin}/users/{handle}#main-key", publicKey.GetProperty("id").GetString());
        var spki = Convert.FromBase64String(ProfileJsonLd.Materialize(profileJson).RsaPublicKeyBase64);
        Assert.NotEmpty(spki);

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
        var verdict2 = await HttpSignatureVerifier.VerifyAsync(
            incoming, _ => Task.FromResult<FederatedKey?>(new FederatedKey(spki, null)));
        Assert.Equal(SignatureRejection.None, verdict2.Rejection);

        // E o par bruto, sem código nenhum em comum com quem assinou: publicada × assinatura.
        var signingString =
            $"(request-target): post {uri.PathAndQuery}\nhost: {uri.Authority}\n"
            + $"date: {sent.Date}\ndigest: {sent.Digest}";
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(spki!, out _);
        Assert.True(rsa.VerifyData(
            Encoding.UTF8.GetBytes(signingString),
            Convert.FromBase64String(AuthorParameter(sent.Signature, "signature")),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact] // AC-03
    public async Task Ac03_the_kit_path_is_untouched_and_a_restore_devolves_the_same_identity()
    {
        var handle = PostRoutes.Handle("ac03");
        string encrypted, credential, wrapped, mainKeyB64;

        await using (var host = await StartAsync(TempFile(), Convert.ToBase64String(RootKey.Create().AsSpan())))
        {
            var client = new AuthClient(host.Http);
            using var registered = await client.RegisterAsync(handle, PostRoutes.Password);
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            using var body = JsonDocument.Parse(await registered.Content.ReadAsStringAsync());
            var kit = body.RootElement.GetProperty("kit");
            encrypted = kit.GetProperty("encryptedRootKey").GetString()!;
            credential = kit.GetProperty("recoveryCredential").GetString()!;

            // (a) o kit ainda abre com a senha e descreve a MESMA identidade que a instância serve:
            // a Ed25519 derivada dele é a publicada — o embrulho do kit não mudou; o duplo embrulho
            // mora em outra tabela (GAP-01 intacto).
            var actorRk = RootKeyCipher.Decrypt(Convert.FromBase64String(encrypted), PostRoutes.Password);
            var state = await host.App.Services.GetRequiredService<ActorGrain>().GetStateAsync(handle);
            var ed25519 = Assert.Single(state!.Keys.PublicKeys, key => key.KeyId == "#ed25519");
            Assert.Equal(
                ed25519.PublicKey,
                Convert.ToBase64String(KeyDerivation.DeriveEd25519PublicKey(actorRk)));

            // (b) a rota do kit devolve o embrulho do ATOR — abre com o RK do ator, e é OUTRO valor
            // que a cópia de instância guardada na tabela 0009.
            using var exportedResponse = await client.PostAsync("/api/recovery/kit",
                configure: request => request.Headers.TryAddWithoutValidation(
                    AuthSessions.CsrfHeader, client.Csrf!));
            Assert.Equal(HttpStatusCode.Created, exportedResponse.StatusCode);
            using var exported = JsonDocument.Parse(await exportedResponse.Content.ReadAsStringAsync());
            wrapped = exported.RootElement.GetProperty("wrappedSigningKey").GetString()!;
            using var kitKey = KeyDerivation.UnwrapRsaPrivateKey(actorRk, Convert.FromBase64String(wrapped));
            mainKeyB64 = Convert.ToBase64String(kitKey.ExportSubjectPublicKeyInfo());
            Assert.Equal(
                mainKeyB64,
                Assert.Single(state.Keys.PublicKeys, key => key.KeyId == "#main-key").PublicKey);
            Assert.NotEqual(wrapped, await ProjectionDb.ScalarAsync<string>(host.StoragePath,
                "SELECT wrapped_signing_key FROM instance_signing_keys WHERE handle = @Handle",
                new { Handle = handle }));
        }

        // (c) restaurar o kit em uma instância NOVA devolve a MESMA identidade — o caminho da
        // P-02 que as regressões dela cobrem, reexecutado sobre o registro desta task.
        await using (var fresh = await StartAsync(TempFile())) // sem segredo: o kit é recuperação
        {
            var client = new AuthClient(fresh.Http);
            using var imported = await client.PostAsync("/api/recovery/import", new
            {
                handle,
                password = PostRoutes.Password,
                recoveryCredential = credential,
                encryptedRootKey = encrypted,
                wrappedSigningKey = wrapped,
            });
            Assert.Equal(HttpStatusCode.Created, imported.StatusCode);

            using var profileResponse = await fresh.Http.GetAsync($"/users/{handle}");
            Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
            var restored = ProfileJsonLd.Materialize(await profileResponse.Content.ReadAsStringAsync());
            Assert.Equal(mainKeyB64, restored.RsaPublicKeyBase64);
        }
    }

    [Fact] // AC-04 (o material nunca sai)
    public async Task Ac04_the_copy_never_leaves_the_table_and_no_route_or_log_carries_it()
    {
        var rootKey = RootKey.Create();
        var output = new CapturedOutput();
        var console = Console.Out;
        Console.SetOut(output);
        try
        {
            await using var host = await StartAsync(TempFile(), Convert.ToBase64String(rootKey.AsSpan()));
            var client = new AuthClient(host.Http);
            var handle = PostRoutes.Handle("ac04");

            using var registered = await client.RegisterAsync(handle, PostRoutes.Password);
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            var registerBody = await registered.Content.ReadAsStringAsync();

            var wrapped = await ProjectionDb.ScalarAsync<string>(host.StoragePath,
                "SELECT wrapped_signing_key FROM instance_signing_keys WHERE handle = @Handle",
                new { Handle = handle });
            Assert.False(string.IsNullOrWhiteSpace(wrapped));
            using var opened = KeyDerivation.UnwrapRsaPrivateKey(rootKey, Convert.FromBase64String(wrapped!));
            string[] secrets =
            [
                Convert.ToBase64String(opened.ExportPkcs8PrivateKey()),
                Convert.ToHexString(opened.ExportPkcs8PrivateKey()),
                Convert.ToBase64String(opened.ExportRSAPrivateKey()),
                Convert.ToHexString(opened.ExportRSAPrivateKey()),
                Convert.ToBase64String(rootKey.AsSpan()),
            ];

            // Os quatro canais de resposta que a AC cobre, mais a própria linha do ator e o log.
            var profile = await GetStringAsync(host.Http, $"/users/{handle}");
            using var meResponse = await client.SendAsync(HttpMethod.Get, "/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
            var me = await meResponse.Content.ReadAsStringAsync();
            using var kitResponse = await client.PostAsync("/api/recovery/kit",
                configure: request => request.Headers.TryAddWithoutValidation(
                    AuthSessions.CsrfHeader, client.Csrf!));
            Assert.Equal(HttpStatusCode.Created, kitResponse.StatusCode);
            var kitBody = await kitResponse.Content.ReadAsStringAsync();
            var actorRow = await new ActorStore($"Data Source={host.StoragePath};Pooling=False")
                .ReadPersistedJsonAsync(handle);
            Assert.NotNull(actorRow);

            var everything = string.Join('\n',
                registerBody, profile, me, kitBody, actorRow!, output.Snapshot());
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
            }

            // E a cópia em si: nenhum serviço a devolve — nem a rota do kit, que traz outro valor.
            foreach (var answer in new[] { registerBody, profile, me, kitBody })
            {
                Assert.DoesNotContain(wrapped!, answer, StringComparison.Ordinal);
            }
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    [Fact] // AC-04 (sem segredo no boot)
    public async Task Ac04_without_the_secret_activation_stays_auditably_disabled()
    {
        var output = new CapturedOutput();
        var console = Console.Out;
        Console.SetOut(output);
        try
        {
            await using var host = await StartAsync(TempFile()); // no Squid:RecoveryKey
            var handle = PostRoutes.Handle("ac04n");
            using var registered = await new AuthClient(host.Http)
                .RegisterAsync(handle, PostRoutes.Password);
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

            // Ativação recusa auditavelmente como hoje (AC-01/03 da TASK-0042 preservadas):
            // nulo, sem exceção e sem derrubar nada — o aviso do boot é o que registra o estado.
            var grain = host.App.Services.GetRequiredService<ActorGrain>();
            Assert.Null(await grain.ActivateSigningKeyAsync(handle));

            var text = await WaitForOutputAsync(output,
                line => line.Contains(DisabledWarning, StringComparison.Ordinal));
            Assert.Contains(DisabledWarning, text, StringComparison.Ordinal);
            Assert.Contains("Squid:RecoveryKey", text, StringComparison.Ordinal);
            Assert.Contains("\"Level\":\"Warning\"", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    [Fact] // AC-05
    public async Task Ac05_without_a_copy_the_output_stays_disabled_while_a_later_actor_federates()
    {
        var storage = TempFile();
        var rootKey = RootKey.Create();
        var oldHandle = PostRoutes.Handle("ac05o");

        try
        {
            // O ator registrado ANTES do segredo: a primeira subida nem conhece a configuração.
            // O arquivo fica de propósito — é ele que o host da próxima subida vai reabrir.
            await using (var before = await StartAsync(storage, keepStorage: true))
            {
                using var registered = await new AuthClient(before.Http)
                    .RegisterAsync(oldHandle, PostRoutes.Password);
                Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            }
            // O host para antes do próximo nascer: o clustering localhost fixa as portas do silo.

            var output = new CapturedOutput();
            var console = Console.Out;
            Console.SetOut(output);
            try
            {
                var sender = new FakePeerSender();
                await using var after = await StartAsync(
                    storage, Convert.ToBase64String(rootKey.AsSpan()),
                    services => services.AddSingleton<IPeerSender>(sender));
                var origin = $"{after.Http.BaseAddress!.Scheme}://{after.Http.BaseAddress.Authority}";
                var grain = after.App.Services.GetRequiredService<ActorGrain>();
                var outbound = after.App.Services.GetRequiredService<IOutboundPipeline>();
                var deliveries = after.App.Services.GetRequiredService<IPeerDeliveryStore>();

                // Sem cópia a ativação devolve nulo — é o limite aceito no gate GAP-50.
                Assert.Null(await grain.ActivateSigningKeyAsync(oldHandle));

                // E a saída dele fica DESATIVADA com aviso auditável, a fila intacta e o fio quieto.
                var oldTarget = $"https://remote.example/users/{InboxHarness.Unique("ac05a")}";
                await outbound.EnqueueFollowAsync(origin, oldHandle, oldTarget);
                var held = await outbound.SendAsync(
                    Assert.Single(await deliveries.ForRecipientAsync(oldTarget)));
                Assert.Equal(PeerDeliveryState.Pending, held.State);
                Assert.Equal("federation_output_disabled", held.Reason);
                Assert.Empty(sender.Calls);

                var text = await WaitForOutputAsync(output,
                    line => line.Contains(HeldWarning, StringComparison.Ordinal)
                            && line.Contains(oldHandle, StringComparison.Ordinal));
                Assert.Contains("instance signing copy", text, StringComparison.Ordinal);

                // Registrar DEPOIS da config nasce com cópia — e essa federationa de verdade.
                var newHandle = PostRoutes.Handle("ac05n");
                using var later = await new AuthClient(after.Http)
                    .RegisterAsync(newHandle, PostRoutes.Password);
                Assert.Equal(HttpStatusCode.Created, later.StatusCode);
                Assert.False(string.IsNullOrWhiteSpace(await ProjectionDb.ScalarAsync<string>(
                    storage,
                    "SELECT wrapped_signing_key FROM instance_signing_keys WHERE handle = @Handle",
                    new { Handle = newHandle })));
                Assert.NotNull(await grain.ActivateSigningKeyAsync(newHandle));

                var newTarget = $"https://remote.example/users/{InboxHarness.Unique("ac05b")}";
                await outbound.EnqueueFollowAsync(origin, newHandle, newTarget);
                var sent = await outbound.SendAsync(
                    Assert.Single(await deliveries.ForRecipientAsync(newTarget)));
                Assert.Equal(PeerDeliveryState.Sent, sent.State);
                Assert.Single(sender.Calls);
            }
            finally
            {
                Console.SetOut(console);
            }
        }
        finally
        {
            ProjectionDb.TryDelete(storage);
        }
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>A unique temp SQLite file the caller owns (the hosts below delete their own).</summary>
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"squid-doublewrap-{Guid.NewGuid():N}.db");

    /// <summary>One host under test, on a dynamic port, with its own storage file.</summary>
    private static async Task<Host> StartAsync(
        string storage, string? recoveryKey = null, Action<IServiceCollection>? configure = null,
        bool keepStorage = false)
    {
        var args = new List<string>
        {
            "--Storage:Path=" + storage,
            "--Logging:LogLevel:Default=Warning",
        };
        if (recoveryKey is not null)
        {
            args.Add("--Squid:RecoveryKey=" + recoveryKey);
        }

        var app = SquidServiceHost.Build(args.ToArray(),
            configure is null ? null : builder => configure(builder.Services));
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        return new Host(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }, storage, keepStorage);
    }

    private static async Task<string> GetStringAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>One quoted draft-cavage auth parameter, as the wire carried it.</summary>
    private static string AuthorParameter(string header, string name)
    {
        foreach (var chunk in header.Split(','))
        {
            var entry = chunk.Trim();
            var separator = entry.IndexOf('=');
            if (separator <= 0 || !entry[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = entry[(separator + 1)..].Trim();
            return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
        }

        throw new InvalidOperationException($"the signature header carries no {name}");
    }

    /// <summary>Waits until any line of the captured output matches, returning the whole output.</summary>
    private static async Task<string> WaitForOutputAsync(CapturedOutput output, Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var text = output.Snapshot();
            if (text.Split('\n').Any(line => predicate(line)) || predicate(text)) return text;
            await Task.Delay(100);
        }

        return output.Snapshot();
    }

    /// <summary>
    /// Console capture a live host keeps logging into: every write and every read share one lock,
    /// so <see cref="Snapshot"/> cannot race a background log line the way a bare StringWriter would.
    /// </summary>
    private sealed class CapturedOutput : TextWriter
    {
        private readonly object _gate = new();
        private readonly StringBuilder _buffer = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Snapshot()
        {
            lock (_gate) return _buffer.ToString();
        }

        public override void Write(char value)
        {
            lock (_gate) _buffer.Append(value);
        }

        public override void Write(string? value)
        {
            lock (_gate) _buffer.Append(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_gate) _buffer.Append(buffer, index, count);
        }
    }

    /// <summary>One host under test, on a dynamic port; its storage file dies with it unless kept.</summary>
    private sealed record Host(
        WebApplication App, HttpClient Http, string StoragePath, bool KeepStorage) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            if (KeepStorage) return; // a second host reopens the same file (AC-05)
            try
            {
                File.Delete(StoragePath);
            }
            catch (IOException)
            {
                // a leftover temp file is not a product failure; the OS cleans it up
            }
        }
    }
}
