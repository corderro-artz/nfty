using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

/// <summary>Whole-pixel enlargement for export: every source pixel becomes an exact factor-by-factor
/// block, so pixel art stays pixel art. No resampler is involved.</summary>
public static class Upscale
{
    /// <summary>Enlarges <paramref name="src"/> by an integer factor.</summary>
    /// <param name="src">The image; left untouched.</param>
    /// <param name="factor">1, 2, 4 or 8.</param>
    /// <returns>A new image the caller owns.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The factor is not 1, 2, 4 or 8.</exception>
    public static Image<Rgba32> By(Image<Rgba32> src, int factor)
    {
        ArgumentNullException.ThrowIfNull(src);
        if (factor is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be 1, 2, 4 or 8.");

        var dst = new Image<Rgba32>(src.Width * factor, src.Height * factor);
        for (int y = 0; y < src.Height; y++)
        {
            for (int dy = 0; dy < factor; dy++)
            {
                int oy = y * factor + dy;
                for (int x = 0; x < src.Width; x++)
                {
                    var px = src[x, y];
                    for (int dx = 0; dx < factor; dx++) dst[x * factor + dx, oy] = px;
                }
            }
        }
        return dst;
    }
}
