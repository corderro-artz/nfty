using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Reactive;
using Nfty.App.Controls;
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>
/// The pop-out viewer. Markup carries the layout; this file carries only what needs a control
/// reference: the canvas geometry (shared with the editor and the Rise page through
/// <see cref="PixelCanvasHost"/>), the backdrop, the pointer, and the 3D view's inputs.
/// </summary>
/// <remarks>
/// <para><b>Zoom is not reimplemented.</b> The wheel and the drag call <see cref="PixelCanvasHost"/>,
/// whose <c>TryGeometry</c> is the one function the zoom exists in, so the pixel under the pointer,
/// the lattice, and the grid lines move together by construction.</para>
/// <para><b>The pointer talks to the untransformed surface</b>, never the image, as everywhere else
/// that draws on a zoomed canvas.</para>
/// <para>Nothing here writes to the subject; a viewer's only writes are Save image, Copy and the
/// remembered window state.</para>
/// </remarks>
public partial class ViewerView : UserControl
{
    private readonly Image _img;
    private readonly Panel _surface;
    private readonly Panel _backdrop;
    private readonly PixelGridLayer _grid;
    private readonly Grid _body;
    private readonly ModelPreviewView _model;
    private readonly PixelCanvasHost _canvas;
    private ViewerViewModel? _vm;
    private bool _framed;
    private bool _panning;
    private Point _panFrom;

