using System.Globalization;

namespace Nfty.Core.Imaging;

public static class ColorSpec
{
    public static RgbColor Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new FormatException("Empty color spec.");
        int i = spec.IndexOf(':');
        if (i <= 0)
            throw new FormatException($"Color spec '{spec}' is missing a prefix (hex:/rgb:/hsl:/hsv:).");

        string prefix = spec[..i].Trim().ToLowerInvariant();
        string body = spec[(i + 1)..].Trim();

        return prefix switch
        {
            "hex" => Hex(body),
            "rgb" => Triple(body, (a, b, c) => new RgbColor(Byte(a), Byte(b), Byte(c))),
            "hsv" => Triple(body, (h, s, v) => ColorConvert.HsvToRgb(h, s / 100.0, v / 100.0)),
            "hsl" => Triple(body, (h, s, l) => ColorConvert.HslToRgb(h, s / 100.0, l / 100.0)),
            _ => throw new FormatException($"Unknown color prefix '{prefix}'."),
        };
    }

    private static RgbColor Hex(string body)
    {
        if (body.Length != 6 && body.Length != 8)
            throw new FormatException($"hex expects rrggbb or rrggbbaa, got '{body}'.");
        byte P(int start) => byte.Parse(body.Substring(start, 2), NumberStyles.HexNumber);
        return new RgbColor(P(0), P(2), P(4));
    }

    private static RgbColor Triple(string body, Func<double, double, double, RgbColor> make)
    {
        var parts = body.Split(',');
        if (parts.Length != 3)
            throw new FormatException($"Expected 3 comma-separated values, got '{body}'.");
        double D(string s) => double.Parse(s.Trim(), CultureInfo.InvariantCulture);
        return make(D(parts[0]), D(parts[1]), D(parts[2]));
    }

    private static byte Byte(double d) => (byte)Math.Clamp((int)Math.Round(d), 0, 255);
}
