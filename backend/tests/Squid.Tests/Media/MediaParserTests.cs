using System.Text;
using SkiaSharp;
using Squid.Domain.Objects;
using Squid.Service;

namespace Squid.Tests.Media;

/// <summary>
/// The unit-level half of TASK-0027: the pure parsers the upload pipeline is built on — magic
/// bytes (§9.5), the EXIF strip of §2.6, the <c>mvhd</c> duration of GAP-22 and the hand-written
/// blurHash encoder — each checked against bytes this test itself spells out.
/// </summary>
public sealed class MediaParserTests
{
    [Fact]
    public void Sniff_TheMagicBytesDecideTheType()
    {
        Assert.Equal("image/jpeg", MediaSignature.Sniff([0xFF, 0xD8, 0xFF, 0xE0, 0x00]));
        Assert.Equal("image/png", MediaSignature.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]));
        Assert.Equal("image/gif", MediaSignature.Sniff(MediaHarness.MinimalGif()));
        Assert.Equal("video/mp4", MediaSignature.Sniff(MediaHarness.Mp4WithDuration(1000, 1)));
        Assert.Equal("audio/wav", MediaSignature.Sniff(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt ")));
        Assert.Equal("audio/mpeg", MediaSignature.Sniff(Encoding.ASCII.GetBytes("ID3\0\0\0")));
        Assert.Equal("audio/flac", MediaSignature.Sniff(Encoding.ASCII.GetBytes("fLaC\0\0\0\0")));
    }

    [Fact]
    public void Sniff_AnExecutableOrAnythingUnknown_IsNull()
    {
        Assert.Null(MediaSignature.Sniff(Encoding.ASCII.GetBytes("MZ\0\0This program cannot be run in DOS mode")));
        Assert.Null(MediaSignature.Sniff([0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01]));
        Assert.Null(MediaSignature.Sniff([0x23, 0x21, 0x2F, 0x62, 0x69, 0x6E])); // shebang
        Assert.Null(MediaSignature.Sniff(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 "))); // WebP: not an MVP upload
        Assert.Null(MediaSignature.Sniff([]));
    }

    [Fact]
    public void ExifStrip_Jpeg_DropsTheExifApp1AndKeepsEverythingElse()
    {
        using var jpeg = new MemoryStream();
        jpeg.Write(new byte[] { 0xFF, 0xD8 }); // SOI
        jpeg.Write(Segment(0xE1, Encoding.ASCII.GetBytes("Exif\0\0" + MediaHarness.ExifMarker)));
        jpeg.Write(Segment(0xE0, Encoding.ASCII.GetBytes("JFIF\0"))); // APP0 must survive
        jpeg.Write(Segment(0xDA, new byte[] { 0x01, 0x02 })); // SOS
        jpeg.Write(new byte[] { 0x42, 0x43 }); // entropy-coded data
        jpeg.Write(new byte[] { 0xFF, 0xD9 }); // EOI

        var stripped = ExifStripper.Strip(jpeg.ToArray(), "image/jpeg");

        Assert.False(MediaHarness.Contains(stripped, MediaHarness.Bytes("Exif\0\0")));
        Assert.False(MediaHarness.Contains(stripped, MediaHarness.Bytes(MediaHarness.ExifMarker)));
        Assert.True(MediaHarness.Contains(stripped, Encoding.ASCII.GetBytes("JFIF")));
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, stripped[..2]);
        Assert.Equal(new byte[] { 0x42, 0x43, 0xFF, 0xD9 }, stripped[^4..]);
    }

    [Fact]
    public void ExifStrip_Png_DropsTheExifChunkAndKeepsIdat()
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        png.Write(Chunk("eXIf", MediaHarness.Bytes(MediaHarness.ExifMarker)));
        png.Write(Chunk("IDAT", new byte[] { 1, 2, 3, 4 }));

        var stripped = ExifStripper.Strip(png.ToArray(), "image/png");

        Assert.False(MediaHarness.Contains(stripped, MediaHarness.Bytes("eXIf")));
        Assert.True(MediaHarness.Contains(stripped, MediaHarness.Bytes("IDAT")));
        Assert.True(stripped.Length < png.Length);
    }

    [Fact]
    public void Mp4Duration_ReadsTheMvhdOfBothVersions()
    {
        Assert.Equal(7, Mp4Duration.Seconds(MediaHarness.Mp4WithDuration(1000, 7000)));

        var versionOne = BuildVersionOneMvhd(timescale: 90000, duration: 45000); // 0.5 s → rounds away from zero
        Assert.Equal(1, Mp4Duration.Seconds(versionOne));
    }

    [Fact]
    public void Mp4Duration_WithoutAMovieHeader_AnswersNull()
    {
        Assert.Null(Mp4Duration.Seconds(Encoding.ASCII.GetBytes("just some bytes without any box structure")));
        Assert.Null(Mp4Duration.Seconds(MediaHarness.MinimalGif()));
    }

    [Fact]
    public void BlurHash_SolidBlack_IsTheExactKnownString()
    {
        using var black = new SKBitmap(16, 16);
        for (var y = 0; y < black.Height; y++)
        {
            for (var x = 0; x < black.Width; x++)
            {
                black.SetPixel(x, y, SKColors.Black);
            }
        }

        // 4×3 components: size digit 'L', max-AC digit '0', DC "0000", every AC pair "fQ"
        // (9·19² + 9·19 + 9 = 3429 = 41·83 + 26 → 'f' then charset[26] = 'Q').
        var expected = "L00000" + string.Concat(Enumerable.Repeat("fQ", 11));
        Assert.Equal(expected, BlurHashEncoder.Encode(black));
        Assert.Equal(expected, BlurHashEncoder.Encode(black)); // deterministic: AC-05 compares two runs
    }

    [Fact]
    public void BlurHash_AColouredBitmap_IsAlso28CharsAndDiffersFromBlack()
    {
        using var red = new SKBitmap(4, 4);
        for (var y = 0; y < red.Height; y++)
        {
            for (var x = 0; x < red.Width; x++)
            {
                red.SetPixel(x, y, SKColors.Red);
            }
        }

        var hash = BlurHashEncoder.Encode(red);
        Assert.Equal(28, hash.Length);
        Assert.NotEqual("L00000" + string.Concat(Enumerable.Repeat("fQ", 11)), hash);
        Assert.Equal(hash, BlurHashEncoder.Encode(red));
    }

    /// <summary>One JPEG segment: marker byte, big-endian length (payload + 2), payload.</summary>
    private static byte[] Segment(byte marker, byte[] payload)
    {
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        segment[2] = (byte)((payload.Length + 2) >> 8);
        segment[3] = (byte)(payload.Length + 2);
        payload.CopyTo(segment, 4);
        return segment;
    }

    /// <summary>One PNG chunk: length, type, payload, and a zero CRC (this parser never reads it).</summary>
    private static byte[] Chunk(string type, byte[] payload)
    {
        var chunk = new byte[12 + payload.Length];
        chunk[0] = (byte)(payload.Length >> 24);
        chunk[1] = (byte)(payload.Length >> 16);
        chunk[2] = (byte)(payload.Length >> 8);
        chunk[3] = (byte)payload.Length;
        Encoding.ASCII.GetBytes(type, 0, 4, chunk, 4);
        payload.CopyTo(chunk, 8);
        return chunk;
    }

    /// <summary>An mvhd box of version 1 (64-bit times) inside a moov, for the other duration shape.</summary>
    private static byte[] BuildVersionOneMvhd(int timescale, long duration)
    {
        using var payload = new MemoryStream();
        payload.WriteByte(1); // version 1: 64-bit times follow
        payload.Write(new byte[] { 0, 0, 0 }); // flags
        payload.Write(new byte[8]); // creation
        payload.Write(new byte[8]); // modification
        WriteBigEndian64(payload, (ulong)timescale);
        WriteBigEndian64(payload, (ulong)duration);
        var movieHeader = payload.ToArray();

        var file = new MemoryStream();
        WriteBigEndian32(file, (uint)(8 + 8 + movieHeader.Length)); // moov holds a whole mvhd box
        file.Write("moov"u8);
        WriteBigEndian32(file, (uint)(8 + movieHeader.Length)); // the mvhd BOX is header + payload
        file.Write("mvhd"u8);
        file.Write(movieHeader);
        return file.ToArray();
    }

    // `System.IO.Stream` spelled out: the sibling namespace Squid.Tests.Stream would otherwise win.
    private static void WriteBigEndian64(System.IO.Stream stream, ulong value)
    {
        for (var shift = 56; shift >= 0; shift -= 8) stream.WriteByte((byte)(value >> shift));
    }

    private static void WriteBigEndian32(System.IO.Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
