namespace Nfty.Core.Imaging;

public static class ColorConvert
{
    private static byte B(double x) => (byte)Math.Clamp((int)Math.Round(x * 255.0), 0, 255);

    public static RgbColor HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        double m = v - c;
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

    public static RgbColor HslToRgb(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        double m = l - c / 2;
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

    public static (double H, double S, double V) RgbToHsv(RgbColor c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double s = max == 0 ? 0 : d / max;
        return (Hue(r, g, b, max, d), s, max);
    }

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
