using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

public static class Colorizer
{
    public static Image<Rgba32> Apply(Image<Rgba32> valueMap, double h, double s, ColorModel model)
    {
        var result = valueMap.Clone();
        result.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    double g = row[x].R / 255.0;
                    RgbColor c = model == ColorModel.Hsv
                        ? ColorConvert.HsvToRgb(h, s, g)
                        : ColorConvert.HslToRgb(h, s, g);
                    row[x] = new Rgba32(c.R, c.G, c.B, row[x].A);
                }
            }
        });
        return result;
    }
}
