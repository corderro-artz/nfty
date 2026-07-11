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
        {
            var rgb = ColorSpec.Parse(entry.Fixed);
            var (h, s) = c.Model == ColorModel.Hsv
                ? (ColorConvert.RgbToHsv(rgb).H, ColorConvert.RgbToHsv(rgb).S)
                : (ColorConvert.RgbToHsl(rgb).H, ColorConvert.RgbToHsl(rgb).S);
            return new RolledColor(h, s);
        }

        var range = entry.Range!;
        double hue = range.HueMin + rng.NextDouble() * (range.HueMax - range.HueMin);
        double sat = (range.SatMin + rng.NextDouble() * (range.SatMax - range.SatMin)) / 100.0;
        return new RolledColor(hue, sat);
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
