using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Core.Generation;

public readonly record struct RolledColor(double H, double S);

public static class ColorRoller
{
    public static RolledColor Roll(Colorization c, IRng rng)
    {
        var entry = PickEntry(c.Entries, rng);
        if (entry.Fixed is not null)
            return FromFixed(entry.Fixed, c.Model);

        var range = entry.Range!;
        double hue = range.HueMin + rng.NextDouble() * (range.HueMax - range.HueMin);
        double sat = (range.SatMin + rng.NextDouble() * (range.SatMax - range.SatMin)) / 100.0;
        return new RolledColor(hue, sat);
    }

    /// <summary>
    /// Resolves a single fixed color spec to its (H, S) deterministically, consuming NO RNG.
    /// Used by Static layers, which colorize with exactly one fixed color and no per-asset roll.
    /// </summary>
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
        return entries[^1];
    }
}
