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
    internal StackRise(Image<Rgba32> art, DepthMap depth, StackFaces faces)
    {
        Art = art;
        Depth = depth;
        Faces = faces;
    }

    /// <summary>The picture the stack shows from the front (the compositor's composite, or the
    /// finished picture it was given).</summary>
    public Image<Rgba32> Art { get; }

    /// <summary>Per pixel, the union of the solid layers' columns.</summary>
    public DepthMap Depth { get; }

    /// <summary>The front and back owners' pictures and the wall seams.</summary>
    public StackFaces Faces { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Art.Dispose();
        Faces.Dispose();
    }
}
