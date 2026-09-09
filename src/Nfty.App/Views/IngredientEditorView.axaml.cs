using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
// Both Avalonia and the engine have a PixelRect; the overlay speaks the engine's.
using PixelRect = Nfty.Core.Editing.PixelRect;

namespace Nfty.App.Views;

/// <summary>The ingredient editor view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
/// <remarks>
/// The pointer work lives here because it is genuinely view-shaped: it maps pointer positions onto
/// canvas pixels, and it draws the <b>selection marquee</b> in control coordinates over the art.
/// <para>What it no longer draws is the shape tools' rubber band. That band existed because a
/// gesture was otherwise drawn blind — press, drag across nothing, release, find out — and an
/// accent outline was the cheap way to say something was happening. The honest way is to show the
/// pixels, which is what <see cref="IngredientEditorViewModel.PreviewToolStroke"/> does on every
/// pointer move; keeping the outline as well would put a second, disagreeing account of the same
/// gesture on top of the first. Select keeps a box because marking changes no pixel, so there is
/// nothing for a pixel preview to show.</para>
/// </remarks>
public partial class IngredientEditorView : UserControl
{
    private readonly List<(int x, int y)> _points = new();
    private bool _drawing;

    // Set at PRESS: a Select drag that begins inside the marquee is a MOVE, and the overlay has to
    // say so from the first pixel. The ViewModel only resolves mark-vs-move on release, which is
    // too late to draw with.
    private bool _movingSelection;

