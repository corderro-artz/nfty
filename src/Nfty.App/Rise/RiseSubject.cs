using Nfty.Core.Editing;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Rise;

/// <summary>
/// One picture being risen: its finished art, the depth being painted on it and the undo history of
/// that painting. The art never changes here, so risability is fixed at construction.
/// </summary>
public sealed class RiseSubject : IDisposable
{
    private readonly bool _ownsArt;
    private DepthMap? _saved;

    /// <summary>Wraps a picture and the depth it already carries.</summary>
    /// <param name="art">The finished art.</param>
    /// <param name="stored">The stored depth, or null for paper.</param>
    /// <param name="ownsArt">Whether disposing this subject disposes <paramref name="art"/>.</param>
    /// <param name="faces">The owners' face pictures of a stack, or null for a single picture whose
    /// faces are the art. Owned from here on.</param>
    public RiseSubject(Image<Rgba32> art, DepthMap? stored, bool ownsArt = true, StackFaces? faces = null)
    {
        ArgumentNullException.ThrowIfNull(art);
        // Everything that can throw is built first; nothing is assigned (so nothing can leak) until it all exists.
        DepthSurface depth;
        Image<Rgba32> texture;
        try { depth = new DepthSurface(art, stored); texture = FaceAtlas.Build(art, faces); }
        catch { faces?.Dispose(); throw; }   // the faces are taken whatever happens
        Art = art;
        Faces = faces;
        Texture = texture;
        _ownsArt = ownsArt;
        Depth = depth;
        History = new EditHistory<DepthPixel>();
        _saved = Depth.ToDepthMap();
    }

    /// <summary>The finished picture.</summary>
    public Image<Rgba32> Art { get; }

    /// <summary>The model texture: the <see cref="FaceAtlas"/> of <see cref="Art"/>, built once and
    /// owned here. The preview rasterizes it against a mesh risen from the same art.</summary>
    public Image<Rgba32> Texture { get; private set; }

    /// <summary>The stack's face owners (what the model's front and back show), or null when the
    /// picture is a single layer. Owned here; the mesh and the export read it.</summary>
    public StackFaces? Faces { get; private set; }

    /// <summary>The depth being painted.</summary>
    public DepthSurface Depth { get; private set; }

    /// <summary>Undo and redo for <see cref="Depth"/>.</summary>
    public EditHistory<DepthPixel> History { get; private set; }

    /// <summary>Whether the picture can rise at all: no partial alpha, and something solid.</summary>
    public bool IsRisable => Depth.PartialCount == 0 && Depth.SolidCount > 0;

    /// <summary>Whether the painted depth differs from what was last stored or saved.</summary>
    public bool IsDirty => !Depth.SameDepthAs(_saved);

    /// <summary>Records what a save WROTE as the stored depth.</summary>
    /// <param name="written">The map the save took from this subject and wrote, or null for paper.</param>
    /// <remarks>Never "the current depth": the page stays live while a save awaits the disk, so a stroke
    /// painted after the snapshot was taken is not in the file, and marking the surface as it is now
    /// would call that stroke saved — the footer clean, Back asking nothing, the stroke lost.</remarks>
    public void MarkSaved(DepthMap? written) => _saved = written;

    /// <summary>Starts over from a stored depth: a new surface, an empty history, nothing dirty.</summary>
    /// <param name="stored">The stored depth, or null for paper.</param>
    /// <param name="faces">The face owners that go with that depth, or null; taken.</param>
    /// <returns>What was replaced (the old texture and faces). The caller disposes it once nothing
    /// still reads them, i.e. after the page has been told.</returns>
    public IDisposable Reseed(DepthMap? stored, StackFaces? faces = null)
    {
        DepthSurface depth;
        Image<Rgba32> texture;
        try { depth = new DepthSurface(Art, stored); texture = FaceAtlas.Build(Art, faces); }
        catch { faces?.Dispose(); throw; }   // the faces are taken whatever happens
        var retired = new Retired(Texture, Faces);
        Faces = faces;
        Texture = texture;
        Depth = depth;
        History = new EditHistory<DepthPixel>();
        _saved = Depth.ToDepthMap();
        return retired;
    }

    /// <summary>Takes a recombined stack's depth and faces in place: the same <see cref="Depth"/> instance
    /// and the same <see cref="History"/>, a new <see cref="Texture"/>. A derived union is never unsaved
    /// work, so the result is marked saved.</summary>
    /// <param name="union">The stack's union depth, or null for paper.</param>
    /// <param name="faces">The face owners that go with it, or null; taken.</param>
    /// <returns>The retired texture and faces. The caller disposes it once nothing still reads them,
    /// i.e. after the page has been told.</returns>
    public IDisposable ReplaceCombined(DepthMap? union, StackFaces? faces)
    {
        Image<Rgba32> texture;
        try { texture = FaceAtlas.Build(Art, faces); }
        catch { faces?.Dispose(); throw; }   // the faces are taken whatever happens
        try { Depth.Overwrite(union); }
        catch { texture.Dispose(); faces?.Dispose(); throw; }
        var retired = new Retired(Texture, Faces);
        Faces = faces;
        Texture = texture;
        _saved = union;
        return retired;
    }

    /// <summary>Takes a rectangle of an updated stack in place: the same <see cref="Depth"/>, the same
    /// <see cref="Texture"/> image and the same <see cref="Faces"/> instance, only the pixels inside the
    /// rectangle rewritten. The result is what <see cref="ReplaceCombined"/> would give for the whole stack.</summary>
    /// <param name="rise">The stack, already updated inside <paramref name="area"/>.</param>
    /// <param name="area">The pixels that changed.</param>
    public void UpdateCombined(StackRise rise, SixLabors.ImageSharp.Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(rise);
        var r = SixLabors.ImageSharp.Rectangle.Intersect(area, new SixLabors.ImageSharp.Rectangle(0, 0, Art.Width, Art.Height));
        if (r.IsEmpty) return;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
                Depth.Set(x, y, rise.DepthAt(x, y));
        Faces?.CopyRegionFrom(rise.Faces, r);
        FaceAtlas.UpdateRegion(Texture, Art, Faces, r);
        _saved = Depth.ToDepthMap();
    }

    private sealed class Retired(Image<Rgba32> texture, StackFaces? faces) : IDisposable
    {
        public void Dispose()
        {
            texture.Dispose();
            faces?.Dispose();
        }
    }

    /// <summary>Whether <see cref="Dispose"/> has run; for tests that count what was let go.</summary>
    internal bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        IsDisposed = true;
        Texture.Dispose();
        Faces?.Dispose();
        if (_ownsArt) Art.Dispose();
    }
}
