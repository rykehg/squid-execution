using System.Text;
using SkiaSharp;

namespace Squid.Service;

/// <summary>
/// The blurHash encoder of SPEC-0009 section E — hand-written, no package (decision 3 of the spec):
/// the reference algorithm (DCT with 4×3 components, base83 over the JSON/HTML/shell-safe set)
/// running over pixels SkiaSharp already decoded. The bitmap is first drawn down to fit 32×32, the
/// size the reference encoders use — the transform is O(pixels × components) and must stay off the
/// request's hot path for a full-resolution photo. Deterministic: the same pixels always produce
/// the same string, which is what AC-05 compares (blurHash of the served file vs of the blob).
/// </summary>
public static class BlurHashEncoder
{
    private const int ComponentsX = 4;

    private const int ComponentsY = 3;

    /// <summary>Longest edge after the downscale, before the DCT.</summary>
    private const int MaxPixels = 32;

    private const string EncodeCharacters =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

    /// <summary>The blurHash of <paramref name="source"/>'s pixels; the source stays owned by the caller.</summary>
    public static string Encode(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var pixels = Downscale(source);
        var width = pixels.Width;
        var height = pixels.Height;

        // One factor per component, ordered by increasing X first then Y (row-major y/x), which
        // is also the order the string emits them: DC first, then the rest.
        var factors = new float[ComponentsY * ComponentsX, 3];
        for (var y = 0; y < ComponentsY; y++)
        {
            for (var x = 0; x < ComponentsX; x++)
            {
                var normalisation = (x == 0 && y == 0) ? 1f : 2f;
                float r = 0, g = 0, b = 0;
                for (var py = 0; py < height; py++)
                {
                    var row = MathF.Cos(MathF.PI * y * py / height);
                    for (var px = 0; px < width; px++)
                    {
                        var basis = row * MathF.Cos(MathF.PI * x * px / width);
                        var pixel = pixels.GetPixel(px, py);
                        r += basis * SrgbToLinear(pixel.Red);
                        g += basis * SrgbToLinear(pixel.Green);
                        b += basis * SrgbToLinear(pixel.Blue);
                    }
                }

                var scale = normalisation / (width * height);
                var index = y * ComponentsX + x;
                factors[index, 0] = r * scale;
                factors[index, 1] = g * scale;
                factors[index, 2] = b * scale;
            }
        }

        // Field 1: the component counts, packed as (nx-1) + (ny-1)*9 in one base83 digit.
        var builder = new StringBuilder(27);
        builder.Append(Encode83((ComponentsX - 1) + (ComponentsY - 1) * 9, 1));

        // Field 2: the maximum AC magnitude, quantised to a single digit scaled by 1/166.
        var actualMaximum = 0f;
        for (var index = 1; index < factors.GetLength(0); index++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                actualMaximum = Math.Max(actualMaximum, MathF.Abs(factors[index, channel]));
            }
        }

        var quantisedMaximum = Math.Clamp((long)Math.Floor(actualMaximum * 166 - 0.5), 0, 82);
        var maximum = (quantisedMaximum + 1) / 166f;
        builder.Append(Encode83(quantisedMaximum, 1));

        // Field 3: the average colour as a 24-bit sRGB value, four digits.
        var dc = Encode83(
            (LinearToSrgb(factors[0, 0]) << 16) + (LinearToSrgb(factors[0, 1]) << 8) + LinearToSrgb(factors[0, 2]),
            4);
        builder.Append(dc);

        // Field 4: every AC component as three 0..18 values packed into two digits.
        for (var index = 1; index < factors.GetLength(0); index++)
        {
            var quantR = QuantiseAc(factors[index, 0], maximum);
            var quantG = QuantiseAc(factors[index, 1], maximum);
            var quantB = QuantiseAc(factors[index, 2], maximum);
            builder.Append(Encode83((quantR * 19 * 19) + (quantG * 19) + quantB, 2));
        }

        return builder.ToString();
    }

    /// <summary>Draws the bitmap into a ≤ 32×32 canvas — a new bitmap, so the caller keeps its own.</summary>
    private static SKBitmap Downscale(SKBitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        var scale = Math.Min(1f, (float)MaxPixels / Math.Max(longest, 1));
        var width = Math.Max(1, (int)MathF.Round(source.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(source.Height * scale));

        var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.DrawBitmap(
                source, SKRect.Create(source.Width, source.Height), SKRect.Create(width, height));
        }

        return target;
    }

    private static long QuantiseAc(float value, float maximum) =>
        Math.Clamp((long)Math.Floor(SignPow(value / maximum, 0.5f) * 9 + 9.5f), 0, 18);

    private static float SignPow(float value, float exp) =>
        MathF.Sign(value) * MathF.Pow(MathF.Abs(value), exp);

    private static float SrgbToLinear(byte value)
    {
        var v = value / 255f;
        return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    private static int LinearToSrgb(float value)
    {
        var v = Math.Clamp(value, 0f, 1f);
        return v <= 0.0031308f
            ? (int)(v * 12.92f * 255f + 0.5f)
            : (int)(((1.055f * MathF.Pow(v, 1f / 2.4f)) - 0.055f) * 255f + 0.5f);
    }

    /// <summary>Big-endian base83: <paramref name="length"/> digits, most significant first (Algorithm.md).</summary>
    private static string Encode83(long value, int length)
    {
        var digits = new char[length];
        for (var i = 0; i < length; i++)
        {
            var divisor = (long)Math.Pow(83, length - 1 - i);
            digits[i] = EncodeCharacters[(int)(value / divisor % 83)];
        }

        return new string(digits);
    }
}
