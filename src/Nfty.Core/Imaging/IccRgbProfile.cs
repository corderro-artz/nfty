using System.Buffers.Binary;
using System.Text;

namespace Nfty.Core.Imaging;

/// <summary>
/// nfty's own reader for the one kind of ICC profile it converts: an RGB matrix/TRC ("matrix-shaper")
/// display profile.
/// </summary>
/// <remarks>
/// <para><b>Why not ImageSharp's.</b> Its parser is where advisory GHSA-gwg2-r3hj-4w44 lives (CLUT
/// parsing, unpatched in the pinned 3.1.11). This reads the raw bytes with every offset and length
/// checked, and reads only what a matrix-shaper conversion needs: the header, the tag table, the name
/// (<c>desc</c>, v2 text or v4 <c>mluc</c>), the three colorants (<c>rXYZ gXYZ bXYZ</c>) and the three
/// tone curves (<c>rTRC gTRC bTRC</c>, <c>curv</c> gamma or table, <c>para</c> types 0-4).</para>
/// <para><b>Never looked at</b>: <c>A2B*</c>/<c>B2A*</c> LUTs, CLUTs, anything not listed above. A
/// profile carrying LUTs AND the matrix/TRC tags is converted through the matrix/TRC (the colorimetric
/// reading every matrix-shaper consumer uses); one with LUTs alone cannot be converted.</para>
/// <para>Hard limits: <see cref="IccProfileBytes.MaxProfileBytes"/> total, <see cref="MaxTags"/> tags,
/// <see cref="MaxCurveEntries"/> curve entries. Nothing here throws: <see cref="TryRead"/> answers
/// false with what it could learn (the name, for the note).</para>
/// </remarks>
internal sealed class IccRgbProfile
{
    /// <summary>The most tags a profile may declare.</summary>
    public const int MaxTags = 100;

    /// <summary>The most entries a sampled tone curve may carry.</summary>
    public const int MaxCurveEntries = 4096;

    /// <summary>The profile's own name, or null when it gives none that can be read.</summary>
    public string? Name { get; private init; }

    /// <summary>Linear RGB to PCS XYZ (D50): columns are the red, green and blue colorants.</summary>
    public double[,] Matrix { get; private init; } = new double[3, 3];

    /// <summary>The red, green and blue tone curves, encoded value 0..1 to linear 0..1.</summary>
    public IccCurve[] Curves { get; private init; } = [];

    private IccRgbProfile() { }

    /// <summary>Reads a profile.</summary>
    /// <param name="bytes">The raw profile.</param>
    /// <param name="profile">The profile, when it is an RGB matrix-shaper one.</param>
    /// <param name="name">Its name, whenever one could be read (even when the profile cannot be converted).</param>
    /// <returns>Whether the profile can be converted.</returns>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out IccRgbProfile? profile, out string? name)
    {
        profile = null;
        name = null;
        if (bytes.Length < 132 || bytes.Length > IccProfileBytes.MaxProfileBytes) return false;
        uint size = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (size < 132 || size > (uint)bytes.Length) return false;
        var p = bytes[..(int)size];
        if (!p.Slice(36, 4).SequenceEqual("acsp"u8)) return false;

        uint tagCount = BinaryPrimitives.ReadUInt32BigEndian(p[128..]);
        if (tagCount > MaxTags || 132 + tagCount * 12 > (uint)p.Length) return false;

        var tags = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        for (int i = 0; i < tagCount; i++)
        {
            var e = p.Slice(132 + i * 12, 12);
            string sig = Encoding.ASCII.GetString(e[..4]);
            uint off = BinaryPrimitives.ReadUInt32BigEndian(e[4..]);
            uint len = BinaryPrimitives.ReadUInt32BigEndian(e[8..]);
            if (off > (uint)p.Length || len > (uint)p.Length - off || len < 8) return false;
            tags.TryAdd(sig, ((int)off, (int)len));
        }

        if (tags.TryGetValue("desc", out var d)) name = ReadName(p.Slice(d.Offset, d.Length));

        // Data color space RGB, PCS XYZ: the only pair a 3x3 matrix can describe.
        if (!p.Slice(16, 4).SequenceEqual("RGB "u8) || !p.Slice(20, 4).SequenceEqual("XYZ "u8)) return false;

        var m = new double[3, 3];
        string[] xyz = ["rXYZ", "gXYZ", "bXYZ"];
        for (int c = 0; c < 3; c++)
        {
            if (!tags.TryGetValue(xyz[c], out var t) || !ReadXyz(p.Slice(t.Offset, t.Length), out var v)) return false;
            m[0, c] = v.X; m[1, c] = v.Y; m[2, c] = v.Z;
        }

        var curves = new IccCurve[3];
        string[] trc = ["rTRC", "gTRC", "bTRC"];
        for (int c = 0; c < 3; c++)
        {
            if (!tags.TryGetValue(trc[c], out var t) || IccCurve.TryRead(p.Slice(t.Offset, t.Length)) is not { } curve)
                return false;
            curves[c] = curve;
        }

        profile = new IccRgbProfile { Name = name, Matrix = m, Curves = curves };
        return true;
    }

