using Nfty.Core.Editing;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// What the stack rule makes of a stack: the art, the union depth map and the face owners' pictures.
/// Owns <see cref="Art"/> and <see cref="Faces"/>.
/// </summary>
public sealed class StackRise : IDisposable
{
    private readonly int[] _frontOwner;
    private readonly int[] _backOwner;
    private readonly byte[] _union;

    internal StackRise(Image<Rgba32> art, byte[] union, StackFaces faces, int[] frontOwner, int[] backOwner)
    {
        Art = art;
        _union = union;
        Faces = faces;
        _frontOwner = frontOwner;
        _backOwner = backOwner;
    }

    /// <summary>The layer that owns the front face at a pixel.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>The bottom-first index into the layers given to the combine (for a selection or
    /// asset, into the PRESENT picks in the order given); -1 on a pixel no layer covers. Ties go to
    /// the upper layer.</returns>
    public int FrontOwner(int x, int y) => _frontOwner[Index(x, y)];

    /// <summary>The layer that owns the back face at a pixel.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>As <see cref="FrontOwner"/>, for the back face.</returns>
    public int BackOwner(int x, int y) => _backOwner[Index(x, y)];

    private int Index(int x, int y)
    {
        if ((uint)x >= (uint)Art.Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Art.Height) throw new ArgumentOutOfRangeException(nameof(y));
        return (y * Art.Width) + x;
    }

    /// <summary>The picture the stack shows from the front (the compositor's composite, or the
    /// finished picture it was given).</summary>
    public Image<Rgba32> Art { get; }

    /// <summary>Per pixel, the union of the solid layers' columns, as a map of its own (a copy of the
    /// current state: <see cref="StackDepth.CombineInto"/> updates a rise in place).</summary>
    public DepthMap Depth => DepthMap.FromBytes(Art.Width, Art.Height, _union);

    /// <summary>The union depth at a pixel, without building a map.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>The pair; 0/0 on a pixel no layer covers.</returns>
    public DepthPixel DepthAt(int x, int y)
    {
        int i = Index(x, y) * 2;
        return new DepthPixel(_union[i], _union[i + 1]);
    }

    internal byte[] UnionBytes => _union;

    internal int[] FrontOwners => _frontOwner;

    internal int[] BackOwners => _backOwner;

    /// <summary>The front and back owners' pictures and the wall seams.</summary>
    public StackFaces Faces { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Art.Dispose();
        Faces.Dispose();
    }
}
