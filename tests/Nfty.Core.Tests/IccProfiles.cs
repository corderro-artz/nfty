using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// ICC profiles built byte by byte from PUBLISHED colorimetry (Lindbloom's D65 RGB-to-XYZ matrices
/// and Bradford D65-to-D50 matrix), so a test reads a profile the way any display profile is laid
/// out, with numbers the code under test did not compute. Shared by Core and App tests.
/// </summary>
public static class IccProfiles
{
    private static readonly double[,] BradfordD65ToD50 =
    {
        { 1.0478112, 0.0228866, -0.0501270 },
        { 0.0295424, 0.9904844, -0.0170491 },
        { -0.0092345, 0.0150436, 0.7521316 },
    };

    /// <summary>Display P3, D65.</summary>
    public static readonly double[,] P3 =
    {
        { 0.4865709, 0.2656677, 0.1982173 },
        { 0.2289746, 0.6917385, 0.0792869 },
        { 0.0, 0.0451134, 1.0439444 },
    };

    /// <summary>Adobe RGB (1998), D65.</summary>
    public static readonly double[,] Adobe =
    {
        { 0.5767309, 0.1855540, 0.1881852 },
        { 0.2973769, 0.6273491, 0.0752741 },
        { 0.0270343, 0.0706872, 0.9911085 },
    };

    /// <summary>sRGB, D65.</summary>
    public static readonly double[,] Srgb =
    {
        { 0.4124564, 0.3575761, 0.1804375 },
        { 0.2126729, 0.7151522, 0.0721750 },
        { 0.0193339, 0.1191920, 0.9503041 },
    };

    /// <summary>How the tone curves are written.</summary>
    public enum Trc
    {
        /// <summary>The sRGB curve as <c>para</c> type 3.</summary>
        SrgbPara,

        /// <summary><c>curv</c> with one entry, gamma 563/256 (Adobe RGB's 2.19921875).</summary>
        AdobeGamma,

        /// <summary>The sRGB curve sampled into a 1024-entry <c>curv</c> table.</summary>
        SrgbTable,
    }

    /// <summary>A matrix/TRC RGB display profile.</summary>
    /// <param name="name">Its <c>desc</c>.</param>
    /// <param name="d65">Its D65 RGB-to-XYZ matrix.</param>
    /// <param name="trc">Its curves.</param>
    /// <param name="v4Name">Write the name as a v4 <c>mluc</c> rather than a v2 <c>desc</c>.</param>
    /// <returns>The profile bytes.</returns>
    public static byte[] MatrixProfile(string name, double[,] d65, Trc trc, bool v4Name = false)
    {
        var tags = new List<(string Sig, byte[] Data)>
        {
            ("desc", v4Name ? Mluc(name) : Desc(name)),
            ("wtpt", Xyz(0.9642, 1.0, 0.8249)),
        };
        string[] xyz = ["rXYZ", "gXYZ", "bXYZ"];
        for (int c = 0; c < 3; c++)
        {
            double X = d65[0, c], Y = d65[1, c], Z = d65[2, c];
            tags.Add((xyz[c], Xyz(
                BradfordD65ToD50[0, 0] * X + BradfordD65ToD50[0, 1] * Y + BradfordD65ToD50[0, 2] * Z,
                BradfordD65ToD50[1, 0] * X + BradfordD65ToD50[1, 1] * Y + BradfordD65ToD50[1, 2] * Z,
                BradfordD65ToD50[2, 0] * X + BradfordD65ToD50[2, 1] * Y + BradfordD65ToD50[2, 2] * Z)));
        }
        var curve = trc switch
        {
            Trc.SrgbPara => Para3(2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045),
            Trc.AdobeGamma => CurvGamma(0x0233),
            _ => CurvTable(1024),
        };
        foreach (var s in new[] { "rTRC", "gTRC", "bTRC" }) tags.Add((s, curve));
        return Assemble("RGB ", tags);
    }

    /// <summary>An RGB profile that describes itself only through a LUT (<c>A2B0</c>), no matrix.</summary>
    /// <param name="name">Its <c>desc</c>.</param>
    /// <returns>The profile bytes.</returns>
    public static byte[] LutOnlyProfile(string name) =>
        Assemble("RGB ", [("desc", Desc(name)), ("A2B0", Encoding.ASCII.GetBytes("mft2").Concat(new byte[60]).ToArray())]);

    /// <summary>A CMYK profile.</summary>
    /// <param name="name">Its <c>desc</c>.</param>
    /// <returns>The profile bytes.</returns>
    public static byte[] CmykProfile(string name) => Assemble("CMYK", [("desc", Desc(name))]);

