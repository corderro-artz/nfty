using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// Tiny synthetic art, built in memory. '#' is fully opaque, '.' is fully clear, '~' is half-alpha —
/// the one thing a voxel model cannot represent. Every opaque pixel gets a color of its own, so a
/// texture test can tell which pixel a texel came from.
/// </summary>
internal static class Art
{
    public static Image<Rgba32> FromAscii(params string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = rows[y][x] switch
                {
                    '#' => Opaque(x, y),
                    '~' => new Rgba32(200, 40, 40, 128),
                    _ => new Rgba32(0, 0, 0, 0),
                };
        return img;
    }

    public static Rgba32 Opaque(int x, int y) => new((byte)(10 + (x * 20)), (byte)(10 + (y * 20)), 200, 255);

    /// <summary>A depth map over the whole image, one (front, back) per pixel, 0..255 each.</summary>
    public static DepthMap Depth(Image<Rgba32> art, Func<int, int, (int Front, int Back)> at)
    {
        var bytes = new byte[art.Width * art.Height * 2];
        for (int y = 0; y < art.Height; y++)
            for (int x = 0; x < art.Width; x++)
            {
                var (front, back) = at(x, y);
                int i = ((y * art.Width) + x) * 2;
                bytes[i] = checked((byte)front);
                bytes[i + 1] = checked((byte)back);
            }
        return DepthMap.FromBytes(art.Width, art.Height, bytes);
    }
}
