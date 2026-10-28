using Nfty.App.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Viewer;

/// <summary>An archive shown as a grid of tiles: the viewer's body is the grid and the rail carries
/// the archive's facts. It has no art or model of its own.</summary>
public sealed class GallerySubject : ViewerSubject
{
    /// <summary>Wraps a gallery; the subject owns it.</summary>
    /// <param name="gallery">The tiles.</param>
    public GallerySubject(ArchiveGalleryViewModel gallery)
    {
        Gallery = gallery;
        AllowExport = gallery.AllowExport;
    }

    /// <summary>The tile grid.</summary>
    public ArchiveGalleryViewModel Gallery { get; }

    /// <inheritdoc />
    public override string Title => Gallery.Title;
    /// <inheritdoc />
    public override IReadOnlyList<ViewerFact> Facts => Gallery.Facts;
    /// <inheritdoc />
    public override Image<Rgba32>? Art => null;
    /// <inheritdoc />
    public override ViewerModel? Model => null;

    /// <inheritdoc />
    public override void Dispose() => Gallery.Dispose();
}
