using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

/// <summary>What an imported picture's embedded color profile means for it.</summary>
public enum PictureProfileKind
{
    /// <summary>No profile: the pixels are taken as sRGB, as every browser and marketplace does.</summary>
    None,

    /// <summary>An sRGB profile: the pixels already mean what they will be shown as.</summary>
    Srgb,

    /// <summary>A non-sRGB RGB matrix/TRC profile nfty can convert to sRGB.</summary>
    Convertible,

    /// <summary>A profile nfty cannot convert (LUT-only, gray, CMYK, malformed, oversized): the
    /// picture is imported as stored.</summary>
    Unsupported,
}

/// <summary>
/// A picture's embedded ICC profile, read by nfty's own reader, and the one conversion of it to sRGB
/// both front-ends use.
/// </summary>
/// <remarks>
/// <para><b>Why convert.</b> nfty stores and publishes pixels with no profile, and everything
/// downstream (marketplaces, browsers, game engines) reads untagged pixels as sRGB. A picture drawn in
/// Display P3 or Adobe RGB, imported byte for byte, therefore looks duller and shifted everywhere it
/// travels. Converting at import makes the stored pixels mean what the artist saw.</para>
/// <para><b>Never parsed by ImageSharp</b>: the raw profile is lifted out of the file's own container
/// by <c>IccProfileBytes</c> and read by <c>IccRgbProfile</c> (profile at most 1 MB, 100 tags, 4096 curve entries).</para>
/// <para><b>The conversion</b>, per pixel: each channel through its tone curve to linear (a 256-entry
/// table per channel), the profile's colorant matrix to PCS XYZ (D50), Bradford D50 to D65, the
/// IEC 61966-2-1 XYZ-to-linear-sRGB matrix, clamp to 0..1, then the sRGB transfer function back to
/// 8 bits by searching a table of the 255 midpoints between codes (round to nearest, ties to the
/// upper code). All in <see cref="double"/>, no SIMD, nothing culture-dependent, so the same picture
/// converts to the same bytes on every machine. Alpha is untouched. Out-of-gamut colors clip
/// per channel: the simple, predictable answer, and what a "relative colorimetric" convert does.</para>
/// <para><b>Order on a value-map layer</b>: converted FIRST, then <see cref="Editing.ImageImport.ToValueMap"/>
/// takes its lightness, so a Dynamic/Static layer's grays are the lightness of the colors the artist saw.</para>
/// </remarks>
public sealed class PictureProfile
{
    private readonly IccRgbProfile? _rgb;

    /// <summary>What the profile means for the picture.</summary>
    public PictureProfileKind Kind { get; }

    /// <summary>The profile's name as the note prints it (its own <c>desc</c>, or a description when it
    /// gives none).</summary>
    public string Name { get; }

    /// <summary>A picture with no profile.</summary>
    public static PictureProfile NoProfile { get; } = new(PictureProfileKind.None, "", null);

    private PictureProfile(PictureProfileKind kind, string name, IccRgbProfile? rgb)
    {
        Kind = kind;
        Name = name;
        _rgb = rgb;
    }

    /// <summary>A convertible profile from a parsed one, whatever its colorants: lets a test run an
    /// sRGB profile through the math.</summary>
    internal static PictureProfile ConvertibleForTests(IccRgbProfile rgb) =>
        new(PictureProfileKind.Convertible, rgb.Name ?? "", rgb);

    /// <summary>Whether <see cref="ConvertToSrgb"/> can run.</summary>
    public bool CanConvert => Kind == PictureProfileKind.Convertible;

    /// <summary>The sentence the import form shows, or null when there is nothing to say (no profile, or sRGB).</summary>
    public string? Note => Kind switch
    {
        PictureProfileKind.Convertible => $"This picture uses {Name}. Convert to sRGB so it looks the same everywhere.",
        PictureProfileKind.Unsupported => $"Uses {Name}; nfty can't convert this profile — imported as is.",
        _ => null,
    };

