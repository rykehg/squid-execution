namespace Squid.Domain.Objects;

/// <summary>
/// The magic-byte decision of SPEC-0009 section E: the upload type comes from the first bytes of
/// the payload, never from the file name or the declared media type (contract §9.5 — an
/// executable disguised as an image simply carries no image magic, and an unknown signature is a
/// refusal, never a guess). Pure bytes in, media type out — no I/O, no package.
/// </summary>
public static class MediaSignature
{
    /// <summary>The sniffed media type, or null when the bytes are not a supported upload (→ 415).</summary>
    public static string? Sniff(ReadOnlySpan<byte> data)
    {
        // JPEG: SOI + first marker byte.
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // PNG: its 8-byte signature, fixed and impossible to fake by accident.
        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return "image/png";
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        // ISO base media: the ftyp box at offset 4, whose brand tells an MP4 from a QuickTime
        // MOV from an M4A (GAP-22 duration rides the same mvhd box for both video brands).
        if (data.Length >= 12 && data[4] == 0x66 && data[5] == 0x74 && data[6] == 0x79 && data[7] == 0x70)
        {
            var brand = data.Slice(8, 4);
            if (brand.SequenceEqual("qt  "u8)) return "video/quicktime";
            if (brand.SequenceEqual("M4A "u8)) return "audio/mp4";
            return "video/mp4";
        }

        // A QuickTime file may open with moov instead of ftyp; MP4 never does.
        if (data.Length >= 4 && data[0] == 0x6D && data[1] == 0x6F && data[2] == 0x6F && data[3] == 0x76)
        {
            return "video/quicktime";
        }

        if (data.Length >= 12
            && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 // RIFF
            && data[8] == 0x57 && data[9] == 0x41 && data[10] == 0x56 && data[11] == 0x45) // WAVE
        {
            return "audio/wav";
        }

        // The audio set the MVP uploads: tagged MP3, raw MP3 frame sync, Ogg, FLAC.
        if (data.Length >= 3 && data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33)
        {
            return "audio/mpeg";
        }

        if (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
        {
            return "audio/mpeg";
        }

        if (data.Length >= 4 && data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67 && data[3] == 0x53)
        {
            return "audio/ogg";
        }

        if (data.Length >= 4 && data[0] == 0x66 && data[1] == 0x4C && data[2] == 0x61 && data[3] == 0x43)
        {
            return "audio/flac";
        }

        // Everything else — PE (MZ), ELF, scripts, zip, RIFF/WEBP — is not a supported upload.
        return null;
    }
}
