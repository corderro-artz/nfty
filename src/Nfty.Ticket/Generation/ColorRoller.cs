using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Core.Generation;

/// <summary>One rolled color: the hue and saturation a value-map is tinted with. Value and
/// lightness are deliberately absent — those come from the grayscale art.</summary>
/// <param name="H">Hue in degrees.</param>
/// <param name="S">Saturation as a 0..1 fraction.</param>
public readonly record struct RolledColor(double H, double S);

/// <summary>Turns a layer's colorization into the color one asset will use.</summary>
public static class ColorRoller
{
    /// <summary>Rolls a color, consuming RNG.</summary>
    /// <param name="c">Validated colorization: nonempty entries with finite, nonnegative weights
    /// and a finite positive total, each containing exactly one valid range or fixed spec.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <returns>The rolled hue and saturation.</returns>
    /// <exception cref="InvalidOperationException">The entries have zero or negative total weight.</exception>
    /// <exception cref="FormatException">The chosen fixed color spec is malformed or nonfinite.</exception>
    /// <remarks>Consumes one draw for the entry and two more for a range; fixed entries consume
    /// only the entry draw. The caller validates the colorization before rolling.</remarks>
    public static RolledColor Roll(Colorization c, IRng rng)
    {
        var entry = PickEntry(c.Entries, rng);
        if (entry.Fixed is not null)
            return FromFixed(entry.Fixed, c.Model);

        var range = entry.Range!;
        double hue = SampleHue(range, rng.NextDouble());
        double sat = SampleSat(range, rng.NextDouble());
        return new RolledColor(hue, sat);
    }

    /// <summary>
    /// The hue this range yields for a unit sample. Split out so <c>UniqueSpace</c> can
    /// evaluate the <em>same</em> expression at its endpoints instead of re-deriving the reachable
    /// interval algebraically — the count is a promise about what <see cref="Roll"/> produces, and
    /// re-derivation is what let the two drift apart before.
    /// </summary>
    /// <param name="range">A validated, finite range with ascending hue and saturation bounds.</param>
    /// <param name="unit">A value in <c>[0,1)</c>, as <see cref="IRng.NextDouble"/> produces.</param>
    /// <returns>Hue in degrees. Floating-point rounding can reach the upper bound.</returns>
    public static double SampleHue(ColorRange range, double unit) =>
        range.HueMin + unit * (range.HueMax - range.HueMin);

    /// <summary>
    /// The saturation this range yields for a unit sample, as the 0..1 fraction
    /// <see cref="RolledColor.S"/> carries. The <c>/100</c> is not cosmetic: its rounding is baked
    /// into the DNA of every Set ever generated, so this is the only definition of the value.
    /// </summary>
    /// <param name="range">A validated, finite range with ascending hue and saturation bounds.</param>
    /// <param name="unit">A value in <c>[0,1)</c>, as <see cref="IRng.NextDouble"/> produces.</param>
    /// <returns>Saturation as a 0..1 fraction. Floating-point rounding can reach the upper bound.</returns>
    public static double SampleSat(ColorRange range, double unit) =>
        (range.SatMin + unit * (range.SatMax - range.SatMin)) / 100.0;

    /// <summary>
    /// Resolves a single fixed color spec to its (H, S) deterministically, consuming NO RNG.
    /// Used by Static layers, which colorize with exactly one fixed color and no per-asset roll,
    /// and by <c>preview</c>, so a rendered preview resolves color exactly as generation does.
    /// </summary>
    /// <param name="fixedSpec">A prefixed color spec accepted by <see cref="ColorSpec.Parse"/>.</param>
    /// <param name="model">HSV or HSL, determining which saturation is taken from the RGB color.</param>
    /// <returns>The fixed hue in degrees and saturation as a 0..1 fraction.</returns>
    /// <exception cref="FormatException">The color spec is malformed or contains a nonfinite component.</exception>
    public static RolledColor FromFixed(string fixedSpec, ColorModel model)
    {
        var rgb = ColorSpec.Parse(fixedSpec);
        var (h, s, _) = model == ColorModel.Hsv
            ? ColorConvert.RgbToHsv(rgb)
            : ColorConvert.RgbToHsl(rgb);
        return new RolledColor(h, s);
    }

    private static ColorEntry PickEntry(IReadOnlyList<ColorEntry> entries, IRng rng)
    {
        double total = entries.Sum(e => e.Weight);
        if (total <= 0) throw new InvalidOperationException("Color entries have zero total weight.");
        double r = rng.NextDouble() * total, acc = 0;
        foreach (var e in entries)
        {
            acc += e.Weight;
            if (r < acc) return e;
        }
        // Multiplication can round r up to total, even with a unit sample below one. A trailing
        // zero-weight entry remains shelved at that endpoint; use the last positive option.
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].Weight > 0) return entries[i];
        throw new InvalidOperationException("Color entries have no positive weight.");
    }
}
