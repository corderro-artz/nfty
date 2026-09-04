using Avalonia.Media.Imaging;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Imaging;

/// <summary>Turns a variant's value-map into a display <see cref="Bitmap"/> the way the cook path
/// would: custom = raw, dynamic/static = colorized via <see cref="Colorizer"/>. Pure; the caller owns
/// the returned bitmaps.</summary>
public static class VariantImagery
{
    /// <summary>Renders one variant as the GUI shows it.</summary>
    /// <param name="bridge">Converts an ImageSharp frame to an Avalonia bitmap.</param>
    /// <param name="ing">The layer.</param>
    /// <param name="variantId">Which variant.</param>
    /// <param name="salt">Varies the sampled color, so two swatches of the same variant differ.</param>
    /// <returns>The bitmap; the caller owns it.</returns>
    public static Bitmap Render(IImageBridge bridge, LoadedIngredient ing, string variantId, int salt = 0)
    {
        var map = ing.VariantImages[variantId];
        var coloriz = ing.Manifest.Colorization;
        if (coloriz is null) return bridge.ToBitmap(map);        // custom — raw, book owns the source

        var rng = new SplitMix64Rng(SeedHash.ToUlong($"{ing.Manifest.Id}:{variantId}:{salt}"));
        var c = ColorRoller.Roll(coloriz, rng);
        using var colored = Colorizer.Apply(map, c.H, c.S, coloriz.Model);
        return bridge.ToBitmap(colored);
    }

    /// <summary>Renders one variant once per color the layer can take, for the Colorways panel.</summary>
    /// <param name="bridge">Converts an ImageSharp frame to an Avalonia bitmap.</param>
    /// <param name="ing">The layer.</param>
    /// <param name="samples">How many swatches to render across the range.</param>
    /// <returns>One bitmap per sample.</returns>
    public static IReadOnlyList<Bitmap> Colorways(IImageBridge bridge, LoadedIngredient ing, int samples = 6)
    {
        var coloriz = ing.Manifest.Colorization;
        string firstId = ing.Manifest.Variants[0].Id;
        if (coloriz is null || !coloriz.Entries.Any(e => e.Range is not null))
            return new[] { Render(bridge, ing, firstId) };       // custom or static — a single swatch

        var range = coloriz.Entries.First(e => e.Range is not null).Range!;
        double sat = (range.SatMin + range.SatMax) / 2.0 / 100.0;
        var map = ing.VariantImages[firstId];
        var result = new List<Bitmap>(samples);
        for (int i = 0; i < samples; i++)
        {
            double t = samples == 1 ? 0 : (double)i / (samples - 1);
            double hue = range.HueMin + t * (range.HueMax - range.HueMin);
            using var colored = Colorizer.Apply(map, hue, sat, coloriz.Model);
            result.Add(bridge.ToBitmap(colored));
        }
        return result;
    }

    /// <summary>Renders a value-map with an explicit color range, for the editor's live preview.</summary>
    /// <param name="bridge">Converts an ImageSharp frame to an Avalonia bitmap.</param>
    /// <param name="valueMap">The source image.</param>
    /// <param name="dynamic">Whether to tint at all; false composites as-is.</param>
    /// <param name="hueMin">Lowest hue in degrees.</param>
    /// <param name="hueMax">Highest hue in degrees.</param>
    /// <param name="satMin">Lowest saturation, 0-100.</param>
    /// <param name="satMax">Highest saturation, 0-100.</param>
    /// <param name="fixedColor">A fixed color spec to use instead of sampling the range, or null.</param>
    /// <param name="salt">Varies the sample within that seed.</param>
    /// <returns>The bitmap; the caller owns it.</returns>
    public static Bitmap RenderWith(IImageBridge bridge, Image<Rgba32> valueMap, bool dynamic,
        double hueMin, double hueMax, double satMin, double satMax, string fixedColor, int salt = 0)
    {
        try
        {
            RolledColor c;
            if (dynamic)
            {
                var rng = new SplitMix64Rng(SeedHash.ToUlong($"editor:{salt}"));
                double hue = hueMin + rng.NextDouble() * (hueMax - hueMin);
                double s = (satMin + rng.NextDouble() * (satMax - satMin)) / 100.0;
                c = new RolledColor(hue, s);
            }
            else
            {
                c = ColorRoller.FromFixed(fixedColor, ColorModel.Hsv);
            }
            using var colored = Colorizer.Apply(valueMap, c.H, c.S, ColorModel.Hsv);
            return bridge.ToBitmap(colored);
        }
        catch (FormatException) { return bridge.ToBitmap(valueMap); }        // bad color spec — show raw
    }
}
