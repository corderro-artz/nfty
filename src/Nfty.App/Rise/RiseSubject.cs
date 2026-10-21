using Nfty.Core.Editing;
using Nfty.Core.Model;
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
    public RiseSubject(Image<Rgba32> art, DepthMap? stored, bool ownsArt = true)
    {
        ArgumentNullException.ThrowIfNull(art);
        Art = art;
        _ownsArt = ownsArt;
        Depth = new DepthSurface(art, stored);
        History = new EditHistory<DepthPixel>();
        _saved = Depth.ToDepthMap();
    }

    /// <summary>The finished picture.</summary>
    public Image<Rgba32> Art { get; }

    /// <summary>The depth being painted.</summary>
    public DepthSurface Depth { get; private set; }

    /// <summary>Undo and redo for <see cref="Depth"/>.</summary>
    public EditHistory<DepthPixel> History { get; private set; }

    /// <summary>Whether the picture can rise at all: no partial alpha, and something solid.</summary>
    public bool IsRisable => Depth.PartialCount == 0 && Depth.SolidCount > 0;

    /// <summary>Whether the painted depth differs from what was last stored or saved.</summary>
    public bool IsDirty => !Depth.SameDepthAs(_saved);

    /// <summary>Records the current depth as the stored one.</summary>
    public void MarkSaved() => _saved = Depth.ToDepthMap();

    /// <summary>Starts over from a stored depth: a new surface, an empty history, nothing dirty.</summary>
    /// <param name="stored">The stored depth, or null for paper.</param>
    public void Reseed(DepthMap? stored)
    {
        Depth = new DepthSurface(Art, stored);
        History = new EditHistory<DepthPixel>();
        _saved = Depth.ToDepthMap();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsArt) Art.Dispose();
    }
}
