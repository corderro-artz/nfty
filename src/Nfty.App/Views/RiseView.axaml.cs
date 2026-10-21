using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nfty.App.Controls;
using Nfty.App.Services;
using Nfty.App.ViewModels;
// Both Avalonia and the engine have a PixelRect; the overlay speaks the engine's.
using PixelRect = Nfty.Core.Editing.PixelRect;

namespace Nfty.App.Views;

/// <summary>
/// The Rise page. Markup carries the layout; this file carries only what genuinely needs a control
/// reference: the canvas geometry (shared with the Ingredient Editor through <see cref="CanvasMapping"/>),
/// the lattice backdrop, the marquee, the depth labels, and the ONE 3D view moving between its two homes.
/// </summary>
/// <remarks>
/// <para><b>Never two <see cref="ModelPreviewView"/>s.</b> The tile and the big slot trade places by
/// moving the single instance between <c>TileHost</c> and <c>BigHost</c>; two instances would be two
/// bitmaps, two camera subscriptions and two answers to "what is the model looking at".</para>
/// <para>The pointer gestures are a later step: this file maps, draws and hosts.</para>
/// </remarks>
public partial class RiseView : UserControl
{
    private Image _img = null!;
    private Panel _surface = null!;
    private Canvas _overlay = null!;
    private Panel _backdrop = null!;
    private DepthLabelLayer _labels = null!;
    private Border _tileHost = null!;
    private Border _bigHost = null!;
    private ModelPreviewView _model = null!;
    private RiseViewModel? _vm;

    // The geometry the backdrop was last built from; rebuilt only when one of these actually moves
    // (Bounds fires on every layout pass, and a DrawingBrush per pass is the churn to keep out).
    private (double Scale, double OffX, double OffY, int Step, bool Grid) _backdropFrom = (-1, -1, -1, -1, false);

