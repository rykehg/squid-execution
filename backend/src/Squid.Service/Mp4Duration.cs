namespace Squid.Service;

/// <summary>
/// The <c>durationSeconds</c> of SPEC-0009 section E: parsed from the movie header box
/// <c>mvhd</c> of an MP4/MOV file (GAP-22 — the ONLY source of a duration; every other type
/// answers null and this parser is only ever asked about video). Pure bytes, hand-written box
/// walk — no package, no I/O.
/// </summary>
public static class Mp4Duration
{
    /// <summary>Whole seconds of the <c>mvhd</c> box, or null when there is none, it is malformed, or it says nothing.</summary>
    public static int? Seconds(ReadOnlySpan<byte> data)
    {
        var moov = FindBox(data, 0, data.Length, "moov"u8);
        if (moov is null) return null;

        var mvhd = FindBox(data, moov.Value.Start, moov.Value.End, "mvhd"u8);
        if (mvhd is null) return null;

        var payload = data.Slice(mvhd.Value.Start, mvhd.Value.End - mvhd.Value.Start);
        if (payload.Length < 4) return null;

        long timescale;
        long duration;
        if (payload[0] == 1) // version 1: 64-bit times and durations
        {
            if (payload.Length < 36) return null;
            timescale = (long)ReadUInt64(payload, 20);
            duration = (long)ReadUInt64(payload, 28);
        }
        else // version 0: 32-bit
        {
            if (payload.Length < 20) return null;
            timescale = ReadUInt32(payload, 12);
            duration = ReadUInt32(payload, 16);
            if (duration == 0xFFFFFFFF) return null; // all bits set = "unknown" in the spec
        }

        if (timescale == 0 || duration == 0) return null;

        return (int)Math.Round((double)duration / timescale, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The content range of a top-level-ish box with the given type: its header skipped, its end
    /// clamped to the parent. A box of size 0 runs to the end, a size of 1 opens the 64-bit
    /// largesize that follows the type; anything malformed answers null instead of guessing.
    /// </summary>
    private static (int Start, int End)? FindBox(
        ReadOnlySpan<byte> data, int from, int to, ReadOnlySpan<byte> type)
    {
        var index = from;
        while (index + 8 <= to)
        {
            long size = ((long)data[index] << 24) | (data[index + 1] << 16) | (data[index + 2] << 8) | data[index + 3];
            var header = 8;

            if (size == 1)
            {
                if (index + 16 > to) return null;
                size = (long)(ReadUInt64(data, index + 8));
                header = 16;
            }
            else if (size == 0)
            {
                size = to - index;
            }

            if (size < header || index + size > to) return null;

            if (data.Slice(index + 4, 4).SequenceEqual(type))
            {
                return (index + header, (int)(index + size));
            }

            index += (int)size;
        }

        return null;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset) =>
        ((ulong)ReadUInt32(data, offset) << 32) | ReadUInt32(data, offset + 4);
}
