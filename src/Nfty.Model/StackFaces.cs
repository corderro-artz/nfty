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
