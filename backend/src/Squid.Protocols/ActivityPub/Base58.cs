using System.Numerics;
using System.Text;

namespace Squid.Protocols.ActivityPub;

/// <summary>
/// base58btc (multibase <c>z</c>) of contract activitypub §1.1: the Ed25519 key is published as
/// <c>publicKeyMultibase</c> and must come back byte for byte on the round-trip (B-11). The repo
/// ships no base58 package and installing one for these few lines would be a gate (ADR-25).
/// </summary>
internal static class Base58
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Encodes the bytes, no multibase prefix; leading zero bytes become leading '1'.</summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var zeros = 0;
        while (zeros < data.Length && data[zeros] == 0) zeros++;

        var value = new BigInteger(data[zeros..], isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (value > BigInteger.Zero)
        {
            value = BigInteger.DivRem(value, 58, out var digit);
            digits.Append(Alphabet[(int)digit]);
        }

        var reversed = digits.ToString().ToCharArray();
        Array.Reverse(reversed);
        return new string('1', zeros) + new string(reversed);
    }

    /// <summary>Decodes base58btc; a character outside the alphabet is a <see cref="FormatException"/>.</summary>
    public static byte[] Decode(string text)
    {
        var zeros = 0;
        while (zeros < text.Length && text[zeros] == '1') zeros++;

        var value = BigInteger.Zero;
        foreach (var character in text)
        {
            var digit = Alphabet.IndexOf(character);
            if (digit < 0)
            {
                throw new FormatException("invalid base58 character");
            }

            value = (value * 58) + digit;
        }

        var body = value.IsZero
            ? Array.Empty<byte>()
            : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (body.Length == 0)
        {
            return new byte[zeros];
        }

        var result = new byte[zeros + body.Length];
        body.CopyTo(result, zeros);
        return result;
    }
}
