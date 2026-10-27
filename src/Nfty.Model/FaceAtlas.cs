using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// The texture every model carries: <c>[front W][gutter 1][back W]</c>, height H. The front half is
/// what the front faces (and front-owned wall voxels) show, the back half what the back faces show.
/// The one-pixel gutter repeats the front half's last column. The sampler is <c>NEAREST</c>, so there is
/// no smoothing to bleed; the gutter guards the edge texel: a sample exactly on the front half's right
/// edge (u = W / (2W + 1)), or pushed there by float rounding, reads the gutter, never the back half.
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

        var atlas = new Image<Rgba32>(WidthFor(art.Width), art.Height);
        UpdateRegion(atlas, art, faces, new Rectangle(0, 0, art.Width, art.Height));
        return atlas;
    }

    /// <summary>Rewrites a rectangle of an atlas from the art and faces it was built from, in place. The
    /// atlas layout lives here and only here: <see cref="Build"/> is this over the whole picture.</summary>
    /// <param name="atlas">An atlas made by <see cref="Build"/> for this art's size; changed in place.</param>
    /// <param name="art">The finished picture; used for both halves when <paramref name="faces"/> is null.</param>
    /// <param name="faces">The front and back owners' pictures, or null for a single picture.</param>
    /// <param name="area">The art pixels to copy; clipped to the picture.</param>
    /// <exception cref="ArgumentException">The atlas or faces are another size than the art.</exception>
    public static void UpdateRegion(Image<Rgba32> atlas, Image<Rgba32> art, StackFaces? faces, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(art);
        int w = art.Width, h = art.Height;
        if (atlas.Width != WidthFor(w) || atlas.Height != h)
            throw new ArgumentException($"The atlas is {atlas.Width}x{atlas.Height}; art {w}x{h} needs {WidthFor(w)}x{h}.", nameof(atlas));
        if (faces is not null && (faces.Width != w || faces.Height != h))
            throw new ArgumentException(
                $"The faces are {faces.Width}x{faces.Height}, but the art is {w}x{h}.", nameof(faces));
        var r = Rectangle.Intersect(area, new Rectangle(0, 0, w, h));
        if (r.IsEmpty) return;

        var front = faces?.Front ?? art;
        var back = faces?.Back ?? art;
        atlas.ProcessPixelRows(front, (dst, src) =>
        {
            for (int y = r.Top; y < r.Bottom; y++)
            {
                var s = src.GetRowSpan(y);
                var d = dst.GetRowSpan(y);
                s.Slice(r.Left, r.Width).CopyTo(d[r.Left..]);
                if (r.Right == w) d[w] = s[w - 1];
            }
        });
        atlas.ProcessPixelRows(back, (dst, src) =>
        {
            for (int y = r.Top; y < r.Bottom; y++)
                src.GetRowSpan(y).Slice(r.Left, r.Width).CopyTo(dst.GetRowSpan(y)[(w + 1 + r.Left)..]);
        });
    }
}