    /// <summary>The line the CLI prints when it converted.</summary>
    public string ConvertedLine => $"converted from {Name} to sRGB";

    /// <summary>Reads the profile embedded in a picture file.</summary>
    /// <param name="path">The PNG or JPEG.</param>
    /// <returns>What it carries.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static PictureProfile Read(string path) => FromFile(File.ReadAllBytes(path));

    /// <summary>Reads the profile embedded in encoded picture bytes. Never throws for any input.</summary>
    /// <param name="file">The whole PNG or JPEG.</param>
    /// <returns>What it carries.</returns>
    public static PictureProfile FromFile(ReadOnlySpan<byte> file) =>
        IccProfileBytes.Find(file, out var raw) switch
        {
            IccSearch.Found => FromProfile(raw!),
            IccSearch.None => NoProfile,
            _ => new PictureProfile(PictureProfileKind.Unsupported, "an embedded color profile", null),
        };

    /// <summary>Reads a raw ICC profile. Never throws for any input.</summary>
    /// <param name="profile">The profile bytes.</param>
    /// <returns>What it means for a picture carrying it.</returns>
    public static PictureProfile FromProfile(ReadOnlySpan<byte> profile)
    {
        bool ok = IccRgbProfile.TryRead(profile, out var rgb, out var name);
        string shown = name ?? "an embedded color profile";
        if (ok && IsSrgb(rgb!)) return new PictureProfile(PictureProfileKind.Srgb, shown, null);
        // A LUT-only sRGB profile (the v4 "preference" one) cannot be read as a matrix, but says what it is.
        if (!ok && name is not null && name.Contains("sRGB", StringComparison.OrdinalIgnoreCase))
            return new PictureProfile(PictureProfileKind.Srgb, shown, null);
        return ok
            ? new PictureProfile(PictureProfileKind.Convertible, shown, rgb)
            : new PictureProfile(PictureProfileKind.Unsupported, shown, null);
    }

    // ---- color math ----------------------------------------------------------------------------

    private static readonly double[] D50 = [0.9642, 1.0, 0.8249];
    private static readonly double[] D65 = [0.95047, 1.0, 1.08883];

    private static readonly double[,] BradfordCone =
    {
        { 0.8951, 0.2664, -0.1614 },
        { -0.7502, 1.7135, 0.0367 },
        { 0.0389, -0.0685, 1.0296 },
    };

    /// <summary>Linear sRGB to XYZ (D65), built from the IEC 61966-2-1 primaries and white.</summary>
    private static readonly double[,] SrgbToXyzD65 = FromPrimaries(0.64, 0.33, 0.30, 0.60, 0.15, 0.06, D65);

    private static readonly double[,] XyzD65ToSrgb = Invert(SrgbToXyzD65);

    private static readonly double[,] D50ToD65 = Bradford(D50, D65);

    /// <summary>sRGB's own colorants as an ICC profile states them (adapted to D50).</summary>
    private static readonly double[,] SrgbD50 = Multiply(Bradford(D65, D50), SrgbToXyzD65);

