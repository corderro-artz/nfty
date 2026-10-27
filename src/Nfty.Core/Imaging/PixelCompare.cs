using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

/// <summary>Comparing two pictures pixel for pixel.</summary>
public static class PixelCompare
{
    /// <summary>Whether two images have the same size and every pixel equal.</summary>
    /// <param name="a">One image.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when they are pixel-identical.</returns>
    public static bool Same(Image<Rgba32> a, Image<Rgba32> b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        bool same = true;
        a.ProcessPixelRows(b, (ra, rb) =>
        {
            for (int y = 0; y < ra.Height && same; y++)
                if (!ra.GetRowSpan(y).SequenceEqual(rb.GetRowSpan(y))) same = false;
        });
        return same;
    }
}
