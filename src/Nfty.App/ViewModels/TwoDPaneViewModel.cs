using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Backdrop behind the viewer's 2D art.</summary>
public enum ViewerBackdrop
{
    /// <summary>The transparency checker.</summary>
    Checker,
    /// <summary>A flat light ground.</summary>
    Light,
    /// <summary>A flat dark ground.</summary>
    Dark,
    /// <summary>The author's own color, <see cref="TwoDPaneViewModel.BackdropColor"/>.</summary>
    Custom,
}

/// <summary>The viewer's 2D pane: zoom, pan, backdrop and pixel grid. All view state; it never
/// touches the art.</summary>
public sealed partial class TwoDPaneViewModel : ObservableObject, IZoomPan
{
    /// <summary>The deepest zoom.</summary>
    public const double ZoomMax = 64;

    private double _zoom = 1;
    private double _artW, _artH, _surfaceW, _surfaceH;

    /// <summary>Magnification over fitted, clamped to [1, 64].</summary>
    public double Zoom
    {
        get => _zoom;
        set => SetProperty(ref _zoom, Math.Clamp(double.IsFinite(value) ? value : 1, CanvasMapping.ZoomMin, ZoomMax));
    }

    /// <summary>Horizontal pan from centered.</summary>
    [ObservableProperty] private double _panX;

    /// <summary>Vertical pan from centered.</summary>
    [ObservableProperty] private double _panY;

    /// <summary>The backdrop choice.</summary>
    [ObservableProperty] private ViewerBackdrop _backdrop = ViewerBackdrop.Checker;

    /// <summary>The color used when <see cref="Backdrop"/> is Custom, as #RRGGBB.</summary>
    [ObservableProperty] private string _backdropColor = "#808080";

    /// <summary>Whether the pixel grid is drawn.</summary>
    [ObservableProperty] private bool _pixelGrid;

    /// <summary>The lattice step; the viewer's grid is one art pixel.</summary>
    public int GridSize => 1;

    bool IZoomPan.ShowPixelGrid => PixelGrid;

    /// <summary>Tells the pane the art and surface sizes, which 1:1 needs.</summary>
    /// <param name="artW">Art width in pixels.</param>
    /// <param name="artH">Art height in pixels.</param>
    /// <param name="surfaceW">Surface width in device pixels.</param>
    /// <param name="surfaceH">Surface height in device pixels.</param>
    public void SetSizes(double artW, double artH, double surfaceW, double surfaceH)
    {
        _artW = artW; _artH = artH; _surfaceW = surfaceW; _surfaceH = surfaceH;
    }

    /// <summary>Fitted and centered.</summary>
    [RelayCommand]
    private void Fit()
    {
        Zoom = 1;
        PanX = 0;
        PanY = 0;
    }

    /// <summary>One art pixel per device pixel: the fitted scale, inverted.</summary>
    [RelayCommand]
    private void OneToOne()
    {
        if (_artW <= 0 || _artH <= 0 || _surfaceW <= 0 || _surfaceH <= 0) return;
        double fit = Math.Min(_surfaceW / _artW, _surfaceH / _artH);
        Zoom = 1 / fit;
        PanX = 0;
        PanY = 0;
    }
}
