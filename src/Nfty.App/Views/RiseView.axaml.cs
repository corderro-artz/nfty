using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nfty.App.Controls;
using Nfty.App.Services;
using Nfty.App.ViewModels;
// Both Avalonia and the engine have a PixelRect; the overlay speaks the engine's.
using DepthSide = Nfty.Core.Editing.DepthSide;
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
/// <para><b>The pointer.</b> The left button raises the FRONT by one, the right button raises the BACK
/// by one, Shift (held at the press) lowers instead, Ctrl constrains and Alt draws from the center. Shift
/// is the editor's constrain key, so here it cannot be: <see cref="KeyHints.RiseConstrain"/> is Ctrl
/// alone, and every tooltip says so from that one place. Side and sign are fixed at the PRESS, like the
/// editor's right-button eraser; only Ctrl and Alt act live. The preview and the commit both read the
/// path through <c>Gesture()</c>, so what was shown is what is committed.</para>
/// </remarks>
public partial class RiseView : UserControl
{
    private Image _img = null!;
    private Image _below = null!;
    private Image _above = null!;
    private Panel _surface = null!;
    private Canvas _overlay = null!;
    private Panel _backdrop = null!;
    private DepthLabelLayer _labels = null!;
    private Border _tileHost = null!;
    private Border _bigHost = null!;
    private ModelPreviewView _model = null!;
    private Border _host = null!;
    private Border _tile = null!;
    private Border _gridChip = null!;
    private Border _targetChip = null!;
    private ScrollViewer _railScroll = null!;
    private Grid _railGrid = null!;
    private StackPanel _railBody = null!;
    private RiseViewModel? _vm;

    // The gesture in progress. Everything about it is decided at the press (side, sign, whether a Select
    // press is inside the mark) except the two live modifiers.
    private readonly List<(int x, int y)> _points = new();
    private bool _drawing;
    private bool _panning;
    private Point _panFrom;
    private KeyModifiers _mods;
    private DepthSide _side;
    private bool _lower;
    private bool _applyingSelection;

    // The geometry the backdrop was last built from; rebuilt only when one of these actually moves
    // (Bounds fires on every layout pass, and a DrawingBrush per pass is the churn to keep out).
    private (double Scale, double OffX, double OffY, int Step, bool Grid) _backdropFrom = (-1, -1, -1, -1, false);

