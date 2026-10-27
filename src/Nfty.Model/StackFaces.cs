using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// The two face pictures of a risen stack: what the front and the back of each column show, each
/// taken from the layer that OWNS that face by the union rule, plus the seam where a column's wall
/// switches from one owner's color to the other's.
/// </summary>
/// <remarks>
/// Owns both images. Non-solid pixels are clear in both. For an all-paper stack both faces equal the
/// art and every seam is 0.
/// </remarks>
public sealed class StackFaces : IDisposable
{
    private readonly int[] _backColored;

    internal StackFaces(Image<Rgba32> front, Image<Rgba32> back, int[] backColored)
    {
        Front = front;
        Back = back;
        _backColored = backColored;
    }

    /// <summary>What the front of each column shows.</summary>
    public Image<Rgba32> Front { get; }

    /// <summary>What the back of each column shows.</summary>
    public Image<Rgba32> Back { get; }

    /// <summary>Width in pixels.</summary>
    public int Width => Front.Width;

    /// <summary>Height in pixels.</summary>
    public int Height => Front.Height;

    /// <summary>How many voxels, counted from the column's back end, take the back color; the rest
    /// take the front color. 0 on a non-solid pixel.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row, 0 at the top.</param>
    /// <returns>The voxel count.</returns>
    public int BackColored(int x, int y) => _backColored[(y * Width) + x];

    internal int[] SeamsArray => _backColored;

    /// <summary>Copies a rectangle of another set of faces over this one, in place (the same size).</summary>
    /// <param name="source">The faces to read.</param>
    /// <param name="area">The pixels to copy; clipped to the picture.</param>
    /// <exception cref="ArgumentException">The faces are another size.</exception>
    public void CopyRegionFrom(StackFaces source, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException("The faces are another size.", nameof(source));
        var r = Rectangle.Intersect(area, new Rectangle(0, 0, Width, Height));
        if (r.IsEmpty) return;
        for (int y = r.Top; y < r.Bottom; y++)
        {
            for (int x = r.Left; x < r.Right; x++)
            {
                Front[x, y] = source.Front[x, y];
                Back[x, y] = source.Back[x, y];
            }
            Array.Copy(source._backColored, (y * Width) + r.Left, _backColored, (y * Width) + r.Left, r.Width);
        }
    }

    /// <summary>An independent copy; the caller owns it.</summary>
    /// <returns>The copy.</returns>
    public StackFaces Clone() => new(Front.Clone(), Back.Clone(), (int[])_backColored.Clone());

    /// <summary>The faces of a single picture: both are the art, and the seam is 0.</summary>
    /// <param name="art">The picture; cloned, not taken.</param>
    /// <returns>Faces the caller owns.</returns>
    public static StackFaces Uniform(Image<Rgba32> art)
    {
        ArgumentNullException.ThrowIfNull(art);
        return new StackFaces(art.Clone(), art.Clone(), new int[art.Width * art.Height]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Front.Dispose();
        Back.Dispose();
    }
}
