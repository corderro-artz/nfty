namespace Nfty.App.ViewModels;

/// <summary>
/// The zoom, pan and pixel-grid state a zoomable pixel canvas reads and writes. Implemented by the
/// Ingredient editor and the Rise page, so one host (<c>PixelCanvasHost</c>) serves both.
/// </summary>
internal interface IZoomPan
{
    /// <summary>Magnification, already clamped by the implementer.</summary>
    double Zoom { get; set; }

    /// <summary>Horizontal pan, in control pixels, measured from centered.</summary>
    double PanX { get; set; }

    /// <summary>Vertical pan, in control pixels, measured from centered.</summary>
    double PanY { get; set; }

    /// <summary>The lattice step, in canvas pixels.</summary>
    int GridSize { get; }

    /// <summary>Whether the lattice backdrop is drawn at all.</summary>
    bool ShowPixelGrid { get; }
}
