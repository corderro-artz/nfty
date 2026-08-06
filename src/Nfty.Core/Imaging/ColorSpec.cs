using System.Globalization;

namespace Nfty.Core.Imaging;

/// <summary>Parses the prefixed colour specs an author writes. The prefix is REQUIRED — a missing
/// or unknown one is an error rather than a guess, because guessing between <c>hex:</c> and
/// <c>hsv:</c> silently produces the wrong art.</summary>
public static class ColorSpec
{
    /// <summary>Parses a spec such as <c>hex:d6249f</c> or <c>hsv:322,83,84</c>.</summary>
    /// <param name="spec">The prefixed spec.</param>
    /// <returns>The colour.</returns>
    /// <exception cref="FormatException">The prefix is missing, unknown, or the body is malformed.</exception>
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
        // The 8-digit rrggbbaa form used to be accepted and then quietly threw the alpha pair
        // away: RgbColor has no alpha field, and a colorization only ever takes H/S from a fixed
        // color (value comes from the grayscale value-map), so there is nowhere for it to go.
        // Silently accepting a value that is then ignored is worse than rejecting it, so reject.
        if (body.Length == 8)
            throw new FormatException($"hex color '{body}' has 8 digits (rrggbbaa), but only hue "
                + "and saturation are ever taken from a color here — the alpha pair would be "
                + "parsed and then silently discarded. Use 6-digit rrggbb instead.");
        if (body.Length != 6)
            throw new FormatException(
                $"hex color '{body}' must be exactly 6 hex digits (rrggbb); got {body.Length}.");

        byte P(int start)
        {
            if (!byte.TryParse(body.AsSpan(start, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out byte value))
                throw new FormatException(
                    $"hex color '{body}' has a non-hex digit at position {start + 1}.");
            return value;
        }

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
