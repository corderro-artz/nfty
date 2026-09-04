using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;

namespace Nfty.App.Views;

/// <summary>The ingredient editor view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
/// <remarks>
/// The pointer work lives here because it is genuinely view-shaped: it maps pointer positions onto
/// canvas pixels, and it draws the <b>rubber band</b> — the outline of the shape you are about to
/// commit, following the cursor. Without it every shape tool was drawn blind: press, drag across
/// nothing, release, and find out. A user expects to see what they are making while they make it,
/// and the cheapest honest way to show that is an overlay in control coordinates rather than
/// re-rasterising the canvas on every pointer move.
/// </remarks>
public partial class IngredientEditorView : UserControl
{
    private readonly List<(int x, int y)> _points = new();
    private bool _drawing;

    private Image _img = null!;
    private Canvas _overlay = null!;
    private IngredientEditorViewModel? _vm;

    /// <summary>Loads the view.</summary>
    public IngredientEditorView()
    {
        InitializeComponent();

        _img = this.FindControl<Image>("CanvasImage")!;
        _overlay = this.FindControl<Canvas>("CanvasOverlay")!;

        _img.PointerPressed += (_, e) =>
        {
            _drawing = true;
            _points.Clear();
            AddPoint(e);
            DrawBand();
        };
        _img.PointerMoved += (_, e) =>
        {
            if (!_drawing) return;
            AddPoint(e);
            DrawBand();
        };
        _img.PointerReleased += (_, e) =>
        {
            if (!_drawing) return;
            _drawing = false;
            AddPoint(e);
            if (DataContext is IngredientEditorViewModel vm && _points.Count > 0)
                vm.ApplyToolStroke(_points.ToArray());
            _points.Clear();
            DrawBand();          // clears the band and repaints the marquee in its new place
        };

        // The marquee has to survive the gesture that made it, so it is redrawn whenever the
        // selection changes and whenever the canvas is re-laid-out under it.
        DataContextChanged += (_, _) => Rebind();
        _img.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => DrawBand()));
        Rebind();
    }

    private void Rebind()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as IngredientEditorViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        DrawBand();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IngredientEditorViewModel.Selection)
            or nameof(IngredientEditorViewModel.ActiveTool)
            or nameof(IngredientEditorViewModel.Canvas))
            DrawBand();
    }

    /// <summary>
    /// Repaints the overlay: the live rubber band for the gesture in progress, plus the standing
    /// marquee if a region is marked. Both are outlines only — they show where something WILL be,
    /// and drawing them filled would hide the art they are being positioned against.
    /// </summary>
    private void DrawBand()
    {
        _overlay.Children.Clear();
        if (_vm is null || !Geometry(out var scale, out var offX, out var offY)) return;

        Point ToControl(int px, int py) => new(offX + px * scale, offY + py * scale);

        // The standing marquee, whether or not a gesture is in progress.
        if (_vm.Selection is { } sel)
        {
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

        if (!_drawing || _points.Count == 0) return;

        var a = _points[0];
        var b = _points[^1];
        // +1 on the far edge: a band from pixel 3 to pixel 5 covers three whole pixels, and an
        // outline drawn to the near edge of pixel 5 would sit a pixel short of what commits.
        var p0 = ToControl(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        var p1 = ToControl(Math.Max(a.x, b.x) + 1, Math.Max(a.y, b.y) + 1);
        double w = Math.Max(1, p1.X - p0.X), h = Math.Max(1, p1.Y - p0.Y);

        Shape? band = _vm.ActiveTool switch
        {
            EditorTool.Rectangle => new Rectangle { Width = w, Height = h },
            EditorTool.Circle => new Ellipse { Width = w, Height = h },
            EditorTool.Triangle => new Polygon
            {
                Points = new List<Point> { new(w / 2, 0), new(w, h), new(0, h) },
                Width = w,
                Height = h,
            },
            EditorTool.Select => new Rectangle
            {
                Width = w,
                Height = h,
                StrokeDashArray = new AvaloniaList<double>(4, 3),
            },
            EditorTool.Line => null,   // built below: a line is two points, not a box
            _ => null,                 // brush, eraser and fill show their result directly
        };

        if (_vm.ActiveTool == EditorTool.Line)
        {
            // Through pixel CENTRES, because that is where a stamped disc lands.
            var la = ToControl(a.x, a.y);
            var lb = ToControl(b.x, b.y);
            double half = scale / 2;
            band = new Line
            {
                StartPoint = new Point(la.X + half, la.Y + half),
                EndPoint = new Point(lb.X + half, lb.Y + half),
            };
            Canvas.SetLeft(band, 0);
            Canvas.SetTop(band, 0);
        }
        else if (band is not null)
        {
            Canvas.SetLeft(band, p0.X);
            Canvas.SetTop(band, p0.Y);
        }

        if (band is null) return;
        band.Stroke = Brush("AccentBrush");
        band.StrokeThickness = 1;
        band.IsHitTestVisible = false;
        _overlay.Children.Add(band);
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;

    /// <summary>The mapping between canvas pixels and control coordinates, honouring
    /// <c>Stretch="Uniform"</c>. False while the image has no size to map against.</summary>
    private bool Geometry(out double scale, out double offX, out double offY)
    {
        scale = offX = offY = 0;
        if (_img.Source is not Bitmap bmp) return false;
        double imgW = bmp.PixelSize.Width, imgH = bmp.PixelSize.Height;
        double cw = _img.Bounds.Width, ch = _img.Bounds.Height;
        if (imgW <= 0 || imgH <= 0 || cw <= 0 || ch <= 0) return false;
        scale = Math.Min(cw / imgW, ch / imgH);
        offX = (cw - imgW * scale) / 2;
        offY = (ch - imgH * scale) / 2;
        return true;
    }

    // Map the pointer position (control space) to canvas pixel coords.
    private void AddPoint(PointerEventArgs e)
    {
        if (_img.Source is not Bitmap bmp || !Geometry(out var scale, out var offX, out var offY)) return;
        var p = e.GetPosition(_img);
        int px = Math.Clamp((int)((p.X - offX) / scale), 0, bmp.PixelSize.Width - 1);
        int py = Math.Clamp((int)((p.Y - offY) / scale), 0, bmp.PixelSize.Height - 1);
        var pt = (px, py);
        if (_points.Count == 0 || _points[^1] != pt) _points.Add(pt);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Minimal <see cref="IObserver{T}"/> for an Avalonia property subscription — the
    /// framework hands out <c>IObservable</c> and the BCL has no lambda overload for it.</summary>
    private sealed class Sub<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}
