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

    // The geometry the backdrop was last built from. A brush is rebuilt only when one of these
    // actually moves: BuildBackdrop runs off Bounds changes, which fire on every layout pass, and
    // allocating a DrawingBrush per pass is the churn the perf work exists to prevent.
    private (double Scale, double OffX, double OffY, int Step, bool Grid) _backdropFrom = (-1, -1, -1, -1, false);

    /// <summary>Loads the view.</summary>
    public IngredientEditorView()
    {
        InitializeComponent();

        _img = this.FindControl<Image>("CanvasImage")!;
        _surface = this.FindControl<Panel>("CanvasSurface")!;
        _overlay = this.FindControl<Canvas>("CanvasOverlay")!;
        _backdrop = this.FindControl<Panel>("CanvasBackdrop")!;

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
            ZoomAt(e.Delta.Y > 0 ? IngredientEditorViewModel.ZoomStep
                                 : 1 / IngredientEditorViewModel.ZoomStep,
                   e.GetPosition(_surface));
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
        _surface.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ =>
        {
            DrawBand();
            BuildBackdrop();
        }));
        // AND the panel's own bounds. The art sits in a fixed 320px host centered in this panel, so
        // the IMAGE's bounds stop changing once the host is measured while the host's POSITION keeps
        // moving as the pane resizes - and the lattice is anchored on that position. Keyed on the
        // image alone, the backdrop was built once from wherever the host happened to be mid-layout
        // and then never corrected.
        _backdrop.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => BuildBackdrop()));
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
        ApplyCanvasTransform();
        DrawBand();
        BuildBackdrop();
    }

    /// <summary>The canvas has been re-laid-out: re-apply the transform, rebuild the lattice under
    /// it and redraw the marquee over it. All three follow from the same geometry and are never
    /// wanted apart, which is why one event says so.</summary>
    private void OnBackdropChanged()
    {
        ApplyCanvasTransform();
        ClampPan();
        BuildBackdrop();
        DrawBand();
    }

    /// <summary>
    /// Paints the canvas backdrop as a lattice of the ART'S OWN PIXELS, or as a flat ground.
    /// </summary>
    /// <remarks>
    /// <para><b>Both the square size and the PHASE come from the laid-out art.</b> A square is
    /// <c>GridSize</c> canvas pixels wide times the scale the image was arranged at, and the tile
    /// starts at the art's top-left corner rather than the panel's — which is the whole fix. The
    /// backdrop was a fixed 18px checker tiled from the panel's corner, so its squares stood in no
    /// relation to the pixels drawn on top of them: a pixel covered part of one square and part of
    /// the next, and the amount changed with the canvas size. It is the same
    /// <see cref="Geometry"/> the pointer and the marquee are mapped through, so the lattice and the
    /// pixel a click lands on cannot disagree.</para>
    ///
    /// <para><b>Under two device pixels a square is not a square.</b> Half a lattice that fine
    /// averages to a flat tone and the other half aliases into a moire that crawls as the window
    /// moves - so below that floor the flat ground is drawn instead, which is what the lattice would
    /// have looked like anyway. The grid step is the control for it, and its tooltip says so.</para>
    ///
    /// <para>Rebuilt only when something it depends on MOVES. It is driven off <c>Bounds</c>, which
    /// changes on every layout pass, and a new <c>DrawingBrush</c> per pass is exactly the per-frame
    /// churn <c>SetBrowserPerfTests</c> exists to keep out of this app.</para>
    /// </remarks>
    private void BuildBackdrop()
    {
        if (_backdrop is null) return;

        int step = _vm?.GridSize ?? 1;
        bool grid = _vm?.ShowPixelGrid ?? false;

        if (!grid || !Geometry(out var scale, out var offX, out var offY)) { Flatten(step); return; }

        double cell = step * scale;

        // UNDER TWO DEVICE PIXELS A SQUARE IS NOT A SQUARE. Half a lattice that fine averages to a
        // flat tone and the other half aliases into a moire that crawls as the window moves - and
        // what it averages TO is the flat ground, so drawing that is the honest version of the same
        // picture. The step control is what brings the lattice back on a large canvas.
        if (cell < 2) { Flatten(step); return; }

        // The art's corner in the BACKDROP's coordinates. Translated from the SURFACE rather than
        // from the image: the image carries the zoom as a render transform, and TranslatePoint
        // applies one — so going through it would multiply the zoom in twice and put the lattice
        // somewhere neither the art nor the pointer is.
        if (_surface.TranslatePoint(new Point(offX, offY), _backdrop) is not { } art) return;

        var next = (scale, art.X, art.Y, step, true);
        if (_backdropFrom == next && _backdrop.Background is DrawingBrush) return;

        var light = Brush("BgAltBrush");
        var dark = Brush("BgAlt2Brush");
        // Before the control is attached there is no theme to read a brush out of, and caching that
        // failure would leave the backdrop unpainted for the life of the editor.
        if (light is null || dark is null) return;
        _backdropFrom = next;

        double tile = cell * 2;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing
        {
            Brush = light,
            Geometry = new RectangleGeometry(new Rect(0, 0, tile, tile)),
        });
        group.Children.Add(new GeometryDrawing
        {
            Brush = dark,
            Geometry = new RectangleGeometry(new Rect(0, 0, cell, cell)),
        });
        group.Children.Add(new GeometryDrawing
        {
            Brush = dark,
            Geometry = new RectangleGeometry(new Rect(cell, cell, cell, cell)),
        });

        // THE PHASE IS THE FIX, not the size. The lattice is anchored on the ART's corner rather
        // than on this panel's, so a square boundary falls exactly on a pixel boundary; a brush of
        // the right size tiled from the wrong origin is the same bug with better arithmetic. The
        // modulo keeps the start on the panel (a negative DestinationRect is not honoured) while
        // leaving the corner on a tile boundary, which is all "in phase" means.
        double startX = Mod(art.X, tile);
        double startY = Mod(art.Y, tile);

        _backdrop.Background = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            SourceRect = new RelativeRect(0, 0, tile, tile, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(startX, startY, tile, tile, RelativeUnit.Absolute),
            Stretch = Stretch.None,
        };
    }

    /// <summary>The flat ground: the app's own page color, which is what the lattice averages to
    /// anyway at the sizes it is refused at.</summary>
    /// <param name="step">The step in force, so a later pass can tell the state apart.</param>
    private void Flatten(int step)
    {
        var flat = Brush("BgAltBrush");
        if (flat is null) return;                       // no theme yet; try again after attach
        if (ReferenceEquals(_backdrop.Background, flat)) return;
        _backdropFrom = (0, 0, 0, step, false);
        _backdrop.Background = flat;
    }

    /// <summary>A modulo that returns a non-negative remainder — C#'s <c>%</c> keeps the dividend's
    /// sign, and the art can sit at a negative offset in a pane narrower than the tile.</summary>
    private static double Mod(double value, double by)
    {
        double r = value % by;
        return r < 0 ? r + by : r;
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

    /// <summary>
    /// The mapping between canvas pixels and control coordinates: the letterbox a
    /// <c>Stretch="Uniform"</c> image leaves inside its box, times the zoom, plus the pan.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE ONLY PLACE THE ZOOM EXISTS.</b> The pointer mapping, the marquee overlay and
    /// the backdrop lattice are all mapped through this one function, so putting the magnification
    /// here means the pixel a click lands on, the pixel the marquee is drawn around and the square
    /// the lattice paints all move together by construction. The drawn art follows because
    /// <see cref="ApplyCanvasTransform"/> is derived from exactly these two lines: scaling about the
    /// CENTER is what makes a centered letterbox stay centered, so the render transform is a plain
    /// scale-and-translate and needs no second copy of the arithmetic.
    /// </remarks>
    /// <param name="scale">Control pixels per canvas pixel.</param>
    /// <param name="offX">Where the art's left edge sits, in the image control's own coordinates.</param>
    /// <param name="offY">Where its top edge sits.</param>
    /// <returns>False while the image has no size to map against.</returns>
    private bool Geometry(out double scale, out double offX, out double offY)
    {
        scale = offX = offY = 0;
        if (_img.Source is not Bitmap bmp) return false;
        double imgW = bmp.PixelSize.Width, imgH = bmp.PixelSize.Height;
        double cw = _surface.Bounds.Width, ch = _surface.Bounds.Height;
        if (imgW <= 0 || imgH <= 0 || cw <= 0 || ch <= 0) return false;
        double zoom = _vm?.Zoom ?? 1;
        scale = Math.Min(cw / imgW, ch / imgH) * zoom;
        offX = (cw - imgW * scale) / 2 + (_vm?.PanX ?? 0);
        offY = (ch - imgH * scale) / 2 + (_vm?.PanY ?? 0);
        return true;
    }

    /// <summary>
    /// Draws the art at the zoom and pan <see cref="Geometry"/> reports.
    /// </summary>
    /// <remarks>
    /// <para>Origin CENTER, which is what makes this one transform rather than two. Scaling a
    /// centered letterbox about its own center leaves it centered, so the drawn art lands exactly
    /// where the letterbox arithmetic above puts it and the pan is a plain translate on top.</para>
    ///
    /// <para>The overlay is deliberately NOT transformed. It draws the marquee in control
    /// coordinates through <see cref="Geometry"/>, so it already follows; scaling it as well would
    /// multiply the dashed stroke's own width by the zoom and turn a hairline into a ribbon.</para>
    /// </remarks>
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

    /// <summary>
    /// Keeps the art overlapping the tile it is drawn in.
    /// </summary>
    /// <remarks>
    /// One rule covers both directions and falls out of the centered letterbox: the pan is measured
    /// from CENTERED, so the art stays in contact with the tile exactly while the pan is no further
    /// than half the difference between the tile and the art. Zoomed in that reads as "the window
    /// stays inside the art"; fitted it collapses to zero, which is the same rule said about art
    /// that has nowhere to go. It lives in the view because only the view knows how big the art was
    /// drawn, the same division of labour the backdrop already keeps.
    /// </remarks>
    private void ClampPan()
    {
        if (_vm is null || _img.Source is not Bitmap bmp) return;
        double fit = Math.Min(_surface.Bounds.Width / bmp.PixelSize.Width,
                              _surface.Bounds.Height / bmp.PixelSize.Height);
        if (!double.IsFinite(fit) || fit <= 0) return;

        double scale = fit * _vm.Zoom;
        double roomX = Math.Abs(_surface.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
        double roomY = Math.Abs(_surface.Bounds.Height - bmp.PixelSize.Height * scale) / 2;

        // Assigned unconditionally: the generated setter drops an equal value, so this settles in
        // one hop rather than re-entering through the notification it would otherwise raise.
        _vm.PanX = Math.Clamp(_vm.PanX, -roomX, roomX);
        _vm.PanY = Math.Clamp(_vm.PanY, -roomY, roomY);
    }

    /// <summary>
    /// Zooms by a factor, keeping whatever is under the given point under it.
    /// </summary>
    /// <remarks>
    /// An anchored zoom is the whole difference between a magnifier and a control you fight: the
    /// pixel being pointed at is the one the reader wants more of. Derived from
    /// <see cref="Geometry"/> rather than written alongside it — the canvas pixel under the pointer
    /// is the inverse of that mapping, and the new pan is whatever puts the same pixel back under
    /// the same point at the new scale.
    /// </remarks>
    /// <param name="factor">How much bigger to draw it.</param>
    /// <param name="at">The point to hold still, in the image control's coordinates.</param>
    private void ZoomAt(double factor, Point at)
    {
        if (_vm is null || _img.Source is not Bitmap bmp) return;
        if (!Geometry(out double scale, out double offX, out double offY)) return;

        double before = _vm.Zoom;
        _vm.Zoom *= factor;
        double after = _vm.Zoom;
        if (after == before) return;                  // already at a limit

        double newScale = scale * (after / before);
        double imgW = bmp.PixelSize.Width, imgH = bmp.PixelSize.Height;
        double cx = (at.X - offX) / scale, cy = (at.Y - offY) / scale;

        _vm.PanX = at.X - cx * newScale - (_surface.Bounds.Width - imgW * newScale) / 2;
        _vm.PanY = at.Y - cy * newScale - (_surface.Bounds.Height - imgH * newScale) / 2;
        ClampPan();
    }

    // Map the pointer position (control space) to canvas pixel coords.
    private void AddPoint(PointerEventArgs e)
    {
        if (_img.Source is not Bitmap bmp || !Geometry(out var scale, out var offX, out var offY)) return;
        var p = e.GetPosition(_surface);
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
