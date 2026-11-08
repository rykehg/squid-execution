using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Squid.Grains.Actors;

namespace Squid.Protocols.ActivityPub;

/// <summary>
/// Canonical ↔ JSON-LD mapping of the actor profile (SPEC-0007 B-10, B-11): the one place that
/// knows the ActivityPub vocabulary, the fixed embedded <c>@context</c> and the two published keys.
/// Pure functions over strings and records — no I/O and no network fetch (task Gaps), and JSON-LD
/// stays on this side of the boundary, never in <c>Squid.Domain</c> (ADR-11).
/// </summary>
public static class ProfileJsonLd
{
    /// <summary>Media type of the profile document (contract activitypub §6).</summary>
    public const string MediaType = "application/activity+json";

    private const string ActivityStreamsContext = "https://www.w3.org/ns/activitystreams";
    private const string RsaPemHeader = "-----BEGIN PUBLIC KEY-----";
    private const string RsaPemFooter = "-----END PUBLIC KEY-----";

    /// <summary>Ed25519 public keys are 32 bytes; the did:key length check below leans on it.</summary>
    private const int Ed25519KeySize = 32;

    // Multicodec ed25519-pub (0xed 0x01): framing the raw key with it produces the z6Mk… shape of
    // the contract example, and any did:key decoder reads the value as it stands.
    private static readonly byte[] Ed25519Multicodec = [0xed, 0x01];

    /// <summary>Serializes the profile of <paramref name="state"/> as seen from <paramref name="origin"/>.</summary>
    public static string Serialize(ActorState state, string origin) =>
        JsonSerializer.Serialize(ToDocument(ActorProfile.FromState(state, origin)));

    /// <summary>
    /// Materializes a document back into the canonical profile: id → origin + handle, name →
    /// display name, PEM → SPKI base64, multibase → raw key base64. Per B-11, serializing the
    /// result again yields the same document — nothing the document carried is lost here.
    /// </summary>
    public static ActorProfile Materialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var id = root.GetProperty("id").GetString() ?? throw new FormatException("profile without id");
        const string marker = "/users/";
        var cut = id.LastIndexOf(marker, StringComparison.Ordinal);
        if (cut < 0 || cut + marker.Length >= id.Length)
        {
            throw new FormatException("profile id is not a /users/ id");
        }

        var preferred = root.GetProperty("preferredUsername").GetString()
            ?? throw new FormatException("profile without preferredUsername");
        var handle = id[(cut + marker.Length)..];
        if (!string.Equals(preferred, handle, StringComparison.Ordinal))
        {
            throw new FormatException("id and preferredUsername disagree");
        }

        var name = root.TryGetProperty("name", out var nameElement)
            && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()
            : null;

        var pem = root.GetProperty("publicKey").GetProperty("publicKeyPem").GetString()
            ?? throw new FormatException("profile without publicKeyPem");
        var multibase = root.GetProperty("squid:ed25519Key").GetProperty("publicKeyMultibase").GetString()
            ?? throw new FormatException("profile without squid:ed25519Key");

        return new ActorProfile(id[..cut], preferred, name, FromPem(pem), FromMultibase(multibase));
    }

    private static ProfileDocument ToDocument(ActorProfile profile)
    {
        var id = profile.Id;
        return new ProfileDocument(
            [ActivityStreamsContext, new SquidNamespace($"{profile.Origin}/ns/squid#")],
            id,
            "Person",
            profile.PreferredUsername,
            profile.Name,
            new RsaKeyDocument($"{id}#main-key", id, ToPem(profile.RsaPublicKeyBase64)),
            new Ed25519KeyDocument($"{id}#ed25519", ToMultibase(profile.Ed25519PublicKeyBase64)));
    }

    private static string ToPem(string spkiBase64)
    {
        var lines = new List<string>(spkiBase64.Length / 64 + 1);
        for (var offset = 0; offset < spkiBase64.Length; offset += 64)
        {
            lines.Add(spkiBase64.Substring(offset, Math.Min(64, spkiBase64.Length - offset)));
        }

        return $"{RsaPemHeader}\n{string.Join('\n', lines)}\n{RsaPemFooter}";
    }

    private static string FromPem(string pem)
    {
        var body = pem
            .Replace(RsaPemHeader, string.Empty, StringComparison.Ordinal)
            .Replace(RsaPemFooter, string.Empty, StringComparison.Ordinal);
        var compact = new StringBuilder(body.Length);
        foreach (var character in body)
        {
            if (!char.IsWhiteSpace(character))
            {
                compact.Append(character);
            }
        }

        // Validate here so a malformed PEM fails at the boundary, not three layers deeper.
        Convert.FromBase64String(compact.ToString());
        return compact.ToString();
    }

    private static string ToMultibase(string rawKeyBase64)
    {
        var raw = Convert.FromBase64String(rawKeyBase64);
        var framed = new byte[Ed25519Multicodec.Length + raw.Length];
        Ed25519Multicodec.CopyTo(framed, 0);
        raw.CopyTo(framed, Ed25519Multicodec.Length);
        return "z" + Base58.Encode(framed);
    }

    private static string FromMultibase(string multibase)
    {
        if (multibase.Length == 0 || multibase[0] != 'z')
        {
            throw new FormatException("ed25519 key is not base58btc multibase");
        }

        var framed = Base58.Decode(multibase[1..]);
        if (framed.Length != Ed25519Multicodec.Length + Ed25519KeySize
            || framed[0] != Ed25519Multicodec[0]
            || framed[1] != Ed25519Multicodec[1])
        {
            throw new FormatException("multibase value is not a did:key ed25519 public key");
        }

        return Convert.ToBase64String(framed, Ed25519Multicodec.Length, Ed25519KeySize);
    }
}

/// <summary>The wire shape of contract activitypub §1.1, serialized only — materialization reads
/// the JSON directly so the fixed <c>@context</c> never has to round-trip through a polymorphic type.</summary>
internal sealed record ProfileDocument(
    [property: JsonPropertyName("@context")] object[] Context,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("preferredUsername")] string PreferredUsername,
    [property: JsonPropertyName("name")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    [property: JsonPropertyName("publicKey")] RsaKeyDocument PublicKey,
    [property: JsonPropertyName("squid:ed25519Key")] Ed25519KeyDocument Ed25519Key);

/// <summary><c>publicKey</c> is a single RSA object whose key id ends in <c>#main-key</c> (contract §1.1).</summary>
internal sealed record RsaKeyDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("owner")] string Owner,
    [property: JsonPropertyName("publicKeyPem")] string PublicKeyPem);

/// <summary>The additional Ed25519 key of R-02, published for RFC 9421 pairs.</summary>
internal sealed record Ed25519KeyDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("publicKeyMultibase")] string PublicKeyMultibase);

/// <summary>Inline <c>squid:</c> namespace of the context, so no peer fetches anything remote.</summary>
internal sealed record SquidNamespace(
    [property: JsonPropertyName("squid")] string Value);