    private static double S15(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadInt32BigEndian(b) / 65536.0;

    private static bool ReadXyz(ReadOnlySpan<byte> tag, out (double X, double Y, double Z) v)
    {
        v = default;
        if (tag.Length < 20 || !tag[..4].SequenceEqual("XYZ "u8)) return false;
        v = (S15(tag[8..]), S15(tag[12..]), S15(tag[16..]));
        return true;
    }

    /// <summary>A v2 <c>desc</c> ASCII name or a v4 <c>mluc</c> name (en first, else the first record).</summary>
    private static string? ReadName(ReadOnlySpan<byte> tag)
    {
        if (tag[..4].SequenceEqual("desc"u8))
        {
            if (tag.Length < 12) return null;
            uint n = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
            if (n == 0 || n > (uint)(tag.Length - 12)) return null;
            return Clean(Encoding.ASCII.GetString(tag.Slice(12, (int)n)));
        }
        if (tag[..4].SequenceEqual("mluc"u8))
        {
            if (tag.Length < 16) return null;
            uint records = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
            uint recSize = BinaryPrimitives.ReadUInt32BigEndian(tag[12..]);
            if (recSize < 12 || records == 0 || records > 1000 || recSize > (uint)tag.Length || 16UL + (ulong)records * recSize > (ulong)tag.Length) return null;
            int pick = 0;
            for (int i = 0; i < records; i++)
                if (tag[(16 + i * (int)recSize)..].StartsWith("en"u8)) { pick = i; break; }
            var r = tag.Slice(16 + pick * (int)recSize, 12);
            uint len = BinaryPrimitives.ReadUInt32BigEndian(r[4..]);
            uint off = BinaryPrimitives.ReadUInt32BigEndian(r[8..]);
            if (off > (uint)tag.Length || len > (uint)tag.Length - off) return null;
            return Clean(Encoding.BigEndianUnicode.GetString(tag.Slice((int)off, (int)len & ~1)));
        }
        return null;
    }

    /// <summary>Printable characters only, trimmed and capped, so a hostile name cannot reshape a
    /// sentence it is put in.</summary>
    private static string? Clean(string raw)
    {
        var sb = new StringBuilder();
        foreach (char ch in raw)
        {
            if (ch == '\0') break;
            if (!char.IsControl(ch)) sb.Append(ch);
            if (sb.Length >= 64) break;
        }
        string s = sb.ToString().Trim();
        return s.Length == 0 ? null : s;
    }
}

/// <summary>One ICC tone curve: an encoded channel value 0..1 to its linear value.</summary>
internal sealed class IccCurve
{
    private readonly Func<double, double> _eval;

    private IccCurve(Func<double, double> eval) => _eval = eval;

    /// <summary>The linear value of an encoded one, clamped to 0..1.</summary>
    /// <param name="x">Encoded, 0..1.</param>
    /// <returns>Linear, 0..1.</returns>
    public double Linear(double x) => Math.Clamp(_eval(Math.Clamp(x, 0, 1)), 0, 1);

    /// <summary>A pure power curve.</summary>
    /// <param name="gamma">The exponent.</param>
    /// <returns>The curve.</returns>
    public static IccCurve Gamma(double gamma) => new(x => Math.Pow(x, gamma));

    private static double S15(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadInt32BigEndian(b) / 65536.0;

    /// <summary>Reads a <c>curv</c> or <c>para</c> tag.</summary>
    /// <param name="tag">The tag's bytes.</param>
    /// <returns>The curve, or null when it is neither or is malformed.</returns>
    public static IccCurve? TryRead(ReadOnlySpan<byte> tag)
    {
        if (tag.Length < 12) return null;
        if (tag[..4].SequenceEqual("curv"u8))
        {
            uint n = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
            if (n > IccRgbProfile.MaxCurveEntries || 12 + n * 2 > (uint)tag.Length) return null;
            if (n == 0) return new IccCurve(x => x);
            if (n == 1)
            {
                double g = BinaryPrimitives.ReadUInt16BigEndian(tag[12..]) / 256.0;
                return g <= 0 ? null : Gamma(g);
            }
            var table = new double[n];
            for (int i = 0; i < n; i++) table[i] = BinaryPrimitives.ReadUInt16BigEndian(tag[(12 + i * 2)..]) / 65535.0;
            return new IccCurve(x =>
            {
                double pos = x * (table.Length - 1);
                int i = (int)Math.Floor(pos);
                if (i >= table.Length - 1) return table[^1];
                double f = pos - i;
                return table[i] + (table[i + 1] - table[i]) * f;
            });
        }
        if (tag[..4].SequenceEqual("para"u8))
        {
            int type = BinaryPrimitives.ReadUInt16BigEndian(tag[8..]);
            int count = type switch { 0 => 1, 1 => 3, 2 => 4, 3 => 5, 4 => 7, _ => -1 };
            if (count < 0 || tag.Length < 12 + count * 4) return null;
            var v = new double[7];
            for (int i = 0; i < count; i++) v[i] = S15(tag[(12 + i * 4)..]);
            double g = v[0], a = v[1], b = v[2], c = v[3], d = v[4], e = v[5], f = v[6];
            if (g <= 0) return null;
            static double Pw(double t, double g) => t <= 0 ? 0 : Math.Pow(t, g);
            return type switch
            {
                0 => new IccCurve(x => Pw(x, g)),
                1 => a == 0 ? null : new IccCurve(x => x >= -b / a ? Pw(a * x + b, g) : 0),
                2 => a == 0 ? null : new IccCurve(x => x >= -b / a ? Pw(a * x + b, g) + c : c),
                3 => new IccCurve(x => x >= d ? Pw(a * x + b, g) : c * x),
                _ => new IccCurve(x => x >= d ? Pw(a * x + b, g) + e : c * x + f),
            };
        }
        return null;
    }
}
