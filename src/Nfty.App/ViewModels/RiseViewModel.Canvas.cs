using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>
/// HOW THE CANVAS IS LOOKED AT: zoom, pan and the pixel grid. Mirrors
/// <c>IngredientEditorViewModel.Zoom.cs</c> on <see cref="CanvasMapping"/>'s constants; none of it
/// touches a pixel or the dirty flag.
/// </summary>
/// <remarks>
/// <see cref="CanvasMapping.TryFrame"/> does not clamp zoom, so the clamp lives here, at the property,
/// where the wheel, the keys and the commands all go through it.
/// </remarks>
public sealed partial class RiseViewModel
{
    /// <summary>The zoom that fits the whole canvas in the tile.</summary>
    public const double ZoomMin = CanvasMapping.ZoomMin;

    /// <summary>The deepest zoom.</summary>
    public const double ZoomMax = CanvasMapping.ZoomMax;

    /// <summary>One notch of the wheel, or one press of the zoom keys.</summary>
    public const double ZoomStep = CanvasMapping.ZoomStep;

    /// <summary>How far one arrow-key press pans, in control pixels.</summary>
    public const double PanStep = CanvasMapping.PanStep;

    /// <summary>How much bigger than fitted the canvas is drawn.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    [NotifyPropertyChangedFor(nameof(IsZoomed))]
    [NotifyPropertyChangedFor(nameof(ZoomTip))]
    private double _zoom = ZoomMin;

    /// <summary>How far the art is pushed sideways from centered, in control pixels; the view clamps it.</summary>
    [ObservableProperty] private double _panX;

    /// <inheritdoc cref="PanX"/>
    [ObservableProperty] private double _panY;

    /// <summary>What the zoom chip reads: "fit" or a percentage.</summary>
    public string ZoomText => IsZoomed
        ? (Zoom * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
        : "fit";

    /// <summary>Whether the canvas is magnified at all.</summary>
    public bool IsZoomed => Zoom > ZoomMin + 0.0001;

    /// <summary>What the zoom chip says it does.</summary>
    public string ZoomTip => IsZoomed
        ? $"Zoomed to {ZoomText}. Click to fit. Wheel over the canvas to zoom, middle-drag or the "
          + "arrow keys to pan."
        : "The whole canvas, fitted. Wheel over it to zoom in, or press + and -.";

    /// <summary>Returns the canvas to the fitted view.</summary>
    [RelayCommand] private void ZoomToFit() => Zoom = ZoomMin;

    /// <summary>Zooms in one notch.</summary>
    [RelayCommand] private void ZoomIn() => Zoom *= ZoomStep;

    /// <summary>Zooms out one notch.</summary>
    [RelayCommand] private void ZoomOut() => Zoom /= ZoomStep;

    /// <summary>Pans by control pixels; a no-op while fitted.</summary>
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
        double clamped = double.IsFinite(value) ? Math.Clamp(value, ZoomMin, ZoomMax) : ZoomMin;
        // IsNaN first: |clamped - NaN| > 1e-9 is false, so a NaN would sail through the comparison.
        if (double.IsNaN(value) || Math.Abs(clamped - value) > 1e-9) { Zoom = clamped; return; }
        if (!IsZoomed) { PanX = 0; PanY = 0; }
        BackdropChanged?.Invoke();
    }

    partial void OnPanXChanged(double value) => BackdropChanged?.Invoke();
    partial void OnPanYChanged(double value) => BackdropChanged?.Invoke();

    /// <summary>The grid step, in canvas pixels per square.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridSizeText))]
    private int _gridSize = 1;

    /// <summary>Whether the backdrop draws the pixel grid rather than a flat ground.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridToggleTip))]
    private bool _showPixelGrid = true;

    /// <summary>The largest grid step this picture admits.</summary>
    public int GridSizeMax => Math.Max(1, Math.Min(Surface.Width, Surface.Height));

    /// <summary>The grid step as the chip prints it.</summary>
    public string GridSizeText => GridSize == 1 ? "1 px" : $"{GridSize} px";

    /// <summary>What the toggle says it will do next.</summary>
    public string GridToggleTip => ShowPixelGrid
        ? "Pixel grid — one square per pixel at this step. Switch to a flat background."
        : "Flat background. Switch to the pixel grid.";

    partial void OnGridSizeChanged(int value)
    {
        int clamped = Math.Clamp(value, 1, GridSizeMax);
        if (clamped != value) { GridSize = clamped; return; }
        BackdropChanged?.Invoke();
    }

    partial void OnShowPixelGridChanged(bool value) => BackdropChanged?.Invoke();

    /// <summary>The canvas was re-laid-out (zoom, pan or grid): the view rebuilds the lattice, the
    /// marquee and the transform together.</summary>
    public event Action? BackdropChanged;

    /// <summary>Switches between the pixel grid and a flat ground.</summary>
    [RelayCommand] private void ToggleGrid() => ShowPixelGrid = !ShowPixelGrid;
}
