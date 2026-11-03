using System.Buffers.Binary;
using System.IO.Compression;

namespace Nfty.Core.Imaging;

/// <summary>
/// Finds the raw ICC profile a PNG or JPEG carries, reading the file's own container — never
/// ImageSharp's ICC code.
/// </summary>
/// <remarks>
/// <para>ImageSharp 3.1.11's ICC parser (<c>IccProfile.Entries</c>/<c>Header</c>) carries an
/// unpatched advisory, and <see cref="ImageFormats"/> drops every profile it decodes. So the bytes are
/// taken here, straight from the PNG <c>iCCP</c> chunk (zlib-inflated with a hard output cap) or the
/// JPEG <c>APP2 ICC_PROFILE</c> segments (reassembled by sequence number), and handed to
/// <see cref="IccRgbProfile"/>, nfty's own bounds-checked reader.</para>
/// <para>Every length is checked against what is left of the file before it is used, and nothing
/// here throws: a container that cannot be followed is <see cref="IccSearch.Malformed"/>.</para>
/// </remarks>
internal static class IccProfileBytes
{
    /// <summary>The largest profile nfty will look at. A display profile is a few kilobytes.</summary>
    public const int MaxProfileBytes = 1 << 20;

    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegIccTag => "ICC_PROFILE\0"u8;

    /// <summary>The profile bytes embedded in a picture file, if any.</summary>
    /// <param name="file">The whole encoded file.</param>
    /// <param name="profile">The raw profile, when one was found.</param>
    /// <returns>What the search found.</returns>
    public static IccSearch Find(ReadOnlySpan<byte> file, out byte[]? profile)
    {
        profile = null;
        if (file.StartsWith(PngSignature)) return FindPng(file, out profile);
        if (file.Length >= 2 && file[0] == 0xFF && file[1] == 0xD8) return FindJpeg(file, out profile);
        return IccSearch.None;
    }

    private static IccSearch FindPng(ReadOnlySpan<byte> file, out byte[]? profile)
    {
        profile = null;
        int at = PngSignature.Length;
        while (file.Length - at >= 12)
        {
            uint len = BinaryPrimitives.ReadUInt32BigEndian(file[at..]);
            var type = file.Slice(at + 4, 4);
            if (len > (uint)(file.Length - at - 12)) return IccSearch.Malformed;
            var data = file.Slice(at + 8, (int)len);
            if (type.SequenceEqual("iCCP"u8)) return InflatePng(data, out profile);
            // The PNG spec puts iCCP before the first IDAT; past it there is nothing to find.
            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8)) return IccSearch.None;
            at += 12 + (int)len;
        }
        return IccSearch.None;
    }

    private static IccSearch InflatePng(ReadOnlySpan<byte> data, out byte[]? profile)
    {
        profile = null;
        int nul = data.IndexOf((byte)0);
        // name 1..79 bytes, NUL, compression method (0 = zlib), then the stream.
        if (nul < 1 || nul > 79 || data.Length < nul + 3 || data[nul + 1] != 0) return IccSearch.Malformed;
        try
        {
            using var input = new MemoryStream(data[(nul + 2)..].ToArray(), writable: false);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            int n;
            while ((n = z.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + n > MaxProfileBytes) return IccSearch.TooLarge;
                output.Write(buffer, 0, n);
            }
            profile = output.ToArray();
            return IccSearch.Found;
        }
        catch (InvalidDataException) { return IccSearch.Malformed; }
    }

    private static IccSearch FindJpeg(ReadOnlySpan<byte> file, out byte[]? profile)
    {
        profile = null;
        var parts = new SortedDictionary<int, byte[]>();
        int count = -1;
        int total = 0;
        int at = 2;
        while (at < file.Length)
        {
            if (file[at] != 0xFF) return IccSearch.Malformed;
            while (at < file.Length && file[at] == 0xFF) at++;   // fill bytes
            if (at >= file.Length) return IccSearch.Malformed;
            byte marker = file[at++];
            if (marker == 0xD8 || marker == 0x01 || marker is >= 0xD0 and <= 0xD7) continue;   // no length
            if (marker == 0xD9 || marker == 0xDA) break;   // end of image / start of scan: headers are over
            if (file.Length - at < 2) return IccSearch.Malformed;
            int len = BinaryPrimitives.ReadUInt16BigEndian(file[at..]);
            if (len < 2 || len > file.Length - at) return IccSearch.Malformed;
            var seg = file.Slice(at + 2, len - 2);
            if (marker == 0xE2 && seg.StartsWith(JpegIccTag))
            {
                if (seg.Length < JpegIccTag.Length + 2) return IccSearch.Malformed;
                int seq = seg[JpegIccTag.Length];
                int of = seg[JpegIccTag.Length + 1];
                if (seq < 1 || of < 1 || seq > of || (count >= 0 && of != count) || parts.ContainsKey(seq))
                    return IccSearch.Malformed;
                count = of;
                var piece = seg[(JpegIccTag.Length + 2)..];
                total += piece.Length;
                if (total > MaxProfileBytes) return IccSearch.TooLarge;
                parts[seq] = piece.ToArray();
            }
            at += len;
        }
        if (count < 0) return IccSearch.None;
        if (parts.Count != count) return IccSearch.Malformed;
        var all = new byte[total];
        int o = 0;
        foreach (var p in parts.Values) { p.CopyTo(all, o); o += p.Length; }
        profile = all;
        return IccSearch.Found;
    }
}

/// <summary>What looking for an embedded profile found.</summary>
internal enum IccSearch
{
    /// <summary>The file carries no profile.</summary>
    None,

    /// <summary>A profile was found and extracted.</summary>
    Found,

    /// <summary>The file says it carries a profile, but the container around it cannot be followed.</summary>
    Malformed,

    /// <summary>The profile is larger than <see cref="IccProfileBytes.MaxProfileBytes"/>.</summary>
    TooLarge,
}