    // The modifiers as of the last input event, pointer OR key. Held here rather than read from the
    // pointer args at each call site because a modifier pressed mid-drag has to take effect without
    // the hand moving - the shape snaps square while the pointer is still.
    private KeyModifiers _mods;

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
            // CAPTURE, or a gesture that wanders off the 320px tile is simply abandoned: moves stop
            // arriving, the release lands on whatever is under the pointer instead, and the stroke
            // is silently lost with its preview still on screen. AddPoint already clamps to the
            // canvas, so a drag past the edge ends at the edge, which is what it looks like.
            e.Pointer.Capture(_img);
            _mods = e.KeyModifiers;
            _drawing = true;
            _points.Clear();
            AddPoint(e);
            _movingSelection = _vm is not null
                && _vm.ActiveTool == EditorTool.Select
                && _points.Count > 0
                && _vm.SelectionContains(_points[0].x, _points[0].y);
            _vm?.PreviewToolStroke(Gesture());
            DrawBand();
        };
        _img.PointerMoved += (_, e) =>
        {
            if (!_drawing) return;
            _mods = e.KeyModifiers;
            AddPoint(e);
            _vm?.PreviewToolStroke(Gesture());
            DrawBand();
        };
        _img.PointerReleased += (_, e) =>
        {
            if (!_drawing) return;
            _mods = e.KeyModifiers;
            _drawing = false;
            AddPoint(e);
            if (DataContext is IngredientEditorViewModel vm && _points.Count > 0)
                vm.ApplyToolStroke(Gesture().ToArray());
            _points.Clear();
            _movingSelection = false;
            e.Pointer.Capture(null);
            DrawBand();          // clears the band and repaints the marquee in its new place
        };

        // Capture can be taken away — the window deactivates, another control grabs it — and then no
        // release is coming. The gesture is abandoned, so the canvas has to stop showing a stroke
        // that is never going to commit.
        _img.PointerCaptureLost += (_, _) => { if (_drawing) AbortGesture(); };

        // TUNNEL, so both of these run before the UserControl's own Escape KeyBinding: mid-gesture
        // Escape means "abandon this stroke", and only once there is no stroke does it mean "drop
        // the marquee". Two meanings on one key, told apart by whether a drag is in progress, is
        // what every editor does with it.
        //
        // handledEventsToo, because the key arrives ALREADY HANDLED - measured, not assumed: a
        // tunnel handler without it never ran once. That is also why the gesture keys live here and
        // not in a KeyBinding beside the others, and it is the same trap the recipe rows' Enter
        // handler documents. Taking handled events is narrow rather than blanket: both handlers do
        // nothing at all unless a drag is in progress, so a child that has already dealt with a key
        // keeps it in every other state.
        AddHandler(KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (!_drawing) return;
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

        // The page takes focus on arrival, because every key this screen owns - Ctrl+Z, Ctrl+Y,
        // Escape - is a KeyBinding on this UserControl, and a KeyBinding only sees a key event that
        // routes through it. With focus nowhere the event is raised on the Window and the whole set
        // is silently inert. IsTabStop is off, so it takes focus without joining the tab order; the
        // first Tab still lands on the first real control.
        AttachedToVisualTree += (_, _) => Focus();

        // The marquee has to survive the gesture that made it, so it is redrawn whenever the
        // selection changes and whenever the canvas is re-laid-out under it.
        DataContextChanged += (_, _) => Rebind();
        _img.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => DrawBand()));
        Rebind();
    }

    /// <summary>The gesture the tools should act on: the raw path, or what the held modifiers make
    /// of it. One helper, called by the preview and by the commit, so the two cannot disagree about
    /// what was drawn.</summary>
    private IReadOnlyList<(int x, int y)> Gesture()
    {
        if (_vm is null || _img.Source is not Bitmap bmp) return _points;
        return StrokeConstraint.Apply(
            _vm.ActiveTool,
            _mods.HasFlag(KeyModifiers.Shift) || _mods.HasFlag(KeyModifiers.Control),
            _mods.HasFlag(KeyModifiers.Alt),
            _movingSelection,
            _points,
            bmp.PixelSize.Width,
            bmp.PixelSize.Height);
    }

    /// <summary>A modifier went down or came up mid-drag: re-show the gesture under the new rule
    /// without waiting for the hand to move.</summary>
    private void OnModifiers(KeyModifiers mods)
    {
        if (mods == _mods) return;
        _mods = mods;
        _vm?.PreviewToolStroke(Gesture());
        DrawBand();
    }

    /// <summary>Drops the gesture in progress and everything it was showing, committing nothing.</summary>
    private void AbortGesture()
    {
        _drawing = false;
        _points.Clear();
        _movingSelection = false;
        _vm?.CancelToolPreview();
        DrawBand();
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

        // A move in progress drags the marquee itself: the region shows where it is GOING, which is
        // the whole affordance. Drawing a fresh mark box here instead — what this did before — told
        // the user the opposite of what release was about to do.
        // Read through Gesture(), never off _points: with a modifier held the marquee has to be
        // dragged along the axis the commit will use, or the ghost promises one landing place and
        // the release picks another.
        var path = _drawing ? Gesture() : _points;
        bool moving = _drawing && _movingSelection && path.Count > 0;
        int mdx = moving ? path[^1].x - path[0].x : 0;
        int mdy = moving ? path[^1].y - path[0].y : 0;

        // The standing marquee, whether or not a gesture is in progress.
        if (_vm.Selection is { } sel0)
        {
            var sel = new PixelRect(sel0.X + mdx, sel0.Y + mdy, sel0.Width, sel0.Height);
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

        if (!_drawing || path.Count == 0 || moving) return;

        var a = path[0];
        var b = path[^1];
        // +1 on the far edge: a band from pixel 3 to pixel 5 covers three whole pixels, and an
        // outline drawn to the near edge of pixel 5 would sit a pixel short of what commits.
        var p0 = ToControl(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        var p1 = ToControl(Math.Max(a.x, b.x) + 1, Math.Max(a.y, b.y) + 1);
        double w = Math.Max(1, p1.X - p0.X), h = Math.Max(1, p1.Y - p0.Y);

        // ONLY Select draws a band. Every tool that paints shows the pixels themselves now
        // (IngredientEditorViewModel.PreviewToolStroke), and an accent outline over them would be a
        // second, disagreeing account of the same gesture: the shape tools FILL, a line commits at
        // the brush's size in the brush's ink, and a 1px hairline said otherwise on both counts.
        // Select is the exception because marking changes no pixel — there is nothing for a preview
        // to show, so the box is the whole feedback.
        if (_vm.ActiveTool != EditorTool.Select) return;

        var band = new Rectangle
        {
            Width = w,
            Height = h,
            StrokeDashArray = new AvaloniaList<double>(4, 3),
            Stroke = Brush("AccentBrush"),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(band, p0.X);
        Canvas.SetTop(band, p0.Y);
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