    /// <summary>Loads the view. It constructs and lays out with no DataContext.</summary>
    public RiseView()
    {
        InitializeComponent();

        _img = this.FindControl<Image>("CanvasImage")!;
        _below = this.FindControl<Image>("BelowImage")!;
        _above = this.FindControl<Image>("AboveImage")!;
        _surface = this.FindControl<Panel>("CanvasSurface")!;
        _overlay = this.FindControl<Canvas>("CanvasOverlay")!;
        _backdrop = this.FindControl<Panel>("CanvasBackdrop")!;
        _labels = this.FindControl<DepthLabelLayer>("DepthLabels")!;
        _tileHost = this.FindControl<Border>("TileHost")!;
        _bigHost = this.FindControl<Border>("BigHost")!;
        _model = this.FindControl<ModelPreviewView>("ModelView")!;
        _host = this.FindControl<Border>("CanvasHost")!;
        _tile = this.FindControl<Border>("ModelTile")!;
        _gridChip = this.FindControl<Border>("GridChip")!;
        _targetChip = this.FindControl<Border>("TargetChip")!;
        _railScroll = this.FindControl<ScrollViewer>("RailScroll")!;
        _railGrid = this.FindControl<Grid>("RailGrid")!;
        _railBody = this.FindControl<StackPanel>("RailBody")!;

        // The page takes focus on arrival: its keys are KeyBindings, which only see a key that routes
        // through this control. The 3D view takes focus only on ITS first attach (the page wins here).
        AttachedToVisualTree += (_, _) => Focus();

        WirePointer();
        WireKeys();

        DataContextChanged += (_, _) => Rebind();
        _surface.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => Reframe()));
        // And the backdrop's own bounds: the art sits in a fixed host centered in it, so the host's
        // POSITION keeps moving as the pane resizes after the image has stopped changing size.
        _backdrop.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => { FitHost(); BuildBackdrop(); }));
        // The things the canvas must keep clear of: the tile (which changes size) and the grid chip.
        _tile.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => FitHost()));
        _gridChip.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => FitHost()));
        _targetChip.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => FitHost()));
        LayoutUpdated += (_, _) => SnapRailFold();
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

        // The dimmed layers around the target move with it: one transform, three images.
        foreach (var image in new[] { _below, _img, _above })
        {
            if (image.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] })
            {
                s.ScaleX = s.ScaleY = zoom;
                t.X = px; t.Y = py;
                continue;
            }

            image.RenderTransformOrigin = RelativePoint.Center;
            image.RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(zoom, zoom), new TranslateTransform(px, py) },
            };
        }
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

    /// <summary>The side the canvas host is drawn at when nothing is in its way.</summary>
    private const double HostSide = 320;

    /// <summary>The smallest the host shrinks to rather than sit under a corner control.</summary>
    private const double HostMinSide = 160;

    private const double HostGap = 8;

    /// <summary>
    /// Sizes and places the canvas host so the art is never UNDER the corner tile or the grid chip.
    /// </summary>
    /// <remarks>
    /// <para>Centered at 320 is right wherever it clears them, and that is every window a frame is
    /// normally looked at; at the smallest window the backdrop is only ~400 tall, and a centered host
    /// put the bottom-right of the art under the 3D tile and the bottom-left under the grid chip. So
    /// the candidates are tried in order of how little they move it - centered, centered at the top,
    /// top-left, left-centered - at 320 and then at smaller sides, and the first that clears both
    /// (with a gap) wins. "Fit" therefore means fit in the free part of the pane, which is what the
    /// pointer, the lattice and the labels follow, as they all map through the surface's real bounds.</para>
    /// <para>It reads only the backdrop, the tile and the chip, all of which are placed by alignment
    /// and so cannot be moved by what this writes: no layout loop.</para>
    /// </remarks>
    private void FitHost()
    {
        double w = _backdrop.Bounds.Width, h = _backdrop.Bounds.Height;
        if (w < 1 || h < 1) return;

        var avoid = new List<Rect>();
        if (_tile.Bounds.Width > 0) avoid.Add(_tile.Bounds.Inflate(HostGap));
        if (_gridChip.Bounds.Width > 0) avoid.Add(_gridChip.Bounds.Inflate(HostGap));
        // The layer header sits over the top of the pane on a write-through page; the art starts below it.
        double top = 8;
        if (_targetChip.IsVisible && _targetChip.Bounds.Width > 0)
        {
            avoid.Add(_targetChip.Bounds.Inflate(HostGap));
            top = _targetChip.Bounds.Bottom + HostGap;
        }

        const double edge = 8;
        for (double side = HostSide; side >= HostMinSide; side -= 8)
        {
            double cx = (w - side) / 2, cy = (h - side) / 2;
            foreach (var (x, y) in new[] { (cx, cy), (cx, top), (edge, top), (edge, cy) })
            {
                if (x < 0 || y < 0 || x + side > w || y + side > h) continue;
                var r = new Rect(x, y, side, side);
                if (avoid.Any(a => a.Intersects(r))) continue;
                PlaceHost(side, x, y);
                return;
            }
        }
        PlaceHost(HostMinSide, edge, edge);       // nothing clears: the smallest, out of the way
    }

    private void PlaceHost(double side, double x, double y)
    {
        // Margins from the top-left corner, so alignment is Left/Top whatever was chosen. Compared
        // against what is there before assigning: layout re-enters this on every pass.
        var margin = new Thickness(x, y, 0, 0);
        if (_host.Width != side) { _host.Width = side; _host.Height = side; }
        if (_host.HorizontalAlignment != Avalonia.Layout.HorizontalAlignment.Left)
            _host.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        if (_host.VerticalAlignment != Avalonia.Layout.VerticalAlignment.Top)
            _host.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        if (_host.Margin != margin) _host.Margin = margin;
    }

    /// <summary>
    /// Ends the rail's scroller between two sections, so its fold never cuts a section header in half.
    /// </summary>
    /// <remarks>
    /// <para>Sized from the one input it cannot change: the star row's own height, which the Grid
    /// decides from the window and the pinned manifest, never from what the scroller holds. The
    /// sections (the children of the visible tab) are measured with the scroller's content, which is
    /// laid out at its natural height whatever viewport it is shown in. Everything that fits is left
    /// alone (Height NaN); otherwise the viewport is cut back to the last section that fits whole.</para>
    /// <para>Written only when the answer changes, with NaN tested first.</para>
    /// </remarks>
    private void SnapRailFold()
    {
        var row = _railGrid.RowDefinitions.Count > 1 ? _railGrid.RowDefinitions[1] : null;
        if (row is null) return;
        double room = row.ActualHeight;
        if (room <= 0) return;

        var sections = _railBody.Children.OfType<Panel>().FirstOrDefault(p => p.IsVisible);
        if (sections is null) return;
        double top = _railBody.Margin.Top + sections.Bounds.Y;
        double natural = _railBody.Bounds.Height;
        double target = double.NaN;
        if (natural > room + 0.5)
        {
            double best = 0;
            foreach (var c in sections.Children.Where(c => c.IsVisible && c.Bounds.Height > 0))
            {
                double bottom = top + c.Bounds.Bottom + 4;
                if (bottom <= room + 0.5) best = bottom;
            }
            // Never smaller than the first section: a rail that shows nothing is worse than a cut one.
            target = best > 0 ? best : room;
        }

        double have = _railScroll.Height;
        bool same = double.IsNaN(have) ? double.IsNaN(target) : !double.IsNaN(target) && Math.Abs(have - target) <= 0.5;
        if (!same) _railScroll.Height = target;
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

    // ---------------------------------------------------------------- the pointer

    private void WirePointer()
    {
        _surface.PointerPressed += (_, e) =>
        {
            // The surface is still there when the 3D view has the pane; a stroke would land unseen.
            if (_vm?.ShowPaintCanvas != true) return;
            e.Pointer.Capture(_surface);
            _mods = e.KeyModifiers;

            var props = e.GetCurrentPoint(_surface).Properties;
            // PointerUpdateKind AND the flag: platforms differ about which one a press populates.
            if (props.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed || props.IsMiddleButtonPressed)
            {
                _panning = true;
                _panFrom = e.GetPosition(_surface);
                e.Handled = true;
                return;
            }

            _side = props.IsRightButtonPressed || props.PointerUpdateKind == PointerUpdateKind.RightButtonPressed
                ? DepthSide.Back : DepthSide.Front;
            _lower = _mods.HasFlag(KeyModifiers.Shift);
            _drawing = true;
            _points.Clear();
            AddPoint(e);
            _applyingSelection = _vm.ActiveTool == EditorTool.Select && _points.Count > 0
                && _vm.SelectionContains(_points[0].x, _points[0].y);
            // Budgeted 3D repaints for the length of the stroke; GestureEnded lifts it.
            _model.HostInteracting = true;
            _vm.PreviewDepthStroke(Gesture(), _side, _lower);
            DrawBand();
        };

        _surface.PointerMoved += (_, e) =>
        {
            if (_panning)
            {
                var now = e.GetPosition(_surface);
                _vm?.PanBy(now.X - _panFrom.X, now.Y - _panFrom.Y);
                // The anchor follows the pointer even where the clamp refused the move.
                _panFrom = now;
                return;
            }
            if (_drawing && _vm is not null)
            {
                _mods = e.KeyModifiers;
                AddPoint(e);
                _vm.PreviewDepthStroke(Gesture(), _side, _lower);
                DrawBand();
                return;
            }
            Hover(e);
        };

        _surface.PointerReleased += (_, e) =>
        {
            if (_panning) { _panning = false; e.Pointer.Capture(null); return; }
            if (!_drawing) return;
            _mods = e.KeyModifiers;
            // Cleared BEFORE the capture is released: releasing raises PointerCaptureLost, which would
            // otherwise read a finished gesture as an abandoned one.
            _drawing = false;
            AddPoint(e);
            if (_vm is not null && _points.Count > 0) _vm.ApplyDepthStroke(Gesture(), _side, _lower);
            _points.Clear();
            _applyingSelection = false;
            e.Pointer.Capture(null);
            DrawBand();
        };

        // Capture can be taken away - the window deactivates, another control grabs it - and no
        // release is coming; a preview left standing is indistinguishable from a committed stroke.
        _surface.PointerCaptureLost += (_, _) =>
        {
            _panning = false;
            if (_drawing) AbortGesture();
        };

        _surface.PointerExited += (_, _) =>
        {
            if (!_drawing && _vm is not null) _vm.HoverPixel = null;
        };

        _surface.PointerWheelChanged += (_, e) =>
        {
            if (_vm is null || !_vm.ShowPaintCanvas || e.Delta.Y == 0) return;
            ZoomAt(e.Delta.Y > 0 ? RiseViewModel.ZoomStep : 1 / RiseViewModel.ZoomStep, e.GetPosition(_surface));
            e.Handled = true;
        };
    }

    private void Hover(PointerEventArgs e)
    {
        if (_vm is null) return;
        if (!_vm.ShowPaintCanvas || _img.Source is not Bitmap bmp
            || !Geometry(out var scale, out var offX, out var offY))
        {
            _vm.HoverPixel = null;
            return;
        }
        var p = e.GetPosition(_surface);
        _vm.HoverPixel = CanvasMapping.PixelUnder(new CanvasFrame(scale, offX, offY), p.X, p.Y,
            bmp.PixelSize.Width, bmp.PixelSize.Height);
    }

    /// <summary>Maps the pointer (control space) to a canvas pixel and extends the path.</summary>
    private void AddPoint(PointerEventArgs e)
    {
        if (_img.Source is not Bitmap bmp || !Geometry(out var scale, out var offX, out var offY)) return;
        var p = e.GetPosition(_surface);
        var pt = CanvasMapping.PixelAt(new CanvasFrame(scale, offX, offY), p.X, p.Y,
            bmp.PixelSize.Width, bmp.PixelSize.Height);
        if (_points.Count == 0 || _points[^1] != pt) _points.Add(pt);
    }

    /// <summary>The path the tools should act on. One helper, called by the preview and by the commit,
    /// so the two cannot disagree about what was drawn. Ctrl constrains (Shift lowers here).</summary>
    private IReadOnlyList<(int x, int y)> Gesture()
    {
        if (_vm is null || _img.Source is not Bitmap bmp) return _points;
        return StrokeConstraint.Apply(_vm.ActiveTool,
            _mods.HasFlag(KeyModifiers.Control),
            _mods.HasFlag(KeyModifiers.Alt),
            _applyingSelection, _points, bmp.PixelSize.Width, bmp.PixelSize.Height);
    }

    /// <summary>Drops the gesture in progress and everything it was showing, committing nothing.</summary>
    private void AbortGesture()
    {
        _drawing = false;
        _points.Clear();
        _applyingSelection = false;
        _vm?.CancelDepthPreview();     // raises GestureEnded, which lifts the 3D budget
        DrawBand();
    }

    /// <summary>A modifier went down or came up mid-drag: re-show the gesture under the new rule.</summary>
    private void OnModifiers(KeyModifiers mods)
    {
        if (mods == _mods) return;
        _mods = mods;
        _vm?.PreviewDepthStroke(Gesture(), _side, _lower);
        DrawBand();
    }

    /// <summary>Zooms by a factor, holding the pixel under <paramref name="at"/> still.</summary>
    private void ZoomAt(double factor, Point at)
    {
        if (_vm is null || _img.Source is not Bitmap bmp) return;
        if (!Geometry(out double scale, out double offX, out double offY)) return;

        double before = _vm.Zoom;
        _vm.Zoom *= factor;
        double after = _vm.Zoom;
        if (after == before) return;

        var (panX, panY) = CanvasMapping.PanKeeping(bmp.PixelSize.Width, bmp.PixelSize.Height,
            _surface.Bounds.Width, _surface.Bounds.Height,
            new CanvasFrame(scale, offX, offY), before, after, at.X, at.Y);
        _vm.PanX = panX;
        _vm.PanY = panY;
        ClampPan();
    }

    // ---------------------------------------------------------------- the keys

    private void WireKeys()
    {
        // TUNNEL + handledEventsToo, for the editor's measured reason: the key arrives already handled.
        // Narrow, because while no drag is running only the focus-guarded shortcuts act.
        AddHandler(KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (!_drawing) { OnShortcut(e); return; }
            if (e.Key == Key.Escape)
            {
                AbortGesture();
                e.Handled = true;
                return;
            }
            OnModifiers(e.KeyModifiers);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, (object? _, KeyEventArgs e) =>
        {
            if (_drawing) OnModifiers(e.KeyModifiers);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>The canvas shortcuts: a letter per tool, brackets for brush size, Ctrl+A, zoom and pan.
    /// A bare letter is only a shortcut while no text field has focus (<see cref="FocusIsOurs"/>).</summary>
    private void OnShortcut(KeyEventArgs e)
    {
        if (_vm is null || !FocusIsOurs()) return;

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key != Key.A) return;
            _vm.SelectAllCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            switch (e.Key)
            {
                case Key.OemPlus or Key.Add: _vm.ZoomInCommand.Execute(null); e.Handled = true; return;
                case Key.OemMinus or Key.Subtract: _vm.ZoomOutCommand.Execute(null); e.Handled = true; return;
                case Key.D0 or Key.NumPad0: _vm.ZoomToFitCommand.Execute(null); e.Handled = true; return;
            }
        }
        if (e.KeyModifiers != KeyModifiers.None) return;

        // The target keys. Escape yields to an open dialog (the window closes it from there), then drops
        // the marquee, then the target: EscapeCommand does the last two in that order.
        switch (e.Key)
        {
            case Key.PageUp: _vm.StepTargetCommand.Execute(1); e.Handled = true; return;
            case Key.PageDown: _vm.StepTargetCommand.Execute(-1); e.Handled = true; return;
            case Key.Escape:
                if (TopLevel.GetTopLevel(this) is Window { DataContext: ShellViewModel { HasDialog: true } }) return;
                _vm.EscapeCommand.Execute(null);
                e.Handled = true;
                return;
        }

        // An arrow moves the VIEW, so the art travels the other way; only while there is something
        // off screen.
        if (_vm.IsZoomed)
        {
            const double step = RiseViewModel.PanStep;
            (double dx, double dy)? pan = e.Key switch
            {
                Key.Left => (step, 0),
                Key.Right => (-step, 0),
                Key.Up => (0, step),
                Key.Down => (0, -step),
                _ => null,
            };
            if (pan is { } p) { _vm.PanBy(p.dx, p.dy); e.Handled = true; return; }
        }

        EditorTool? tool = e.Key switch
        {
            Key.B => EditorTool.Brush,
            Key.L => EditorTool.Line,
            Key.R => EditorTool.Rectangle,
            Key.G => EditorTool.Fill,
            Key.M => EditorTool.Select,
            _ => null,
        };
        if (tool is { } t) { _vm.SelectToolCommand.Execute(t); e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.OemOpenBrackets: _vm.ShrinkBrushCommand.Execute(null); e.Handled = true; break;
            case Key.OemCloseBrackets: _vm.GrowBrushCommand.Execute(null); e.Handled = true; break;
        }
    }

    /// <summary>Whether the keyboard is this page's: focus is inside this view and not in a text field.</summary>
    private bool FocusIsOurs()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual v) return false;
        if (v.FindAncestorOfType<RiseView>(includeSelf: true) != this) return false;
        return v.FindAncestorOfType<TextBox>(includeSelf: true) is null;
    }

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
