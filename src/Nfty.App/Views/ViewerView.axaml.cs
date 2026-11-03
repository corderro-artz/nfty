using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Reactive;
using Avalonia.VisualTree;
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
        // A gallery tile's click arrives here, bubbled from its button; the tile is the button's own
        // DataContext, so no binding has to hop out of the row template to reach the command.
        AddHandler(Button.ClickEvent, OnTileClick);

        _rows = this.FindControl<ScrollViewer>("FactRows")!;
        _list = this.FindControl<ItemsControl>("FactList")!;
        _railGrid = this.FindControl<Grid>("RailGrid")!;
        LayoutUpdated += (_, _) => SnapRows();
        // The wheel over the facts moves by WHOLE rows: the ScrollViewer's own wheel step is a pixel
        // distance that leaves a row cut at the fold. Tunnel, so the ScrollViewer never sees it.
        _rows.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (e.Delta.Y == 0) return;
            StepRows(e.Delta.Y > 0 ? -1 : 1);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        // The viewer's keys, all on one focus-guarded handler (the editor's pattern): the page takes
        // focus on arrival so they are live, and none fires while a text field (the custom backdrop
        // color) has the keyboard. handledEventsToo: a focused value text block marks keys handled.
        AddHandler(KeyDownEvent, (_, e) => OnShortcut(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        AttachedToVisualTree += (_, _) => Focus();

        // Typing a color makes it the ground. Only while the reader is in the field: the binding
        // writing a remembered color in must not change the backdrop the window reopened with.
        var hex = this.FindControl<TextBox>("CustomColorBox")!;
        hex.TextChanged += (_, _) =>
        {
            if (hex.IsKeyboardFocusWithin && _vm is not null) _vm.TwoD.Backdrop = ViewerBackdrop.Custom;
        };
        Rebind();
    }

    private readonly ScrollViewer _rows;
    private readonly ItemsControl _list;
    private readonly Grid _railGrid;
    private int _firstRow;

    /// <summary>The first fully shown row of the rail's list, as the snap last settled it.</summary>
    internal int FirstRow => _firstRow;

    // ---------------------------------------------------------------- the info rail's snap

    // The rows' tops and bottoms in the list's own space. The list is a StackPanel inside a ScrollViewer
    // that hands it infinite height, so these do not depend on the host's height: writing that height
    // cannot move them, which is what keeps SnapRows out of an infinite layout loop.
    private List<(double Top, double Bottom)> RowSpans()
    {
        var spans = new List<(double, double)>();
        foreach (var c in _list.GetRealizedContainers())
        {
            if (!c.IsVisible) continue;
            spans.Add((c.Bounds.Top, c.Bounds.Bottom));
        }
        spans.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return spans;
    }

    /// <summary>
    /// Sizes the facts host to a WHOLE number of rows from the first shown one, so the fold never
    /// cuts a row, and tells the view model how many sit above and below it.
    /// </summary>
    /// <remarks>
    /// <para>The ROOM is the rail's star row, read off the Grid (the Set browser rarity rail's rule): a
    /// star row is what is left after the Auto rows, not what its child asks for, so writing the
    /// host's height cannot change it. The rows' spans cannot change either (see RowSpans). With both
    /// inputs out of its reach, the handler settles in one pass.</para>
    /// <para>Rows are not one height (a hash takes two lines, a sentence three), so the snap is a
    /// prefix of the rows from the first shown one, not a multiple of a pitch. When the rows from
    /// there to the end leave room, earlier rows are pulled back in: a window grown taller never
    /// shows a gap under the last row with rows hidden above.</para>
    /// </remarks>
    private void SnapRows()
    {
        if (_vm is null) return;
        var spans = RowSpans();
        double room = _railGrid.RowDefinitions[3].ActualHeight - 8 /* the card's top margin */ - 2 /* its border */;
        if (spans.Count == 0 || room <= 0 || double.IsNaN(room))
        {
            _vm.RowsAbove = 0;
            _vm.RowsBelow = 0;
            return;
        }

        int first = Math.Clamp(_firstRow, 0, spans.Count - 1);
        // Pull earlier rows back in while everything from there to the end still fits.
        while (first > 0 && spans[^1].Bottom - spans[first - 1].Top <= room + 0.5) first--;
        int last = first;
        while (last + 1 < spans.Count && spans[last + 1].Bottom - spans[first].Top <= room + 0.5) last++;

        // At least one row, even when a single row is taller than the room: a rail too short for one
        // row is a smaller window than the app allows, and one clipped row beats none.
        double height = Math.Min(spans[last].Bottom - spans[first].Top, room);
        _firstRow = first;
        // IsNaN FIRST: an unset Height is NaN, and every comparison with NaN is false.
        if (double.IsNaN(_rows.Height) || Math.Abs(_rows.Height - height) > 0.5) _rows.Height = height;
        double offset = spans[first].Top;
        if (Math.Abs(_rows.Offset.Y - offset) > 0.5) _rows.Offset = new Vector(0, offset);
        _vm.RowsAbove = first;
        _vm.RowsBelow = spans.Count - 1 - last;
    }

    /// <summary>Moves the first shown row by <paramref name="delta"/> rows, clamped so the last page is
    /// full, and re-snaps.</summary>
    /// <param name="delta">Rows to move; negative scrolls up.</param>
    internal void StepRows(int delta)
    {
        var spans = RowSpans();
        if (spans.Count == 0 || _vm is null) return;
        int shown = Math.Max(1, spans.Count - _vm.RowsAbove - _vm.RowsBelow);
        _firstRow = Math.Clamp(_firstRow + delta, 0, Math.Max(0, spans.Count - shown));
        SnapRows();
    }

    private void OnMoreRequested(bool down)
    {
        if (_vm is null) return;
        int shown = Math.Max(1, RowSpans().Count - _vm.RowsAbove - _vm.RowsBelow);
        if (down) StepRows(shown);
        else { _firstRow = 0; SnapRows(); }
    }

    // ---------------------------------------------------------------- the keys

    private void OnShortcut(KeyEventArgs e)
    {
        if (_vm is null || !FocusIsOurs()) return;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            // Esc folds the extended panel; otherwise it is not ours (the window's dialog layer).
            if (!_vm.IsInfoExtended) return;
            _vm.IsInfoExtended = false;
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return;
        switch (e.Key)
        {
            case Key.I:
                _vm.IsInfoExtended = !_vm.IsInfoExtended; break;
            case Key.D1 or Key.NumPad1:
                _vm.Layout = ViewerLayout.TwoD; break;
            case Key.D2 or Key.NumPad2:
                _vm.Layout = ViewerLayout.Both; break;
            case Key.D3 or Key.NumPad3:
                _vm.Layout = ViewerLayout.ThreeD; break;
            case Key.D0 or Key.NumPad0:
                _vm.TwoD.FitCommand.Execute(null); break;
            case Key.S when _vm.CanShowThreeD:
                _vm.CycleTurntableCommand.Execute(null); break;
            case Key.PageDown:
                StepRows(Math.Max(1, RowSpans().Count - _vm.RowsAbove - _vm.RowsBelow)); break;
            case Key.PageUp:
                StepRows(-Math.Max(1, RowSpans().Count - _vm.RowsAbove - _vm.RowsBelow)); break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Whether the keyboard is the viewer's: focus is inside this view and not in a text
    /// field (the custom backdrop color), where a typed "1" is text.</summary>
    private bool FocusIsOurs()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual v) return false;
        if (v.FindAncestorOfType<ViewerView>(includeSelf: true) != this) return false;
        return v.FindAncestorOfType<TextBox>(includeSelf: true) is null;
    }

    private void OnTileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e.Source is Control c && c.FindAncestorOfType<Button>(includeSelf: true) is { DataContext: Nfty.App.Viewer.GalleryItem item }
            && _vm?.Gallery is { } gallery)
        {
            e.Handled = true;
            gallery.OpenTileCommand.Execute(item);
        }
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;

    private void Rebind()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmChanged;
            _vm.TwoD.PropertyChanged -= OnTwoDChanged;
            _vm.MoreRequested -= OnMoreRequested;
        }
        _vm = DataContext as ViewerViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmChanged;
            _vm.TwoD.PropertyChanged += OnTwoDChanged;
            _vm.MoreRequested += OnMoreRequested;
        }
        _firstRow = 0;
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
            case nameof(ViewerViewModel.SelectedGroup):
                // A new tab starts at its first row.
                _firstRow = 0;
                break;
            case nameof(ViewerViewModel.IsInfoExtended) when _vm is { IsInfoExtended: false }:
                // Folding the panel hands the keyboard back to the page, not to a control under it.
                Focus();
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
                _backdrop.Background = Brush("BackdropLightBrush");
                break;
            case ViewerBackdrop.Dark:
                _backdrop.Background = Brush("BackdropDarkBrush");
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
