using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using SkiaSharp;
using Squid.Api.Auth;
using Squid.Service;
using Squid.Tests.Auth;
using Squid.Tests.Posts;

namespace Squid.Tests.Media;

/// <summary>
/// The host of the TASK-0027 suites: <see cref="SquidServiceHost"/> with its own temp storage
/// file and its own <c>Media:BlobRoot</c> volume, so every byte the upload writes can be read
/// back and scanned (AC-01 scans the blob for EXIF). Hosts never overlap, which is why every
/// suite belongs to the <c>ServiceHost</c> collection (localhost clustering fixes the silo ports).
/// </summary>
public sealed class MediaHostFixture : IAsyncDisposable
{
    /// <summary>The application under test — the same code <c>Program</c> runs.</summary>
    public WebApplication App { get; }

    /// <summary>Base client the per-test cookie jars share.</summary>
    public HttpClient Http { get; }

    /// <summary>SQLite file behind the host, so a test can read the rows themselves.</summary>
    public string StoragePath { get; }

    /// <summary>The configured blob volume of this host — every stored file lives under it.</summary>
    public string BlobRoot { get; }

    /// <summary>Starts the host on <c>http://127.0.0.1:0</c> with a private blob volume.</summary>
    public MediaHostFixture()
    {
        StoragePath = Path.Combine(Path.GetTempPath(), $"squid-media-{Guid.NewGuid():N}.db");
        BlobRoot = Path.Combine(Path.GetTempPath(), $"squid-media-blobs-{Guid.NewGuid():N}");
        App = SquidServiceHost.Build([
            "--Storage:Path=" + StoragePath,
            "--Logging:LogLevel:Default=Warning",
            "--Media:BlobRoot=" + BlobRoot,
        ]);
        App.Urls.Add("http://127.0.0.1:0");
        App.StartAsync().GetAwaiter().GetResult();

        Http = new HttpClient { BaseAddress = new Uri(App.Urls.Single()) };
    }

    /// <summary>A fresh jar: cookies are never shared between tests.</summary>
    public AuthClient NewClient() => new(Http);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        try
        {
            File.Delete(StoragePath);
        }
        catch (IOException)
        {
            // A leftover temp file is not a product failure; the OS cleans it up.
        }

        try
        {
            if (Directory.Exists(BlobRoot)) Directory.Delete(BlobRoot, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// House helpers of the TASK-0027 suites: the multipart upload (with the CSRF header the session
/// pipeline demands of every mutation), the synthetic payloads every AC needs — a real JPEG
/// carrying a real EXIF/GPS segment, a valid GIF, an MP4 skeleton with a known <c>mvhd</c> — and
/// the byte scan AC-01/AC-05 compare with.
/// </summary>
internal static class MediaHarness
{
    /// <summary>The ASCII marker a byte scan looks for inside the spliced EXIF segment.</summary>
    public const string ExifMarker = "GPS_TEST_MARKER";

    /// <summary>Registers a fresh account and returns its cookie jar with the handle.</summary>
    public static async Task<(AuthClient Client, string Handle)> SignedInAsync(MediaHostFixture host, string prefix)
    {
        var handle = PostRoutes.Handle(prefix);
        var client = host.NewClient();
        var registered = await client.RegisterAsync(handle, PostRoutes.Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return (client, handle);
    }

    /// <summary>POST /api/media with the multipart fields the contract §5 names: file + alt.</summary>
    public static Task<HttpResponseMessage> UploadAsync(
        AuthClient client, byte[] bytes, string alt,
        string? declaredType = null, string fileName = "arquivo.bin")
    {
        var file = new ByteArrayContent(bytes);
        if (declaredType is not null) file.Headers.ContentType = MediaTypeHeaderValue.Parse(declaredType);

        var multipart = new MultipartFormDataContent();
        multipart.Add(file, "file", fileName);
        multipart.Add(new StringContent(alt), "alt");

        return client.SendAsync(HttpMethod.Post, "/api/media", null, request =>
        {
            request.Content = multipart;
            if (client.Csrf is { Length: > 0 } csrf)
            {
                request.Headers.TryAddWithoutValidation(AuthSessions.CsrfHeader, csrf);
            }
        });
    }

    /// <summary>A real (Skia-encoded) JPEG with a structurally valid EXIF APP1 — GPS IFD included — spliced after SOI.</summary>
    public static byte[] JpegWithExif(int width = 8, int height = 4)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 23 + 40), (byte)(y * 51 + 20), 0xC0));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = encoded.ToArray();

        var segment = ExifApp1Segment();
        var spliced = new byte[2 + segment.Length + jpeg.Length - 2];
        spliced[0] = jpeg[0];
        spliced[1] = jpeg[1];
        segment.CopyTo(spliced, 2);
        jpeg.AsSpan(2).CopyTo(spliced.AsSpan(2 + segment.Length));
        return spliced;
    }