    private static bool IsSrgb(IccRgbProfile p)
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                if (Math.Abs(p.Matrix[r, c] - SrgbD50[r, c]) > 0.005) return false;
        for (int c = 0; c < 3; c++)
            for (int i = 0; i <= 16; i++)
            {
                double x = i / 16.0;
                if (Math.Abs(p.Curves[c].Linear(x) - SrgbEotf(x)) > 0.004) return false;
            }
        return true;
    }

    private static double SrgbEotf(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    /// <summary>The 255 linear values halfway (in encoded space) between consecutive 8-bit codes.</summary>
    private static readonly double[] Midpoints = BuildMidpoints();

    private static double[] BuildMidpoints()
    {
        var m = new double[255];
        for (int k = 0; k < 255; k++) m[k] = SrgbEotf((k + 0.5) / 255.0);
        return m;
    }

    private static byte Encode(double linear)
    {
        // The first midpoint above the value: codes below it are the nearest.
        int lo = 0, hi = Midpoints.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (linear >= Midpoints[mid]) lo = mid + 1; else hi = mid;
        }
        return (byte)lo;
    }

    /// <summary>Converts a decoded picture's pixels from this profile to sRGB, in place. Alpha is untouched.</summary>
    /// <param name="image">The picture, decoded from the file this profile was read from.</param>
    /// <exception cref="InvalidOperationException">The profile cannot be converted (<see cref="CanConvert"/> is false).</exception>
    public void ConvertToSrgb(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (_rgb is null) throw new InvalidOperationException("This picture's color profile cannot be converted.");

        var lut = new double[3][];
        for (int c = 0; c < 3; c++)
        {
            lut[c] = new double[256];
            for (int v = 0; v < 256; v++) lut[c][v] = _rgb.Curves[c].Linear(v / 255.0);
        }
        var m = Multiply(XyzD65ToSrgb, Multiply(D50ToD65, _rgb.Matrix));

        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    double r = lut[0][p.R], g = lut[1][p.G], b = lut[2][p.B];
                    double sr = m[0, 0] * r + m[0, 1] * g + m[0, 2] * b;
                    double sg = m[1, 0] * r + m[1, 1] * g + m[1, 2] * b;
                    double sb = m[2, 0] * r + m[2, 1] * g + m[2, 2] * b;
                    row[x] = new Rgba32(Encode(sr), Encode(sg), Encode(sb), p.A);
                }
            }
        });
    }

    private static double[,] FromPrimaries(double rx, double ry, double gx, double gy, double bx, double by, double[] white)
    {
        double[,] p =
        {
            { rx / ry, gx / gy, bx / by },
            { 1, 1, 1 },
            { (1 - rx - ry) / ry, (1 - gx - gy) / gy, (1 - bx - by) / by },
        };
        var inv = Invert(p);
        var s = new double[3];
        for (int r = 0; r < 3; r++) s[r] = inv[r, 0] * white[0] + inv[r, 1] * white[1] + inv[r, 2] * white[2];
        var m = new double[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++) m[r, c] = p[r, c] * s[c];
        return m;
    }

    private static double[,] Bradford(double[] from, double[] to)
    {
        var src = Apply(BradfordCone, from);
        var dst = Apply(BradfordCone, to);
        var scale = new double[3, 3];
        for (int i = 0; i < 3; i++) scale[i, i] = dst[i] / src[i];
        return Multiply(Invert(BradfordCone), Multiply(scale, BradfordCone));
    }

    private static double[] Apply(double[,] m, double[] v) =>
    [
        m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2],
        m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2],
        m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2],
    ];

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var m = new double[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                m[r, c] = a[r, 0] * b[0, c] + a[r, 1] * b[1, c] + a[r, 2] * b[2, c];
        return m;
    }

    private static double[,] Invert(double[,] a)
    {
        double det = a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1])
                   - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0])
                   + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
        var m = new double[3, 3];
        m[0, 0] = (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) / det;
        m[0, 1] = (a[0, 2] * a[2, 1] - a[0, 1] * a[2, 2]) / det;
        m[0, 2] = (a[0, 1] * a[1, 2] - a[0, 2] * a[1, 1]) / det;
        m[1, 0] = (a[1, 2] * a[2, 0] - a[1, 0] * a[2, 2]) / det;
        m[1, 1] = (a[0, 0] * a[2, 2] - a[0, 2] * a[2, 0]) / det;
        m[1, 2] = (a[0, 2] * a[1, 0] - a[0, 0] * a[1, 2]) / det;
        m[2, 0] = (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]) / det;
        m[2, 1] = (a[0, 1] * a[2, 0] - a[0, 0] * a[2, 1]) / det;
        m[2, 2] = (a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0]) / det;
        return m;
    }
}
