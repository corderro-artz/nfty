using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nfty.App.ViewModels;

/// <summary>
/// HOW THE CANVAS IS LOOKED AT: the zoom, and where it is panned to.
/// </summary>
/// <remarks>
/// <para>None of this touches a pixel, a draft or the dirty flag — it is the same class of state the
/// pixel grid is, and the grid's own rule applies to all of it: view state must never mark the
/// editor dirty.</para>
///
/// <para><b>Zoom and pan are a modification of ONE function.</b> The view maps between canvas pixels
/// and control coordinates in a single place (<c>Geometry</c>), and the pointer, the marquee overlay
/// and the backdrop lattice are all mapped through it. Putting the zoom there means the thing you
/// click is the thing you painted and the lattice stays in phase, at every magnification, without a
/// second copy of the arithmetic anywhere.</para>
/// </remarks>
public partial class IngredientEditorViewModel
{
    /// <summary>The zoom that fits the whole canvas in the tile. There is nothing below it: zooming
    /// out past the art would be empty tile, which answers no question.</summary>
    public const double ZoomMin = 1;

    /// <summary>The ceiling. Sixteen times the fitted size is a pixel the size of a fingertip on a
    /// small sprite and about five device pixels per art pixel on a 1000px canvas — past that the
    /// tile shows less of the drawing than a reader can navigate by.</summary>
    public const double ZoomMax = 16;

    /// <summary>One notch of the wheel, or one press of the zoom keys.</summary>
    public const double ZoomStep = 1.25;

    /// <summary>How far one arrow-key press pans, in control pixels.</summary>
    public const double PanStep = 24;

    /// <summary>
    /// How much bigger than fitted the canvas is drawn. One is the whole art in the tile.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    [NotifyPropertyChangedFor(nameof(IsZoomed))]
    [NotifyPropertyChangedFor(nameof(ZoomTip))]
    private double _zoom = ZoomMin;

    /// <summary>How far the art is pushed sideways from centered, in control pixels. Clamped by the
    /// view, which is the only thing that knows how big the art was drawn.</summary>
    [ObservableProperty] private double _panX;

    /// <inheritdoc cref="PanX"/>
    [ObservableProperty] private double _panY;

    /// <summary>What the zoom chip reads.</summary>
    /// <remarks>
    /// "fit" rather than "100%" at the bottom of the range, because that is what the number means
    /// here — the art fills the tile, whatever its pixel count — and because it is also what
    /// clicking the chip does, so the button says its own action while it is already there.
    /// </remarks>
    public string ZoomText => IsZoomed
        ? (Zoom * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
        : "fit";

    /// <summary>Whether the canvas is magnified at all, which is also the only state pan means
    /// anything in.</summary>
    public bool IsZoomed => Zoom > ZoomMin + 0.0001;

    /// <summary>What the zoom chip says it does. It names the gestures, because none of them has a
    /// control of its own.</summary>
    public string ZoomTip => IsZoomed
        ? $"Zoomed to {ZoomText}. Click to fit. Wheel over the canvas to zoom, middle-drag or the "
          + "arrow keys to pan."
        : "The whole canvas, fitted. Wheel over it to zoom in, or press + and -.";

    /// <summary>Returns the canvas to the fitted view, centered.</summary>
    [RelayCommand]
    private void ZoomToFit() => Zoom = ZoomMin;

    /// <summary>Zooms in one notch, about the center.</summary>
    [RelayCommand]
    private void ZoomIn() => Zoom *= ZoomStep;

    /// <summary>Zooms out one notch, about the center.</summary>
    [RelayCommand]
    private void ZoomOut() => Zoom /= ZoomStep;

    /// <summary>Pans by a number of control pixels. A no-op at the fitted zoom, where there is
    /// nothing off screen to pan to.</summary>
    /// <param name="dx">Horizontal, positive moving the art right.</param>
    /// <param name="dy">Vertical, positive moving the art down.</param>
    public void PanBy(double dx, double dy)
    {
        if (!IsZoomed) return;
        PanX += dx;
        PanY += dy;
    }

    partial void OnZoomChanged(double value)
    {
        // THE CEILING BELONGS TO THE PROPERTY, the rule BrushSize and GridSize already keep: the
        // wheel, the keys and the commands cannot then disagree about the limits.
        double clamped = double.IsFinite(value) ? Math.Clamp(value, ZoomMin, ZoomMax) : ZoomMin;
        // double.IsNaN FIRST, and not folded into the comparison: |1 - NaN| > 1e-9 is FALSE, so a
        // NaN would be waved straight through a guard that looks like it catches everything. The
        // same trap WeightedRoller documents about its own total, and the same one that made the
        // rarity rail stop snapping its rows.
        if (double.IsNaN(value) || Math.Abs(clamped - value) > 1e-9) { Zoom = clamped; return; }

        // Back at fit there is nothing off screen, so a pan left over from a magnified view would
        // shove the whole art sideways inside a tile it already fits. The view clamps pan to the
        // same conclusion; this makes it true before a layout pass rather than after one.
        if (!IsZoomed) { PanX = 0; PanY = 0; }

        CanvasViewChanged();
    }

    partial void OnPanXChanged(double value) => CanvasViewChanged();
    partial void OnPanYChanged(double value) => CanvasViewChanged();

    /// <summary>The art moved under the backdrop, so the lattice, the marquee and the transform all
    /// have to follow. One event for all three: they are always read together, and a name missed
    /// from a watch list is a setting that silently stops applying.</summary>
    private void CanvasViewChanged() => BackdropChanged?.Invoke();
}
