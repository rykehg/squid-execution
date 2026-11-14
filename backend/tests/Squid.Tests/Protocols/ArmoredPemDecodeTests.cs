using System.Security.Cryptography;
using System.Text;
using Squid.Protocols.ActivityPub;

namespace Squid.Tests.Protocols;

/// <summary>
/// AC-01 and the single-line half of AC-03 of TASK-0052 (S-41): the public-key PEM reader of
/// <see cref="ProfileJsonLd"/> against the ARMORED multi-line key of contract activitypub §1.1 —
/// the shape Mastodon and Friendica publish. The defect this suite locks: the compact used to run
/// BEFORE the armor Replace, the armor's spaces were already swallowed, the base64 never
/// validated, and <c>HttpFederatedActorClient.KeyOf</c> then answered null for every remote key.
/// The round-trip signs with the ORIGINAL key and verifies with the DECODED one; the single-line
/// spellings keep answering exactly as they did before the fix.
/// </summary>
public sealed class ArmoredPemDecodeTests
{
    [Fact] // AC-01
    public void An_armored_multiline_pem_decodes_into_a_key_that_signs_and_verifies()
    {
        using var rsa = RSA.Create(2048);
        var pem = Armor(rsa.ExportSubjectPublicKeyInfo());
        Assert.Contains("\n", pem, StringComparison.Ordinal); // the multi-line armor of §1.1, 64 columns

        var der = ProfileJsonLd.TryDecodePublicKeyPem(pem);
        Assert.NotNull(der);
        Assert.Equal(rsa.ExportSubjectPublicKeyInfo(), der); // the decoded bytes ARE the published SPKI

        using var decoded = RSA.Create();
        decoded.ImportSubjectPublicKeyInfo(der, out _);
        var payload = Encoding.UTF8.GetBytes("round-trip da chave PEM blindada TASK-0052");
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.True(
            decoded.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "a chave decodificada do PEM blindado nao verificou a assinatura da chave que o publicou");
    }

    [Fact] // AC-03 (linha única) + o veredito tolerante
    public void The_single_line_spellings_keep_decoding_and_hostile_material_stays_a_null_verdict()
    {
        using var rsa = RSA.Create(2048);
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var body = Convert.ToBase64String(spki);

        // a grafia que ja passava antes da correcao: base64 de linha unica, sem armor.
        Assert.Equal(spki, ProfileJsonLd.TryDecodePublicKeyPem(body));
        // e a linha unica COM armor, mesma leitura do Replace de armor.
        Assert.Equal(spki, ProfileJsonLd.TryDecodePublicKeyPem(
            $"-----BEGIN PUBLIC KEY-----{body}-----END PUBLIC KEY-----"));

        // publicacao hostil continua sendo um veredito nulo, nunca uma excecao (FR-040).
        Assert.Null(ProfileJsonLd.TryDecodePublicKeyPem(
            "-----BEGIN PUBLIC KEY-----\nnao-e-base64!\n-----END PUBLIC KEY-----"));
        Assert.Null(ProfileJsonLd.TryDecodePublicKeyPem(null));
        Assert.Null(ProfileJsonLd.TryDecodePublicKeyPem("   "));
    }

    /// <summary>The armored PEM of contract §1.1 — armor plus 64-column body lines, as Mastodon publishes it.</summary>
    private static string Armor(byte[] spki)
    {
        var base64 = Convert.ToBase64String(spki);
        var lines = new List<string>();
        for (var offset = 0; offset < base64.Length; offset += 64)
        {
            lines.Add(base64.Substring(offset, Math.Min(64, base64.Length - offset)));
        }

        return $"-----BEGIN PUBLIC KEY-----\n{string.Join('\n', lines)}\n-----END PUBLIC KEY-----";
    }
}
