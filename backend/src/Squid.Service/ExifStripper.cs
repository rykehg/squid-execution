namespace Squid.Service;

/// <summary>
/// The EXIF removal of SPEC-0009 section E — hand-written segment parsers, no package (decision 3
/// of the spec, human gate 2026-11-09). data-model §2.6/FR-021 fix the order: this runs FIRST in
/// the upload pipeline, before decode, before the blob is hashed and persisted, before blurHash —
/// so the served bytes never carry EXIF/GPS (AC-01, AC-05). A file whose structure does not parse
/// comes back untouched: it either decodes without metadata or fails decoding and is refused.
/// </summary>
public static class ExifStripper
{
    /// <summary>The bytes with their EXIF (and every other APP1 payload) removed.</summary>
    public static byte[] Strip(byte[] original, string mimeType) => mimeType switch
    {
        "image/jpeg" => StripJpeg(original),
        "image/png" => StripPng(original),
        // GIF has no metadata segment at all; video/audio carry no EXIF (GAP-21 territory).
        _ => original,
    };

    /// <summary>
    /// Walks the marker segments before the scan and drops every APP1: EXIF (TIFF payload) and
    /// XMP both live there, and both are the places a photo carries location tags. From SOS on,
    /// the entropy-coded stream is copied verbatim — EXIF never appears after it.
    /// </summary>
    private static byte[] StripJpeg(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return data;

        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2); // SOI

        var index = 2;
        while (index + 4 <= data.Length)
        {
            if (data[index] != 0xFF) return data; // not a marker stream: keep the file as it came

            var marker = data[index + 1];
            if (marker == 0xFF) { index++; continue; } // fill byte before the real marker

            // Standalone markers carry no length: restart markers and the temporarily unused ones.
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                index += 2;
                continue;
            }

            // EOI and SOS: everything that follows is not a segment — copy it and stop parsing.
            if (marker == 0xD9 || marker == 0xDA)
            {
                output.Write(data, index, data.Length - index);
                return output.ToArray();
            }

            var length = (data[index + 2] << 8) | data[index + 3];
            if (length < 2 || index + 2 + length > data.Length)
            {
                output.Write(data, index, data.Length - index); // malformed tail: keep it whole
                return output.ToArray();
            }

            if (marker != 0xE1) // APP1 = EXIF/XMP; every other segment is copied along
            {
                output.Write(data, index, 2 + length);
            }

            index += 2 + length;
        }

        output.Write(data, index, data.Length - index);
        return output.ToArray();
    }

    /// <summary>Walks the PNG chunks after the signature and drops the <c>eXIf</c> chunk.</summary>
    private static byte[] StripPng(byte[] data)
    {
        if (data.Length < 8
            || data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47
            || data[4] != 0x0D || data[5] != 0x0A || data[6] != 0x1A || data[7] != 0x0A)
        {
            return data;
        }

        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 8); // signature

        var index = 8;
        while (index + 12 <= data.Length)
        {
            var length = (data[index] << 24) | (data[index + 1] << 16) | (data[index + 2] << 8) | data[index + 3];
            if (length < 0 || index + 12L + length > data.Length)
            {
                output.Write(data, index, data.Length - index); // malformed chunk: keep the rest
                return output.ToArray();
            }

            var isExif = data[index + 4] == 0x65 && data[index + 5] == 0x58
                && data[index + 6] == 0x49 && data[index + 7] == 0x66; // "eXIf"
            if (!isExif)
            {
                output.Write(data, index, 12 + length);
            }

            index += 12 + length;
        }

        output.Write(data, index, data.Length - index);
        return output.ToArray();
    }
}