    /// <summary>Loads the view. It constructs and lays out with no DataContext.</summary>
    public ViewerView()
    {
        InitializeComponent();
        _img = this.FindControl<Image>("CanvasImage")!;
        _surface = this.FindControl<Panel>("CanvasSurface")!;
        _backdrop = this.FindControl<Panel>("CanvasBackdrop")!;
        _grid = this.FindControl<PixelGridLayer>("GridLines")!;
        _body = this.FindControl<Grid>("PaneGrid")!;
        _model = this.FindControl<ModelPreviewView>("ModelView")!;
        _canvas = new PixelCanvasHost(_img, _surface, _backdrop, () => _vm?.TwoD, Brush);

        _surface.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(_ => Reframe()));
        // The 3D camera is framed on the pane's real shape once it has one, and again whenever the
        // layout changes the pane's shape: a camera framed for a square pane crops a model in a tall
        // narrow one. Never on an ordinary resize, which would throw away an orbit the reader made.
        _model.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(b =>
        {
            if (_vm is not null && b.Width >= 1 && b.Height >= 1) _vm.Preview.ViewportAspect = (float)(b.Width / b.Height);
            if (_framed || _vm is null || b.Width < 1 || b.Height < 1) return;
            _framed = true;
            _vm.Preview.Reset((float)(b.Width / b.Height));
        }));
        _img.GetObservable(Image.SourceProperty).Subscribe(new AnonymousObserver<IImage?>(_ => Reframe()));
        DataContextChanged += (_, _) => Rebind();
        ActualThemeVariantChanged += (_, _) => { _canvas.Detach(); ApplyBackdrop(); };
        AttachedToVisualTree += (_, _) => { _canvas.Detach(); ApplyBackdrop(); };
        WirePointer();
        Rebind();
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;

    private void Rebind()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmChanged;
            _vm.TwoD.PropertyChanged -= OnTwoDChanged;
        }
        _vm = DataContext as ViewerViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmChanged;
            _vm.TwoD.PropertyChanged += OnTwoDChanged;
        }
        _model.DataContext = _vm?.Preview;
        if (_vm is not null) _vm.StillSource = _model.EncodeStill;
        _framed = false;
        _model.Mesh = _vm?.Mesh;
        _model.Texture = _vm?.Texture;
        ApplyColumns();
        _canvas.Detach();
        Reframe();
        ApplyBackdrop();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewerViewModel.TwoDColumn):
            case nameof(ViewerViewModel.ThreeDColumn):
                ApplyColumns();
                _framed = false;
                break;
            case nameof(ViewerViewModel.Mesh):
            case nameof(ViewerViewModel.Texture):
                _model.Mesh = _vm?.Mesh;
                _model.Texture = _vm?.Texture;
                break;
        }
    }

    private void OnTwoDChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TwoDPaneViewModel.Backdrop):
            case nameof(TwoDPaneViewModel.BackdropColor):
                ApplyBackdrop();
                break;
            case nameof(TwoDPaneViewModel.Zoom):
            case nameof(TwoDPaneViewModel.PanX):
            case nameof(TwoDPaneViewModel.PanY):
                Reframe();
                break;
        }
    }

    // The pair of pane columns is fixed; a layout only moves the hidden one to zero. Columns are not
    // bindable, so the view sets them from the two lengths the view model computes.
    private void ApplyColumns()
    {
        if (_vm is null) return;
        _body.ColumnDefinitions[0].Width = _vm.TwoDColumn;
        _body.ColumnDefinitions[1].Width = _vm.ThreeDColumn;
    }

    private void Reframe()
    {
        _canvas.ApplyCanvasTransform();
        _canvas.ClampPan();
        if (_vm is not null && _img.Source is Bitmap bmp)
            _vm.TwoD.SetSizes(bmp.PixelSize.Width, bmp.PixelSize.Height, _surface.Bounds.Width, _surface.Bounds.Height);
        _grid.PixelWidth = (_img.Source as Bitmap)?.PixelSize.Width ?? 0;
        _grid.PixelHeight = (_img.Source as Bitmap)?.PixelSize.Height ?? 0;
        _grid.Frame = _canvas.Frame();
        if (_vm?.TwoD.Backdrop == ViewerBackdrop.Checker) _canvas.RebuildBackdrop();
    }

    // Checker is the editor's lattice of the art's own pixels; Light and Dark are flat theme grounds,
    // Custom the reader's own color. The lattice host is only asked while it is the choice, so it
    // never repaints over a flat ground.
    private void ApplyBackdrop()
    {
        if (_vm is null) return;
        var two = _vm.TwoD;
        switch (two.Backdrop)
        {
            case ViewerBackdrop.Checker:
                _canvas.Detach();
                _canvas.RebuildBackdrop();
                break;
            case ViewerBackdrop.Light:
                _backdrop.Background = Brush("TileBrush");
                break;
            case ViewerBackdrop.Dark:
                _backdrop.Background = Brush("PanelBrush");
                break;
            default:
                _backdrop.Background = Color.TryParse(two.BackdropColor, out var c)
                    ? new SolidColorBrush(c)
                    : Brush("TileBrush");
                break;
        }
    }

    // ---------------------------------------------------------------- the pointer

    private void WirePointer()
    {
        _surface.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(_surface).Properties;
            if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed) return;
            e.Pointer.Capture(_surface);
            _panning = true;
            _panFrom = e.GetPosition(_surface);
            e.Handled = true;
        };

        _surface.PointerMoved += (_, e) =>
        {
            if (!_panning || _vm is null) return;
            var now = e.GetPosition(_surface);
            _vm.TwoD.PanX += now.X - _panFrom.X;
            _vm.TwoD.PanY += now.Y - _panFrom.Y;
            _panFrom = now;
            _canvas.ClampPan();
        };

        _surface.PointerReleased += (_, e) =>
        {
            if (!_panning) return;
            _panning = false;
            e.Pointer.Capture(null);
        };

        // Capture can be taken away (the window deactivates); no release is coming.
        _surface.PointerCaptureLost += (_, _) => _panning = false;

        _surface.PointerWheelChanged += (_, e) =>
        {
            if (_vm is null || e.Delta.Y == 0) return;
            _canvas.ZoomAt(e.GetPosition(_surface),
                e.Delta.Y > 0 ? CanvasMapping.ZoomStep : 1 / CanvasMapping.ZoomStep);
            e.Handled = true;
        };
    }
}