    /// <summary>A valid 1×1 GIF89a — the smallest image with a Gif kind, decode included.</summary>
    public static byte[] MinimalGif() =>
        Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>An MP4 skeleton — ftyp + moov{ mvhd (version 0) } — carrying exactly the asserted
    /// duration; <paramref name="minSize"/> pads it with trailing zeros to a minimum size (the
    /// ceiling tests need a file of exactly the per-type limit, padding after moov keeps mvhd).</summary>
    public static byte[] Mp4WithDuration(int timescale, long duration, int minSize = 0)
    {
        using var mvhd = new MemoryStream();
        mvhd.WriteByte(0); // version 0
        mvhd.Write(new byte[] { 0, 0, 0 }); // flags
        WriteUInt32Be(mvhd, 0); // creation
        WriteUInt32Be(mvhd, 0); // modification
        WriteUInt32Be(mvhd, (uint)timescale);
        WriteUInt32Be(mvhd, (uint)duration);
        var movieHeaderPayload = mvhd.ToArray();
        var mvhdBoxLength = 8 + movieHeaderPayload.Length;

        using var file = new MemoryStream();
        WriteUInt32Be(file, 16); // ftyp box: header + brand + minor
        file.Write("ftyp"u8);
        file.Write("isom"u8);
        WriteUInt32Be(file, 0);

        WriteUInt32Be(file, (uint)(8 + mvhdBoxLength)); // moov holds exactly one mvhd box
        file.Write("moov"u8);
        WriteUInt32Be(file, (uint)mvhdBoxLength); // mvhd box = its 8-byte header + payload
        file.Write("mvhd"u8);
        file.Write(movieHeaderPayload);
        var bytes = file.ToArray();
        return minSize > bytes.Length ? [.. bytes, .. new byte[minSize - bytes.Length]] : bytes;
    }

    /// <summary>Byte scan (AC-01): does <paramref name="haystack"/> contain <paramref name="needle"/>.</summary>
    public static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var index = 0; index + needle.Length <= haystack.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle)) return true;
        }

        return false;
    }

    /// <summary>ASCII bytes of a marker, for the byte scans.</summary>
    public static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    /// <summary>
    /// APP1 <c>Exif\0\0</c> with a real little-endian TIFF structure: IFD0 holding the GPSInfo
    /// pointer, the GPS IFD itself holding GPSLatitudeRef, and the marker string after both —
    /// the bytes a served file must never contain again (§2.6, FR-021).
    /// </summary>
    private static byte[] ExifApp1Segment()
    {
        using var payload = new MemoryStream();
        payload.Write(Encoding.ASCII.GetBytes("Exif\0\0"));
        payload.Write(Encoding.ASCII.GetBytes("II")); // little-endian TIFF
        payload.WriteByte(0x2A);
        payload.WriteByte(0x00);
        WriteUInt32Le(payload, 8); // IFD0 starts right after the TIFF header

        WriteUInt16Le(payload, 1); // IFD0: one entry
        WriteUInt16Le(payload, 0x8825); // GPSInfo IFD pointer
        WriteUInt16Le(payload, 4); // type LONG
        WriteUInt32Le(payload, 1);
        WriteUInt32Le(payload, 26); // the GPS IFD sits after IFD0: 8 + 2 + 12 + 4
        WriteUInt32Le(payload, 0); // no next IFD

        WriteUInt16Le(payload, 1); // GPS IFD: one entry
        WriteUInt16Le(payload, 0x0001); // GPSLatitudeRef
        WriteUInt16Le(payload, 2); // type ASCII
        WriteUInt32Le(payload, 2);
        payload.Write(Encoding.ASCII.GetBytes("N\0\0\0")); // inline value
        WriteUInt32Le(payload, 0); // no next IFD
        payload.Write(Encoding.ASCII.GetBytes(ExifMarker));

        var body = payload.ToArray();
        var segment = new byte[4 + body.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        segment[2] = (byte)((body.Length + 2) >> 8);
        segment[3] = (byte)(body.Length + 2);
        body.CopyTo(segment, 4);
        return segment;
    }

    // `System.IO.Stream` spelled out: the sibling namespace Squid.Tests.Stream would otherwise win.
    private static void WriteUInt16Le(System.IO.Stream stream, int value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
    }

    private static void WriteUInt32Le(System.IO.Stream stream, long value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }

    private static void WriteUInt32Be(System.IO.Stream stream, long value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
