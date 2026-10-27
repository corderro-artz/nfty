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
using Avalonia.Reactive;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
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

    // Set at PRESS from the button that started the gesture: the right button erases for the length
    // of that one stroke without disturbing what the toolstrip has armed. Setting ActiveTool instead
    // would flip the toolbar under the hand and, off Select, silently drop the marquee.
    private bool _erasing;

    // The modifiers as of the last input event, pointer OR key. Held here rather than read from the
    // pointer args at each call site because a modifier pressed mid-drag has to take effect without
    // the hand moving - the shape snaps square while the pointer is still.
    private KeyModifiers _mods;

    // A middle-button drag is a PAN, not a stroke. It is decided at press and takes the gesture
    // outright: panning while painting would be two answers to one drag, and the middle button is
    // the one this editor had not already spent (the right one erases).
    private bool _panning;
    private Point _panFrom;

    private Image _img = null!;
    private Panel _surface = null!;
    private Canvas _overlay = null!;
    private Panel _backdrop = null!;
    private IngredientEditorViewModel? _vm;

    // The zoom, the transform, the pan clamp and the lattice: shared with the Rise page.
    private PixelCanvasHost _canvas = null!;

    /// <summary>Loads the view.</summary>
    public IngredientEditorView()
    {
        InitializeComponent();

        _img = this.FindControl<Image>("CanvasImage")!;
        _surface = this.FindControl<Panel>("CanvasSurface")!;
        _overlay = this.FindControl<Canvas>("CanvasOverlay")!;
        _backdrop = this.FindControl<Panel>("CanvasBackdrop")!;
        _canvas = new PixelCanvasHost(_img, _surface, _backdrop, () => _vm, Brush);

        _surface.PointerPressed += (_, e) =>
        {
            // CAPTURE, or a gesture that wanders off the 320px tile is simply abandoned: moves stop
            // arriving, the release lands on whatever is under the pointer instead, and the stroke
            // is silently lost with its preview still on screen. AddPoint already clamps to the
            // canvas, so a drag past the edge ends at the edge, which is what it looks like.
            // THE SURFACE IS STILL THERE WHEN THE CANVAS IS NOT. "Fill pane with preview" hides the
            // art and puts the colorized result over it, and the panel the pointer talks to has no
            // opinion about that — so without this a stroke would be painted into a canvas nobody
            // can see. It was the image's own visibility that used to answer this, back when the
            // image was what the pointer talked to.
            if (_vm?.ShowPaintCanvas != true) return;

            e.Pointer.Capture(_surface);
            _mods = e.KeyModifiers;

            // THE MIDDLE BUTTON PANS, and it is settled here rather than on the move for the same
            // reason mark-versus-move is: a gesture has to know what it is from its first pixel.
            var pressed = e.GetCurrentPoint(_surface).Properties;
            // PointerUpdateKind, not IsMiddleButtonPressed: the flag reads a MODIFIER bit that says
            // the button is being held, and on a press the only thing that is certainly true is
            // which button caused this event. Both are checked because platforms differ about which
            // of the two they populate on the down.
            if (pressed.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed
                || pressed.IsMiddleButtonPressed)
            {
                _panning = true;
                _panFrom = e.GetPosition(_surface);
                e.Handled = true;
                return;
            }

            // THE RIGHT BUTTON ERASES, whatever the toolstrip has armed. Pixel editors hand the
            // second button the second ink; this app has no second color, so the second ink is
            // "none" — which is also the correction a hand makes most often and the one that
            // otherwise costs a trip to the toolstrip and back.
            _erasing = e.GetCurrentPoint(_surface).Properties.IsRightButtonPressed;
            _drawing = true;
            _points.Clear();
            AddPoint(e);
            _movingSelection = !_erasing
                && _vm is not null
                && _vm.ActiveTool == EditorTool.Select
                && _points.Count > 0
                && _vm.SelectionContains(_points[0].x, _points[0].y);
            _vm?.PreviewToolStroke(Gesture(), _gestureTool);
            DrawBand();
        };
        _surface.PointerMoved += (_, e) =>
        {
            if (_panning)
            {
                var now = e.GetPosition(_surface);
                _vm?.PanBy(now.X - _panFrom.X, now.Y - _panFrom.Y);
                // The anchor follows the pointer even where the clamp refused the move, so a drag
                // that runs into the edge and comes back does not have to make up the lost distance.
                _panFrom = now;
                return;
            }
            if (!_drawing) return;
            _mods = e.KeyModifiers;
            AddPoint(e);
            _vm?.PreviewToolStroke(Gesture(), _gestureTool);
            DrawBand();
        };
        _surface.PointerReleased += (_, e) =>
        {
            if (_panning) { _panning = false; e.Pointer.Capture(null); return; }
            if (!_drawing) return;
            _mods = e.KeyModifiers;
            _drawing = false;
            AddPoint(e);
            if (DataContext is IngredientEditorViewModel vm && _points.Count > 0)
                vm.ApplyToolStroke(Gesture().ToArray(), _gestureTool);
            _points.Clear();
            _movingSelection = false;
            _erasing = false;
            e.Pointer.Capture(null);
            DrawBand();          // clears the band and repaints the marquee in its new place
        };

        // Capture can be taken away — the window deactivates, another control grabs it — and then no
        // release is coming. The gesture is abandoned, so the canvas has to stop showing a stroke
        // that is never going to commit.
        _surface.PointerCaptureLost += (_, _) =>
        {
            _panning = false;
            if (_drawing) AbortGesture();
        };

        // THE WHEEL ZOOMS, anchored on the pointer. Bare, with no modifier: the canvas is not in a
        // scroller, so there is nothing else for a wheel over it to mean, and a magnifier that
        // needs a chord is one nobody finds.
        _surface.PointerWheelChanged += (_, e) =>
        {
            if (_vm is null || !_vm.ShowPaintCanvas || e.Delta.Y == 0) return;
            _canvas.ZoomAt(e.GetPosition(_surface),
                e.Delta.Y > 0 ? IngredientEditorViewModel.ZoomStep : 1 / IngredientEditorViewModel.ZoomStep);
            e.Handled = true;
        };

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

        // The page takes focus on arrival, because every key this screen owns - Ctrl+Z, Ctrl+Y,
        // Escape - is a KeyBinding on this UserControl, and a KeyBinding only sees a key event that
        // routes through it. With focus nowhere the event is raised on the Window and the whole set
        // is silently inert. IsTabStop is off, so it takes focus without joining the tab order; the
        // first Tab still lands on the first real control.
        AttachedToVisualTree += (_, _) => Focus();

        // The marquee has to survive the gesture that made it, so it is redrawn whenever the
        // selection changes and whenever the canvas is re-laid-out under it.
        DataContextChanged += (_, _) => Rebind();
        _surface.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(_ =>
        {
            DrawBand();
            _canvas.RebuildBackdrop();
        }));
        // AND the panel's own bounds. The art sits in a fixed 320px host centered in this panel, so
        // the IMAGE's bounds stop changing once the host is measured while the host's POSITION keeps
        // moving as the pane resizes - and the lattice is anchored on that position. Keyed on the
        // image alone, the backdrop was built once from wherever the host happened to be mid-layout
        // and then never corrected.
        _backdrop.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(_ => _canvas.RebuildBackdrop()));
        Rebind();
    }

    /// <summary>The gesture the tools should act on: the raw path, or what the held modifiers make
    /// of it. One helper, called by the preview and by the commit, so the two cannot disagree about
    /// what was drawn.</summary>
    /// <summary>The tool this gesture is using: the armed one, or the eraser while the right button
    /// is down. Null means "whatever is armed", which is what the ViewModel's own default says.</summary>
    private EditorTool? _gestureTool => _erasing ? EditorTool.Eraser : null;

    private IReadOnlyList<(int x, int y)> Gesture()
    {
        if (_vm is null || _img.Source is not Bitmap bmp) return _points;
        return StrokeConstraint.Apply(
            _gestureTool ?? _vm.ActiveTool,
            _mods.HasFlag(KeyModifiers.Shift) || _mods.HasFlag(KeyModifiers.Control),
            _mods.HasFlag(KeyModifiers.Alt),
            _movingSelection,
            _points,
            bmp.PixelSize.Width,
            bmp.PixelSize.Height);
    }

    /// <summary>
    /// The canvas shortcuts: a letter per tool, the bracket keys for brush size, Ctrl+A to mark
    /// everything and Delete to clear what is marked.
    /// </summary>
    /// <remarks>
    /// <para><b>A bare letter is only a shortcut while no text field has focus.</b> This screen
    /// carries a name box and a weight box, and a user typing "Brush" into the first would otherwise
    /// arm five tools on the way through. Deleting the guard fails
    /// <c>EditorShortcutTests.A_letter_typed_into_a_field_is_text_and_not_a_shortcut</c>, which is
    /// what makes single letters admissible at all. The ancestor walk is belt and braces and is
    /// NOT what the test proves: focus lands on the TextBox itself, inside a
    /// <c>NumericUpDown</c>'s template included, so <c>is TextBox</c> passes the test too. It stays
    /// because a template is free to put a focusable child inside its box, and this reads the same
    /// either way.</para>
    /// <para>They live here rather than in a <c>KeyBinding</c> for the reason the gesture keys do:
    /// the handler runs on the tunnel and takes handled events, so it sees keys a child has already
    /// marked. That is only safe BECAUSE of the focus guard above; without it this would be a view
    /// swallowing every letter in the app.</para>
    /// </remarks>
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
        // ZOOM AND PAN, which is the keyboard path the pointer gestures are required to ship with —
        // and the only path at all on a trackpad with no middle button. Shift is accepted alongside
        // None because "+" is Shift and "=" on most layouts, and refusing the key someone actually
        // pressed teaches nothing.
        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            switch (e.Key)
            {
                case Key.OemPlus or Key.Add:
                    _vm.ZoomInCommand.Execute(null); e.Handled = true; return;
                case Key.OemMinus or Key.Subtract:
                    _vm.ZoomOutCommand.Execute(null); e.Handled = true; return;
                case Key.D0 or Key.NumPad0:
                    _vm.ZoomToFitCommand.Execute(null); e.Handled = true; return;
            }
        }
        if (e.KeyModifiers != KeyModifiers.None) return;

        // An arrow moves the VIEW, so the art travels the other way — the direction every scroller
        // has taught. Only while there is something off screen: fitted, the canvas has nowhere to go
        // and the key belongs to whatever else wants it.
        if (_vm.IsZoomed)
        {
            const double step = IngredientEditorViewModel.PanStep;
            (double dx, double dy)? pan = e.Key switch
            {
                Key.Left => (step, 0),
                Key.Right => (-step, 0),
                Key.Up => (0, step),
                Key.Down => (0, -step),
                _ => null,
            };
            if (pan is { } p)
            {
                _vm.PanBy(p.dx, p.dy);
                e.Handled = true;
                return;
            }
        }

        EditorTool? tool = e.Key switch
        {
            Key.B => EditorTool.Brush,
            Key.E => EditorTool.Eraser,
            Key.G => EditorTool.Fill,        // Photoshop's bucket key
            Key.R => EditorTool.Rectangle,
            Key.C => EditorTool.Circle,
            Key.T => EditorTool.Triangle,
            Key.L => EditorTool.Line,
            Key.M => EditorTool.Select,      // marquee, which is what the tool draws
            _ => null,
        };
        if (tool is { } t)
        {
            _vm.SelectToolCommand.Execute(t);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.OemOpenBrackets: _vm.ShrinkBrushCommand.Execute(null); e.Handled = true; break;
            case Key.OemCloseBrackets: _vm.GrowBrushCommand.Execute(null); e.Handled = true; break;
            // Backspace as well as Delete: a laptop keyboard often hides Delete behind a function
            // key, and both mean "remove this" to the hand that reaches for them.
            case Key.Delete:
            case Key.Back:
                if (_vm.DeleteSelectionCommand.CanExecute(null))
                {
                    _vm.DeleteSelectionCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }

    /// <summary>
    /// Whether the keyboard is this page's to interpret: focus is inside this view, and not in a
    /// text field within it.
    /// </summary>
    /// <remarks>
    /// The second half is what the tests pin. The first half only matters for a modal whose content
    /// takes focus, and it does NOT close that case in general: the dialog layer hosts its content
    /// in a ContentControl that never focuses itself (which is why Escape is bound on the window),
    /// so a dialog of nothing but buttons can leave focus on the page behind it and these keys live.
    /// Worth knowing rather than worth machinery — the worst of it is arming a tool you cannot see
    /// and one undoable clear.
    /// </remarks>
    private bool FocusIsOurs()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual v) return false;
        if (v.FindAncestorOfType<IngredientEditorView>(includeSelf: true) != this) return false;
        return v.FindAncestorOfType<TextBox>(includeSelf: true) is null;
    }

    /// <summary>A modifier went down or came up mid-drag: re-show the gesture under the new rule
    /// without waiting for the hand to move.</summary>
    private void OnModifiers(KeyModifiers mods)
    {
        if (mods == _mods) return;
        _mods = mods;
        _vm?.PreviewToolStroke(Gesture(), _gestureTool);
        DrawBand();
    }

    /// <summary>Drops the gesture in progress and everything it was showing, committing nothing.</summary>
    private void AbortGesture()
    {
        _drawing = false;
        _points.Clear();
        _movingSelection = false;
        _erasing = false;
        _vm?.CancelToolPreview();
        DrawBand();
    }

    private void Rebind()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmChanged;
            _vm.BackdropChanged -= OnBackdropChanged;
        }
        _vm = DataContext as IngredientEditorViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmChanged;
            _vm.BackdropChanged += OnBackdropChanged;
        }
        // The transform is applied HERE as well as on the event: nothing has changed yet when a view
        // is first bound, so an editor that only ever answered the change notification would open
        // with no transform at all and the first zoom would be the one that installed it.
        _canvas.ApplyCanvasTransform();
        DrawBand();
        _canvas.RebuildBackdrop();
    }

    /// <summary>The canvas has been re-laid-out: re-apply the transform, rebuild the lattice under
    /// it and redraw the marquee over it. All three follow from the same geometry and are never
    /// wanted apart, which is why one event says so.</summary>
    private void OnBackdropChanged()
    {
        _canvas.ApplyCanvasTransform();
        _canvas.ClampPan();
        _canvas.RebuildBackdrop();
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
        if (_vm is null || !_canvas.TryGeometry(out var scale, out var offX, out var offY)) return;

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
        if ((_gestureTool ?? _vm.ActiveTool) != EditorTool.Select) return;

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

    // Map the pointer position (control space) to canvas pixel coords.
    private void AddPoint(PointerEventArgs e)
    {
        if (_img.Source is not Bitmap bmp || !_canvas.TryGeometry(out var scale, out var offX, out var offY)) return;
        var p = e.GetPosition(_surface);
        var pt = CanvasMapping.PixelAt(new CanvasFrame(scale, offX, offY), p.X, p.Y,
            bmp.PixelSize.Width, bmp.PixelSize.Height);
        if (_points.Count == 0 || _points[^1] != pt) _points.Add(pt);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

}
