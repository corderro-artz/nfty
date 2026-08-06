using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

/// <summary>Tints a grayscale value-map with a hue and saturation, preserving each pixel's own
/// value or lightness — which is what makes one piece of art yield a whole family of variants.</summary>
public static class Colorizer
{
    /// <summary>Colorizes a value-map.</summary>
    /// <param name="valueMap">The grayscale source. Not modified.</param>
    /// <param name="h">Hue in degrees.</param>
    /// <param name="s">Saturation, 0..1.</param>
    /// <param name="model">Which space to interpret the colour in.</param>
    /// <returns>A new image; the caller owns it.</returns>
    public static Image<Rgba32> Apply(Image<Rgba32> valueMap, double h, double s, ColorModel model)
    {
        // The hue, saturation and colour model are the same for every pixel, and the only
        // per-pixel input to the conversion is the value-map's R channel — a byte, so the
        // conversion has exactly 256 possible results. Compute all 256 once and index them per
        // pixel rather than re-running the HSV/HSL->RGB maths for every pixel of every layer of
        // every asset. The table is built with the identical expression the per-pixel path used
        // (R / 255.0), so the output is bit-for-bit what the per-pixel conversion produced.
        var lut = new Rgba32[256];
        for (int i = 0; i < lut.Length; i++)
        {
            double g = i / 255.0;
            RgbColor c = model == ColorModel.Hsv
                ? ColorConvert.HsvToRgb(h, s, g)
                : ColorConvert.HslToRgb(h, s, g);
            lut[i] = new Rgba32(c.R, c.G, c.B, 0);
        }

        var result = valueMap.Clone();
        result.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    // Alpha is carried through from the source pixel untouched; only the colour
                    // channels come from the table.
                    Rgba32 c = lut[row[x].R];
                    row[x] = new Rgba32(c.R, c.G, c.B, row[x].A);
                }
            }
        });
        return result;
    }
}