    /// <summary>Lays out header, tag table and tag data.</summary>
    public static byte[] Assemble(string space, List<(string Sig, byte[] Data)> tags)
    {
        int table = 132 + tags.Count * 12;
        int size = table + tags.Sum(t => Pad(t.Data.Length));
        var p = new byte[size];
        BinaryPrimitives.WriteUInt32BigEndian(p, (uint)size);
        p[8] = 2; p[9] = 0x10;
        Encoding.ASCII.GetBytes("mntr").CopyTo(p, 12);
        Encoding.ASCII.GetBytes(space).CopyTo(p, 16);
        Encoding.ASCII.GetBytes("XYZ ").CopyTo(p, 20);
        Encoding.ASCII.GetBytes("acsp").CopyTo(p, 36);
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(128), (uint)tags.Count);
        int at = table;
        for (int i = 0; i < tags.Count; i++)
        {
            var e = p.AsSpan(132 + i * 12);
            Encoding.ASCII.GetBytes(tags[i].Sig).CopyTo(e);
            BinaryPrimitives.WriteUInt32BigEndian(e[4..], (uint)at);
            BinaryPrimitives.WriteUInt32BigEndian(e[8..], (uint)tags[i].Data.Length);
            tags[i].Data.CopyTo(p, at);
            at += Pad(tags[i].Data.Length);
        }
        return p;
    }

    private static int Pad(int n) => (n + 3) & ~3;

    private static void S15(Span<byte> b, double v) => BinaryPrimitives.WriteInt32BigEndian(b, (int)Math.Round(v * 65536));

    private static byte[] Xyz(double x, double y, double z)
    {
        var b = new byte[20];
        Encoding.ASCII.GetBytes("XYZ ").CopyTo(b, 0);
        S15(b.AsSpan(8), x); S15(b.AsSpan(12), y); S15(b.AsSpan(16), z);
        return b;
    }

    private static byte[] Desc(string name)
    {
        var ascii = Encoding.ASCII.GetBytes(name + "\0");
        var b = new byte[12 + ascii.Length + 78];
        Encoding.ASCII.GetBytes("desc").CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), (uint)ascii.Length);
        ascii.CopyTo(b, 12);
        return b;
    }

    private static byte[] Mluc(string name)
    {
        var text = Encoding.BigEndianUnicode.GetBytes(name);
        var b = new byte[28 + text.Length];
        Encoding.ASCII.GetBytes("mluc").CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), 12);
        Encoding.ASCII.GetBytes("enUS").CopyTo(b, 16);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), (uint)text.Length);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(24), 28);
        text.CopyTo(b, 28);
        return b;
    }

    private static byte[] Para3(double g, double a, double bb, double c, double d)
    {
        var b = new byte[12 + 20];
        Encoding.ASCII.GetBytes("para").CopyTo(b, 0);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(8), 3);
        S15(b.AsSpan(12), g); S15(b.AsSpan(16), a); S15(b.AsSpan(20), bb); S15(b.AsSpan(24), c); S15(b.AsSpan(28), d);
        return b;
    }

    private static byte[] CurvGamma(ushort u8f8)
    {
        var b = new byte[14];
        Encoding.ASCII.GetBytes("curv").CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(12), u8f8);
        return b;
    }

    private static byte[] CurvTable(int n)
    {
        var b = new byte[12 + n * 2];
        Encoding.ASCII.GetBytes("curv").CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), (uint)n);
        for (int i = 0; i < n; i++)
        {
            double v = i / (double)(n - 1);
            double lin = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(12 + i * 2), (ushort)Math.Round(lin * 65535));
        }
        return b;
    }

    // ---- containers ----------------------------------------------------------------------------

    /// <summary>A PNG of the given pixels (one row) carrying <paramref name="profile"/> in an iCCP chunk.</summary>
    public static byte[] PngWith(byte[]? profile, params Rgba32[] pixels)
    {
        using var img = new Image<Rgba32>(pixels.Length, 1);
        for (int x = 0; x < pixels.Length; x++) img[x, 0] = pixels[x];
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });
        var png = ms.ToArray();
        return profile is null ? png : WithChunk(png, "iCCP", IccpData(profile));
    }

    /// <summary>A <paramref name="w"/>x<paramref name="h"/> PNG of one color carrying <paramref name="profile"/>.</summary>
    public static byte[] PngFilled(byte[]? profile, int w, int h, Rgba32 fill)
    {
        using var img = new Image<Rgba32>(w, h, fill);
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });
        var png = ms.ToArray();
        return profile is null ? png : WithChunk(png, "iCCP", IccpData(profile));
    }

    /// <summary>The Display P3 profile the tests share.</summary>
    public static byte[] DisplayP3 => MatrixProfile("Display P3", P3, Trc.SrgbPara);

    /// <summary>The iCCP payload for a profile.</summary>
    public static byte[] IccpData(byte[] profile)
    {
        using var ms = new MemoryStream();
        ms.Write("Profile\0\0"u8);
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(profile);
        return ms.ToArray();
    }

    /// <summary>An 8x8 JPEG carrying <paramref name="profile"/> split across <paramref name="segments"/> APP2 segments.</summary>
    public static byte[] JpegWith(byte[] profile, int segments)
    {
        using var img = new Image<Rgba32>(8, 8, new Rgba32(120, 60, 30, 255));
        using var ms = new MemoryStream();
        img.Save(ms, new JpegEncoder());
        var jpg = ms.ToArray();
        var outp = new List<byte> { 0xFF, 0xD8 };
        int per = (profile.Length + segments - 1) / segments;
        for (int s = 0; s < segments; s++)
        {
            var piece = profile.Skip(s * per).Take(per).ToArray();
            int len = 2 + 12 + 2 + piece.Length;
            outp.AddRange([0xFF, 0xE2, (byte)(len >> 8), (byte)len]);
            outp.AddRange("ICC_PROFILE\0"u8.ToArray());
            outp.Add((byte)(s + 1)); outp.Add((byte)segments);
            outp.AddRange(piece);
        }
        outp.AddRange(jpg.Skip(2));
        return outp.ToArray();
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>The PNG with one extra chunk spliced in right after IHDR.</summary>
    public static byte[] WithChunk(byte[] png, string type, byte[] data)
    {
        const int afterIhdr = 8 + 4 + 4 + 13 + 4;
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        for (int i = 0; i < 4; i++) chunk[4 + i] = (byte)type[i];
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc(chunk.AsSpan(4, 4 + data.Length)));
        return [.. png[..afterIhdr], .. chunk, .. png[afterIhdr..]];
    }
}