    /// <summary>Loads the view. It constructs and lays out with no DataContext.</summary>
    public RiseView()
    {
        InitializeComponent();

        _img = this.FindControl<Image>("CanvasImage")!;
        _surface = this.FindControl<Panel>("CanvasSurface")!;
        _overlay = this.FindControl<Canvas>("CanvasOverlay")!;
        _backdrop = this.FindControl<Panel>("CanvasBackdrop")!;
        _labels = this.FindControl<DepthLabelLayer>("DepthLabels")!;
        _tileHost = this.FindControl<Border>("TileHost")!;
        _bigHost = this.FindControl<Border>("BigHost")!;
        _model = this.FindControl<ModelPreviewView>("ModelView")!;

        // The page takes focus on arrival: its keys are KeyBindings, which only see a key that routes
        // through this control. The 3D view takes focus only on ITS first attach (the page wins here).
        AttachedToVisualTree += (_, _) => Focus();

        DataContextChanged += (_, _) => Rebind();
        _surface.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => Reframe()));
        // And the backdrop's own bounds: the art sits in a fixed host centered in it, so the host's
        // POSITION keeps moving as the pane resizes after the image has stopped changing size.
        _backdrop.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => BuildBackdrop()));
        // The bitmap is what the geometry is mapped against: a new picture is a new frame. Watched on
        // the Image itself, so the frame is never read against a bitmap the binding has not delivered.
        _img.GetObservable(Image.SourceProperty).Subscribe(new Sub<IImage?>(_ => Reframe()));
        Rebind();
    }

    private void Rebind()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmChanged;
            _vm.BackdropChanged -= OnBackdropChanged;
            _vm.DepthChanged -= OnDepthChanged;
            _vm.SubjectChanged -= OnSubjectChanged;
            _vm.GestureEnded -= OnGestureEnded;
        }
        _vm = DataContext as RiseViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmChanged;
            _vm.BackdropChanged += OnBackdropChanged;
            _vm.DepthChanged += OnDepthChanged;
            _vm.SubjectChanged += OnSubjectChanged;
            _vm.GestureEnded += OnGestureEnded;
        }
        PlaceModel();
        _labels.Surface = _vm?.Surface;
        ApplyCanvasTransform();
        Reframe();
        BuildBackdrop();
        DrawBand();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RiseViewModel.PreviewSwapped):
                PlaceModel();
                break;
            case nameof(RiseViewModel.Surface):
                // A different item (or a rerolled roll) has a different surface to label.
                if (_vm is not null) _labels.Surface = _vm.Surface;
                Reframe();
                break;
            case nameof(RiseViewModel.Selection):
            case nameof(RiseViewModel.ActiveTool):
                DrawBand();
                break;
        }
    }

    /// <summary>The depth under the labels changed: they repaint (and nothing else does).</summary>
    private void OnDepthChanged() => _labels.InvalidateVisual();

    /// <summary>A gesture ended: the 3D view's next paint is exact rather than budgeted.</summary>
    private void OnGestureEnded() => _model.HostInteracting = false;

    /// <summary>The picture changed: the camera re-frames with the real aspect of the slot the 3D view
    /// is in.</summary>
    /// <remarks>
    /// Deliberately NOT followed by a synchronous repaint. This is raised from inside the source's
    /// change (a reroll disposes the old picture first), and the 3D view's <c>Mesh</c> and
    /// <c>Texture</c> are bindings that have not been told yet - painting here would rasterize the
    /// OLD mesh against a texture that has just been disposed. The camera reset marks the view dirty,
    /// and its posted repaint runs after the bindings have caught up.
    /// </remarks>
    private void OnSubjectChanged()
    {
        if (_vm is null) return;
        _vm.Preview.Reset(_model.Bounds.Width >= 1 && _model.Bounds.Height >= 1
            ? (float)(_model.Bounds.Width / _model.Bounds.Height)
            : 1f);
    }

    /// <summary>Puts the one 3D view where the page says it lives: the tile, or the big slot.</summary>
    private void PlaceModel()
    {
        var host = _vm?.PreviewSwapped == true ? _bigHost : _tileHost;
        if (ReferenceEquals(_model.Parent, host)) return;
        // Detached before it is attached again: a control has exactly one logical parent.
        if (_model.Parent is Border from) from.Child = null;
        host.Child = _model;
    }

    /// <summary>The canvas has been re-laid-out (zoom, pan or grid): the transform, the pan clamp, the
    /// lattice, the marquee and the labels all follow from the same geometry and are never wanted apart.</summary>
    private void OnBackdropChanged()
    {
        ApplyCanvasTransform();
        ClampPan();
        Reframe();
    }

    /// <summary>Re-reads the geometry for everything that is drawn from it.</summary>
    private void Reframe()
    {
        _labels.Frame = Geometry(out var scale, out var offX, out var offY)
            ? new CanvasFrame(scale, offX, offY)
            : default;
        DrawBand();
        BuildBackdrop();
    }

    /// <summary>The mapping between canvas pixels and control coordinates: the letterbox a
    /// <c>Stretch="Uniform"</c> image leaves inside its box, times the zoom, plus the pan. The
    /// arithmetic is <see cref="CanvasMapping"/>'s; the zoom it is given is the view model's, which
    /// clamps it (<c>TryFrame</c> does not).</summary>
    private bool Geometry(out double scale, out double offX, out double offY)
    {
        scale = offX = offY = 0;
        if (_img.Source is not Bitmap bmp) return false;
        if (!CanvasMapping.TryFrame(bmp.PixelSize.Width, bmp.PixelSize.Height,
                _surface.Bounds.Width, _surface.Bounds.Height,
                _vm?.Zoom ?? 1, _vm?.PanX ?? 0, _vm?.PanY ?? 0, out var frame)) return false;
        (scale, offX, offY) = (frame.Scale, frame.OffX, frame.OffY);
        return true;
    }

    /// <summary>Draws the art at the zoom and pan <see cref="Geometry"/> reports: scaling about the
    /// CENTER keeps a centered letterbox centered, so it is a plain scale-and-translate on the image
    /// alone. The overlay and the labels are not transformed - they draw in control coordinates.</summary>
    private void ApplyCanvasTransform()
    {
        double zoom = _vm?.Zoom ?? 1;
        double px = _vm?.PanX ?? 0, py = _vm?.PanY ?? 0;

        if (_img.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] })
        {
            s.ScaleX = s.ScaleY = zoom;
            t.X = px; t.Y = py;
            return;
        }

        _img.RenderTransformOrigin = RelativePoint.Center;
        _img.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(zoom, zoom), new TranslateTransform(px, py) },
        };
    }

    /// <summary>Keeps the art overlapping the tile it is drawn in.</summary>
    private void ClampPan()
    {
        if (_vm is null || _img.Source is not Bitmap bmp) return;
        var (x, y) = CanvasMapping.ClampPan(bmp.PixelSize.Width, bmp.PixelSize.Height,
            _surface.Bounds.Width, _surface.Bounds.Height, _vm.Zoom, _vm.PanX, _vm.PanY);
        // Assigned unconditionally: the generated setter drops an equal value, so this settles in one
        // hop rather than re-entering through the notification it would otherwise raise.
        _vm.PanX = x;
        _vm.PanY = y;
    }

    /// <summary>
    /// Paints the backdrop as a lattice of the ART'S OWN PIXELS, or as a flat ground: the editor's
    /// rule, through the shared <see cref="PixelLattice"/>. Both the square size and its phase come
    /// from the laid-out art, and a square under two device pixels is drawn flat instead.
    /// </summary>
    private void BuildBackdrop()
    {
        if (_backdrop is null) return;

        int step = _vm?.GridSize ?? 1;
        bool grid = _vm?.ShowPixelGrid ?? false;

        if (!grid || !Geometry(out var scale, out var offX, out var offY)) { Flatten(step); return; }

        double cell = step * scale;
        if (cell < 2) { Flatten(step); return; }

        // The art's corner in the BACKDROP's coordinates, translated from the SURFACE: the image
        // carries the zoom as a render transform, and TranslatePoint applies one.
        if (_surface.TranslatePoint(new Point(offX, offY), _backdrop) is not { } art) return;

        var next = (scale, art.X, art.Y, step, true);
        if (_backdropFrom == next && _backdrop.Background is DrawingBrush) return;

        var light = Brush("BgAltBrush");
        var dark = Brush("BgAlt2Brush");
        // Before the control is attached there is no theme to read a brush out of, and caching that
        // failure would leave the backdrop unpainted for the life of the page.
        if (light is null || dark is null) return;
        _backdropFrom = next;
        _backdrop.Background = PixelLattice.Build(cell, art.X, art.Y, light, dark);
    }

    private void Flatten(int step)
    {
        var flat = Brush("BgAltBrush");
        if (flat is null) return;                       // no theme yet; try again after attach
        if (ReferenceEquals(_backdrop.Background, flat)) return;
        _backdropFrom = (0, 0, 0, step, false);
        _backdrop.Background = flat;
    }

    /// <summary>Repaints the overlay: the standing marquee, as a dashed accent outline. Select marks
    /// without changing a pixel, so the outline is the whole feedback; every other tool shows its
    /// effect in the depth itself.</summary>
    private void DrawBand()
    {
        _overlay.Children.Clear();
        if (_vm is null || _vm.Selection is not { } sel || !Geometry(out var scale, out var offX, out var offY)) return;

        Point ToControl(int px, int py) => new(offX + px * scale, offY + py * scale);
        var tl = ToControl(sel.X, sel.Y);
        var br = ToControl(sel.X + sel.Width, sel.Y + sel.Height);
        var marquee = new Rectangle
        {
            Width = Math.Max(1, br.X - tl.X),
            Height = Math.Max(1, br.Y - tl.Y),
            Stroke = Brush("AccentBrush"),
            StrokeThickness = 1,
            StrokeDashArray = new AvaloniaList<double>(4, 3),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(marquee, tl.X);
        Canvas.SetTop(marquee, tl.Y);
        _overlay.Children.Add(marquee);
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Minimal <see cref="IObserver{T}"/> for an Avalonia property subscription — the
    /// framework hands out <c>IObservable</c> and the BCL has no lambda overload for it.</summary>
    private sealed class Sub<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}
