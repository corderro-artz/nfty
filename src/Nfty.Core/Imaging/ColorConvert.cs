namespace Nfty.Core.Imaging;

/// <summary>Conversions between RGB and the two color spaces a layer can be authored in. Hue is
/// in degrees and wraps at 360; saturation, value and lightness are 0..1 fractions.</summary>
public static class ColorConvert
{
    private static byte B(double x) => (byte)Math.Clamp((int)Math.Round(x * 255.0), 0, 255);

    /// <summary>HSV to RGB.</summary>
    /// <param name="h">Hue in degrees; wrapped, so 360 is 0.</param>
    /// <param name="s">Saturation, 0..1.</param>
    /// <param name="v">Value, 0..1.</param>
    /// <returns>The color.</returns>
    public static RgbColor HsvToRgb(double h, double s, double v)
    {
        h = Wrap(h);
        double c = v * s;
        return FromChroma(h, c, v - c);
    }

    /// <summary>HSL to RGB.</summary>
    /// <param name="h">Hue in degrees; wrapped, so 360 is 0.</param>
    /// <param name="s">Saturation, 0..1.</param>
    /// <param name="l">Lightness, 0..1.</param>
    /// <returns>The color.</returns>
    public static RgbColor HslToRgb(double h, double s, double l)
    {
        h = Wrap(h);
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        return FromChroma(h, c, l - c / 2);
    }

    private static double Wrap(double h) => ((h % 360) + 360) % 360;

    /// <summary>
    /// The shared tail of both conversions: place chroma <paramref name="c"/> in the RGB sector
    /// that hue <paramref name="h"/> (already wrapped to 0..360) falls in, then lift by the
    /// model's match value <paramref name="m"/>. Only c and m differ between HSV and HSL.
    /// </summary>
    private static RgbColor FromChroma(double h, double c, double m)
    {
        double x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        (double r, double g, double b) = h switch
        {
            < 60  => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _     => (c, 0.0, x),
        };
        return new RgbColor(B(r + m), B(g + m), B(b + m));
    }

    /// <summary>RGB to HSV.</summary>
    /// <param name="c">The color.</param>
    /// <returns>Hue in degrees, saturation and value as 0..1.</returns>
    public static (double H, double S, double V) RgbToHsv(RgbColor c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double s = max == 0 ? 0 : d / max;
        return (Hue(r, g, b, max, d), s, max);
    }

    /// <summary>RGB to HSL.</summary>
    /// <param name="c">The color.</param>
    /// <returns>Hue in degrees, saturation and lightness as 0..1.</returns>
    public static (double H, double S, double L) RgbToHsl(RgbColor c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double l = (max + min) / 2;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        return (Hue(r, g, b, max, d), s, l);
    }

    private static double Hue(double r, double g, double b, double max, double d)
    {
        if (d == 0) return 0;
        double h = max == r ? ((g - b) / d % 6)
                 : max == g ? ((b - r) / d + 2)
                 : ((r - g) / d + 4);
        h *= 60;
        return h < 0 ? h + 360 : h;
    }
}
