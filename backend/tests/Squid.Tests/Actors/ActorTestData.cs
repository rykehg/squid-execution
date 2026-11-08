using Squid.Grains.Actors;

namespace Squid.Tests.Actors;

/// <summary>Key material the tests hand to the grain: wrapped/public bytes only, as production must.</summary>
internal static class ActorTestData
{
    /// <summary>Base64 of <paramref name="bytes"/>.</summary>
    public static string B64(params byte[] bytes) => Convert.ToBase64String(bytes);

    /// <summary>A complete material with two public keys and one wrapped signing key.</summary>
    public static ActorKeyMaterial Material() => new(
        [
            new ActorPublicKey("Ed25519", "#ed25519", B64(11, 12, 13, 14)),
            new ActorPublicKey("RSA-2048", "#main-key", B64(21, 22, 23, 24)),
        ],
        B64(31, 32, 33, 34));
}
