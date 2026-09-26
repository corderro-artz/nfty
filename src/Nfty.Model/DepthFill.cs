using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// Fill-all: the one action that replaced the old Front / Symmetric / Raised modes. It sets every
/// solid pixel of a picture to the same (front, back) at once.
/// </summary>
public static class DepthFill
{
    /// <summary>A depth map giving every solid pixel of <paramref name="art"/> the same depth.</summary>
    /// <param name="art">The picture. Solid means <see cref="Opacity.IsSolid"/>.</param>
    /// <param name="front">Voxels toward the viewer, 0..255.</param>
    /// <param name="back">Voxels away from the viewer, 0..255.</param>
    /// <returns>A map the size of the art: (front, back) on every solid pixel, zero elsewhere. It
    /// REPLACES any stored map — fill-all touches every solid pixel, so nothing of the old one
    /// survives it — which is why this takes none.</returns>
    public static DepthMap All(Image<Rgba32> art, byte front, byte back)
    {
        ArgumentNullException.ThrowIfNull(art);
        var bytes = new byte[checked(art.Width * art.Height * 2)];
        for (int y = 0; y < art.Height; y++)
        {
            for (int x = 0; x < art.Width; x++)
            {
                if (!Opacity.IsSolid(art[x, y])) continue;
                int i = ((y * art.Width) + x) * 2;
                bytes[i] = front;
                bytes[i + 1] = back;
            }
        }
        return DepthMap.FromBytes(art.Width, art.Height, bytes);
    }
}
