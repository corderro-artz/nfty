using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// The texture every model carries: <c>[front W][gutter 1][back W]</c>, height H. The front half is
/// what the front faces (and front-owned wall voxels) show, the back half what the back faces show.
/// The one-pixel gutter repeats the front half's last column, so a <c>NEAREST</c> sample exactly on
/// the front half's right edge (u = W / (2W + 1)) cannot land in the back half.
/// </summary>
/// <remarks>
/// A single picture uses the atlas too — its back half repeats the art — so the writers and the
/// preview share ONE texture shape and ONE UV rule (<c>MeshBuilder</c>, built from the constants here).
/// </remarks>
public static class FaceAtlas
{
    /// <summary>The atlas width for art <paramref name="artWidth"/> pixels wide: <c>2W + 1</c>.</summary>
    /// <param name="artWidth">The art's width.</param>
    /// <returns>The atlas width.</returns>
    public static int WidthFor(int artWidth) => (2 * artWidth) + 1;

    /// <summary>Builds the atlas.</summary>
    /// <param name="art">The finished picture; both halves are copies of it when
    /// <paramref name="faces"/> is null. Not modified.</param>
    /// <param name="faces">The front and back owners' pictures, or null for a single picture.</param>
    /// <returns>The atlas; the caller owns it.</returns>
    /// <exception cref="ArgumentException">The faces are another size than the art.</exception>
    public static Image<Rgba32> Build(Image<Rgba32> art, StackFaces? faces = null)
    {
        ArgumentNullException.ThrowIfNull(art);
        if (faces is not null && (faces.Width != art.Width || faces.Height != art.Height))
            throw new ArgumentException(
                $"The faces are {faces.Width}x{faces.Height}, but the art is {art.Width}x{art.Height}.", nameof(faces));

        int w = art.Width, h = art.Height;
        var front = faces?.Front ?? art;
        var back = faces?.Back ?? art;
        var atlas = new Image<Rgba32>(WidthFor(w), h);
        atlas.ProcessPixelRows(front, (dst, src) =>
        {
            for (int y = 0; y < h; y++)
            {
                var s = src.GetRowSpan(y);
                var d = dst.GetRowSpan(y);
                s.CopyTo(d);
                d[w] = s[w - 1];
            }
        });
        atlas.ProcessPixelRows(back, (dst, src) =>
        {
            for (int y = 0; y < h; y++)
                src.GetRowSpan(y).CopyTo(dst.GetRowSpan(y)[(w + 1)..]);
        });
        return atlas;
    }
}
