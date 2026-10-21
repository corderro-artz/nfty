using System.Globalization;
using System.Numerics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Converters;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Diagnostics;
using Nfty.Core.Editing;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

/// <summary>
/// The Rise page: paint depth onto finished art and watch it become a model.
/// </summary>
/// <remarks>
/// <para><b>The standing preview.</b> A gesture's +1/-1 is APPLIED to the surface from press to release
/// (undone and re-applied on each move, undone on cancel, replaced by the committed command on
/// release) rather than applied, rendered and undone as the editor does: the cell-label overlay
/// renders after the call returns, so it has to find the depth already on the surface. Undo, Redo,
/// item change and Dispose all cancel a standing preview first.</para>
/// <para><b>Never hold a subject across <see cref="RiseSource.Changed"/>.</b> A rerolled recipe
/// replaces its item's subject and disposes the old one, so <see cref="Subject"/> is read from the
/// item each time, and the surface a standing preview was applied to is remembered with it.</para>
/// </remarks>
public sealed partial class RiseViewModel : ViewModelBase, IDisposable
{
    /// <summary>Roughly 30 repaints a second of the model while a stroke is being dragged.</summary>
    internal const int MeshBudgetMs = 33;

    private readonly IImageBridge _bridge;
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _picker;
    private readonly IStatusService _status;

    private (DepthStep Command, DepthSurface Surface)? _standing;
    private DepthSurface _builtSurface;
    private ModelPreviewViewModel? _preview;
    private long _lastMeshMs = long.MinValue / 2;
    private bool _switching;
    private bool _disposed;

    /// <summary>Opens the page over a source.</summary>
    /// <param name="source">What to rise; the page owns it from here and disposes it.</param>
    /// <param name="bridge">Turns the art into a bitmap.</param>
    /// <param name="nav">Page navigation.</param>
    /// <param name="dialogs">Dialogs.</param>
    /// <param name="picker">File and folder pickers.</param>
    /// <param name="status">The status bar.</param>
    public RiseViewModel(RiseSource source, IImageBridge bridge, INavigationService nav, IDialogService dialogs,
        IFilePickerService picker, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        _bridge = bridge;
        _nav = nav;
        _dialogs = dialogs;
        _picker = picker;
        _status = status;

        _selectedItem = source.InitialItem;
        _selectedItem.IsSelected = true;
        var subject = Subject;
        _builtSurface = subject.Depth;
        _canvas = bridge.ToBitmap(subject.Art);
        _mesh = BuildMesh();
        _preview = _mesh is { } m
            ? new ModelPreviewViewModel(m.Min, m.Max)
            : new ModelPreviewViewModel(-Vector3.One, Vector3.One);
        _brushSize = 1;
        source.Changed += OnSourceChanged;
        if (source is VariantRiseSource variant) variant.Saved += OnVariantSaved;
    }

    /// <summary>What the page is showing.</summary>
    public RiseSource Source { get; }

    /// <summary>The rail's rows.</summary>
    public IReadOnlyList<RiseItem> Items => Source.Items;

    /// <summary>The row on the canvas.</summary>
    [ObservableProperty] private RiseItem _selectedItem;

    /// <summary>Puts a row on the canvas.</summary>
    /// <param name="item">One of <see cref="Items"/>.</param>
    [RelayCommand]
    private void SelectItem(RiseItem item)
    {
        if (item is null) return;
        SelectedItem = item;
    }

    /// <summary>The picture on the canvas, read from the item every time (see the class remarks).</summary>
    public RiseSubject Subject => SelectedItem.Subject ?? Source.Open(SelectedItem);

    /// <summary>The depth being painted; what the label overlay reads.</summary>
    public DepthSurface Surface => Subject.Depth;

    /// <summary>The art as a bitmap; rebuilt when the picture changes, never per stroke.</summary>
    [ObservableProperty] private Bitmap _canvas;

    /// <summary>The model the art makes with its depth; null when it cannot rise.</summary>
    [ObservableProperty] private Mesh? _mesh;

    /// <summary>The art, which is also the model's texture.</summary>
    public Image<Rgba32> Texture => Subject.Art;

    /// <summary>The camera of the 3D tile.</summary>
    public ModelPreviewViewModel Preview => _preview!;

    /// <summary>Whether strokes change anything: an editable source and a picture that can rise. A
    /// locked variant still paints, as a scratch surface.</summary>
    public bool IsPaintable => Source.IsEditable && Subject.IsRisable;

    /// <summary>The rail lists variants of one layer.</summary>
    public bool IsVariantRail => Source.Kind == RiseSourceKind.Variant;

    /// <summary>The rail lists the layers of a roll.</summary>
    public bool IsRecipeRail => Source.Kind == RiseSourceKind.Recipe;

    /// <summary>The rail is a grid of tiles: a Set, and anything not named above.</summary>
    public bool IsTileRail => !IsVariantRail && !IsRecipeRail;

    /// <summary>Whether to show the read-only pill.</summary>
    public bool ShowReadOnlyPill => Source.ReadOnlyPill is not null;

    /// <summary>Text of the read-only pill.</summary>
    public string? ReadOnlyPill => Source.ReadOnlyPill;

    // ---------------------------------------------------------------- tools

    /// <summary>The armed tool. Only the five Rise tools can be armed; anything else is reverted.</summary>
    [ObservableProperty] private EditorTool _activeTool = EditorTool.Brush;

    private static bool IsRiseTool(EditorTool t) =>
        t is EditorTool.Brush or EditorTool.Line or EditorTool.Rectangle or EditorTool.Fill or EditorTool.Select;

    /// <summary>Whether the brush is armed.</summary>
    public bool IsToolBrush => ActiveTool == EditorTool.Brush;

    /// <summary>Whether the line tool is armed.</summary>
    public bool IsToolLine => ActiveTool == EditorTool.Line;

    /// <summary>Whether the rectangle tool is armed.</summary>
    public bool IsToolRectangle => ActiveTool == EditorTool.Rectangle;

    /// <summary>Whether the fill tool is armed.</summary>
    public bool IsToolFill => ActiveTool == EditorTool.Fill;

    /// <summary>Whether the select tool is armed.</summary>
    public bool IsToolSelect => ActiveTool == EditorTool.Select;

    partial void OnActiveToolChanged(EditorTool oldValue, EditorTool newValue)
    {
        if (!IsRiseTool(newValue)) { ActiveTool = oldValue; return; }
        OnPropertyChanged(nameof(IsToolBrush));
        OnPropertyChanged(nameof(IsToolLine));
        OnPropertyChanged(nameof(IsToolRectangle));
        OnPropertyChanged(nameof(IsToolFill));
        OnPropertyChanged(nameof(IsToolSelect));
        // A marquee belongs to the tool that made it.
        if (newValue != EditorTool.Select) Selection = null;
    }

    /// <summary>Arms a tool.</summary>
    /// <param name="tool">One of the five Rise tools; others are ignored.</param>
    [RelayCommand]
    private void SelectTool(EditorTool tool) => ActiveTool = tool;

    /// <summary>Brush and line width in pixels.</summary>
    [ObservableProperty] private int _brushSize;

    /// <summary>The widest brush the picture can use.</summary>
    public int BrushSizeMax => Math.Max(1, Math.Min(Surface.Width, Surface.Height));

    // The ceiling belongs to the property, so the field and the keys cannot disagree about it.
    partial void OnBrushSizeChanged(int value)
    {
        int clamped = Math.Clamp(value, 1, BrushSizeMax);
        if (clamped != value) BrushSize = clamped;
    }

    /// <summary>One pixel bigger.</summary>
    [RelayCommand] private void GrowBrush() => BrushSize++;

    /// <summary>One pixel smaller.</summary>
    [RelayCommand] private void ShrinkBrush() => BrushSize--;

    /// <summary>The marked region in canvas pixels, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private PixelRect? _selection;

    /// <summary>Whether a region is marked.</summary>
    public bool HasSelection => Selection is not null;

    /// <summary>Drops the marquee.</summary>
    [RelayCommand] private void ClearSelection() => Selection = null;

    /// <summary>Marks the whole picture, arming Select first (leaving Select drops the mark).</summary>
    [RelayCommand]
    private void SelectAll()
    {
        ActiveTool = EditorTool.Select;
        Selection = new PixelRect(0, 0, Surface.Width, Surface.Height);
    }

    /// <summary>Whether a pixel lies inside the standing marquee. Asked at press time, to tell a mark
    /// from an apply.</summary>
    /// <param name="x">Canvas x.</param>
    /// <param name="y">Canvas y.</param>
    internal bool SelectionContains(int x, int y) =>
        Selection is { } s && x >= s.X && x < s.X + s.Width && y >= s.Y && y < s.Y + s.Height;

    // ---------------------------------------------------------------- gestures

    /// <summary>Shows what a stroke would do, by applying it to the surface and leaving it standing.</summary>
    /// <param name="points">The gesture's pixel path, after the view's constraint transform.</param>
    /// <param name="side">Which side of the paper.</param>
    /// <param name="lower">True to lower by one instead of raise.</param>
    public void PreviewDepthStroke(IReadOnlyList<(int x, int y)> points, DepthSide side, bool lower)
    {
        if (points.Count == 0 || !IsPaintable) return;
        if (ActiveTool == EditorTool.Fill) return;                       // commit-only, as the editor's
        if (ActiveTool == EditorTool.Select && !SelectionContains(points[0].x, points[0].y)) return;   // marking
        UndoStanding();
        var step = new DepthStep(RegionOf(points), side, lower ? -1 : 1);
        if (step.Apply(Surface)) _standing = (step, Surface);
        DepthChanged?.Invoke();
        OnPropertyChanged(nameof(ReadoutText));
        if (NowMs() - _lastMeshMs >= MeshBudgetMs)
        {
            RebuildMesh();
            // Stamped AFTER the rebuild: a rebuild that itself costs more than the budget (39 ms at
            // 512px) would otherwise leave the next move due at once, a duty cycle of 100%.
            _lastMeshMs = NowMs();
        }
    }

    /// <summary>The clock the mesh throttle reads; a test replaces it.</summary>
    internal Func<long> NowMs { get; set; } = () => Environment.TickCount64;

    /// <summary>Called after every mesh rebuild; a test counts them and charges them a cost.</summary>
    internal Action? MeshRebuilt { get; set; }

    /// <summary>Commits a stroke as one undo step, or marks a region when Select starts outside one.</summary>
    /// <param name="points">The gesture's pixel path, after the view's constraint transform.</param>
    /// <param name="side">Which side of the paper.</param>
    /// <param name="lower">True to lower by one instead of raise.</param>
    public void ApplyDepthStroke(IReadOnlyList<(int x, int y)> points, DepthSide side, bool lower)
    {
        if (points.Count == 0) return;
        if (ActiveTool == EditorTool.Select && !SelectionContains(points[0].x, points[0].y))
        {
            // Marking changes no depth, so it works on a page that cannot paint.
            var start = points[0]; var end = points[^1];
            Selection = start == end ? null : ClampToArt(BoundsOf(points));
            GestureEnded?.Invoke();
            return;
        }
        if (!IsPaintable) return;

        bool hadStanding = _standing is not null;
        UndoStanding();
        var step = new DepthStep(RegionOf(points), side, lower ? -1 : 1);
        bool changed;
        using (Perf.Measure("Rise.DepthClick"))
            changed = Subject.History.Do(step, Surface);
        // A click that changed nothing (floored at 0, capped, on a clear pixel) pushed no history, so
        // the model and the labels are as they were: nothing to rebuild unless a preview was undone.
        if (changed || hadStanding)
        {
            RebuildMesh();
            _lastMeshMs = NowMs();
            AfterDepthEdit();
        }
        GestureEnded?.Invoke();
    }

    /// <summary>Abandons a gesture: the depth goes back to exactly what it was.</summary>
    public void CancelDepthPreview()
    {
        bool had = _standing is not null;
        UndoStanding();
        if (had)
        {
            RebuildMesh();
            DepthChanged?.Invoke();
            OnPropertyChanged(nameof(ReadoutText));
        }
        GestureEnded?.Invoke();
    }

    /// <summary>Undoes the standing preview on the surface it was applied to, silently.</summary>
    private void UndoStanding()
    {
        if (_standing is not { } s) return;
        _standing = null;
        s.Command.Undo(s.Surface);
    }

    private List<(int x, int y)> RegionOf(IReadOnlyList<(int x, int y)> points)
    {
        var surface = Surface;
        int w = surface.Width, h = surface.Height;
        switch (ActiveTool)
        {
            case EditorTool.Line:
                return Footprint.Discs(w, h, new[] { points[0], points[^1] }, BrushSize);
            case EditorTool.Rectangle:
                return Footprint.Box(BoundsOf(points), w, h);
            case EditorTool.Fill:
                return surface.SameColorRegion(points[0].x, points[0].y);
            case EditorTool.Select:
                // Only reached when the press was inside the mark: apply to the whole mark, once.
                return Footprint.Box(Selection!.Value, w, h);
            default:
                return Footprint.Discs(w, h, points, BrushSize);
        }
    }

    /// <summary>The marquee never extends past the art, whatever points the view hands over.</summary>
    private PixelRect? ClampToArt(PixelRect r)
    {
        var s = Surface;
        int x0 = Math.Max(0, r.X), y0 = Math.Max(0, r.Y);
        int x1 = Math.Min(s.Width, r.X + r.Width), y1 = Math.Min(s.Height, r.Y + r.Height);
        return x1 > x0 && y1 > y0 ? new PixelRect(x0, y0, x1 - x0, y1 - y0) : null;
    }

    private static PixelRect BoundsOf(IReadOnlyList<(int x, int y)> pts)
    {
        var (ax, ay) = pts[0]; var (bx, by) = pts[^1];
        return new PixelRect(Math.Min(ax, bx), Math.Min(ay, by), Math.Abs(bx - ax) + 1, Math.Abs(by - ay) + 1);
    }

    private Mesh? BuildMesh()
    {
        using var _ = Perf.Measure("Rise.MeshRebuild");
        var subject = Subject;
        return subject.IsRisable
            ? Riser.TryBuild(subject.Art, subject.Depth.ToDepthMap(), new RiseOptions(), Source.Title)
            : null;
    }

    private void RebuildMesh()
    {
        Mesh = BuildMesh();
        if (Mesh is { } m) _preview?.SetSubject(m.Min, m.Max);
        MeshRebuilt?.Invoke();
    }

    partial void OnDepthEdited();

    private void AfterDepthEdit()
    {
        SelectedItem.RefreshDepth();
        DepthChanged?.Invoke();
        OnPropertyChanged(nameof(ReadoutText));
        OnPropertyChanged(nameof(DimensionsText));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnDepthEdited();
    }

    private bool CanUndo() => IsPaintable && Subject.History.CanUndo;
    private bool CanRedo() => IsPaintable && Subject.History.CanRedo;

    /// <summary>Takes back the last stroke.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!CanUndo()) return;     // RelayCommand.Execute does not consult CanExecute
        UndoStanding();
        var s = Subject;
        s.History.Undo(s.Depth);
        RebuildMesh();
        AfterDepthEdit();
        GestureEnded?.Invoke();
    }

    /// <summary>Puts back the stroke just taken back.</summary>
    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (!CanRedo()) return;
        UndoStanding();
        var s = Subject;
        s.History.Redo(s.Depth);
        RebuildMesh();
        AfterDepthEdit();
        GestureEnded?.Invoke();
    }

    // ---------------------------------------------------------------- readout

    /// <summary>The pixel under the pointer, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadoutText))]
    private (int X, int Y)? _hoverPixel;

    /// <summary>"front 1 · back 0 · total 2" for a solid pixel under the pointer, else "" (the line is reserved).</summary>
    public string ReadoutText
    {
        get
        {
            if (HoverPixel is not { } h) return "";
            var s = Surface;
            if (!s.IsSolid(h.X, h.Y)) return "";
            var p = s.Get(h.X, h.Y);
            return string.Create(CultureInfo.InvariantCulture, $"front {p.Front} · back {p.Back} · total {1 + p.Front + p.Back}");
        }
    }

    /// <summary>"8 × 8 · 37 solid".</summary>
    public string DimensionsText
    {
        get
        {
            var s = Surface;
            return string.Create(CultureInfo.InvariantCulture, $"{s.Width} × {s.Height} · {s.SolidCount:N0} solid");
        }
    }

    // ---------------------------------------------------------------- the 3D tile

    /// <summary>The tile and the canvas have traded places.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPaintCanvas))]
    [NotifyPropertyChangedFor(nameof(TileCaption))]
    [NotifyPropertyChangedFor(nameof(SwapTip))]
    private bool _previewSwapped;

    /// <summary>The tile is at its larger size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileSize))]
    [NotifyPropertyChangedFor(nameof(EnlargeTip))]
    private bool _previewEnlarged;

    /// <summary>Side of the corner tile.</summary>
    public double TileSize => PreviewEnlarged ? 208 : 120;

    /// <summary>The big slot shows the paint canvas unless the model has taken it over.</summary>
    public bool ShowPaintCanvas => !PreviewSwapped;

    /// <summary>The tile names whichever half it is showing: the one the big slot is not.</summary>
    public string TileCaption => PreviewSwapped ? "CANVAS" : "3D";

    /// <summary>Tooltip for the swap button.</summary>
    public string SwapTip => PreviewSwapped ? "Return the pane to the paint canvas" : "Fill pane with the 3D view";

    /// <summary>Tooltip for the enlarge button; a toggle, so it names its undo.</summary>
    public string EnlargeTip => PreviewEnlarged ? "Shrink preview" : "Enlarge preview";

    /// <summary>Trades the canvas and the tile.</summary>
    [RelayCommand] private void SwapPreview() => PreviewSwapped = !PreviewSwapped;

    /// <summary>Toggles the tile's size.</summary>
    [RelayCommand] private void EnlargePreview() => PreviewEnlarged = !PreviewEnlarged;

    /// <summary>Whether the tile shows the "2D only" ink.</summary>
    public bool ShowTwoDOnly => Subject.Depth.PartialCount > 0;

    // ---------------------------------------------------------------- recipe die

    /// <summary>The die's face, 1..6.</summary>
    [ObservableProperty] private int _dieFace = 4;

    /// <summary>Whether the source can reroll.</summary>
    public bool HasDie => Source is RecipeRiseSource;

    /// <summary>Rolls the recipe again. The die never lands on the face it is showing: the face is the
    /// button's only confirmation. Random.Shared, never the engine's RNG: decoration must not touch
    /// the stream that decides assets.</summary>
    [RelayCommand(CanExecute = nameof(HasDie))]
    private void Reroll()
    {
        if (Source is not RecipeRiseSource recipe) return;
        CancelDepthPreview();
        int pick = Random.Shared.Next(1, DieFaceConverter.Faces);
        DieFace = pick >= DieFace ? pick + 1 : pick;
        recipe.Reroll();     // raises Changed; OnSourceChanged adopts the new subject
    }

    // ---------------------------------------------------------------- events

    /// <summary>The depth on the surface changed; the label overlay invalidates.</summary>
    public event Action? DepthChanged;

    /// <summary>The picture changed; the view re-frames the camera with the real aspect.</summary>
    public event Action? SubjectChanged;

    /// <summary>A gesture ended; the view clears <c>ModelPreviewView.HostInteracting</c> so the next paint is exact.</summary>
    public event Action? GestureEnded;

    // ---------------------------------------------------------------- following the source

    partial void OnSelectedItemChanged(RiseItem? oldValue, RiseItem newValue)
    {
        if (_disposed) return;
        _switching = true;
        try
        {
            UndoStanding();
            if (oldValue is not null && !ReferenceEquals(oldValue, newValue)) oldValue.IsSelected = false;
            newValue.IsSelected = true;
            Selection = null;
            HoverPixel = null;
            Zoom = ZoomMin;
            PanX = 0; PanY = 0;
            AdoptSubject();
        }
        finally { _switching = false; }
        NotifySubjectBound();
    }

    /// <summary>Rebuilds everything that is made from the subject, and tells the view to re-frame.</summary>
    private void AdoptSubject()
    {
        var subject = Subject;
        _builtSurface = subject.Depth;
        var old = Canvas;
        Canvas = _bridge.ToBitmap(subject.Art);
        old.Dispose();
        RebuildMesh();
        _preview?.Reset(1f);
        ClampToSubject();
        SubjectChanged?.Invoke();
    }

    private void ClampToSubject()
    {
        if (BrushSize > BrushSizeMax) BrushSize = BrushSizeMax;
        if (GridSize > GridSizeMax) GridSize = GridSizeMax;
        OnPropertyChanged(nameof(BrushSizeMax));
        OnPropertyChanged(nameof(GridSizeMax));
    }

    private void NotifySubjectBound()
    {
        OnPropertyChanged(nameof(Subject));
        OnPropertyChanged(nameof(Surface));
        OnPropertyChanged(nameof(Texture));
        OnPropertyChanged(nameof(IsPaintable));
        OnPropertyChanged(nameof(ShowTwoDOnly));
        OnPropertyChanged(nameof(DepthNote));
        OnPropertyChanged(nameof(ReadoutText));
        OnPropertyChanged(nameof(DimensionsText));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DepthNote));
        FillAllCommand.NotifyCanExecuteChanged();
        OnDepthEdited();
        DepthChanged?.Invoke();
    }

    private void OnSourceChanged()
    {
        if (_disposed || _switching) return;
        // A source can replace the subject (reroll) or reseed its depth in place (a Set re-read): in
        // both cases the surface a standing preview was applied to is gone, so drop it, don't undo it.
        var surface = Subject.Depth;
        if (!ReferenceEquals(surface, _builtSurface))
        {
            _standing = null;
            Selection = null;
            AdoptSubject();
        }
        foreach (var item in Items) item.NotifyThumbnail();
        NotifySubjectBound();
    }

    /// <summary>Frees the bitmap and the source. Safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        UndoStanding();
        _disposed = true;
        Source.Changed -= OnSourceChanged;
        if (Source is VariantRiseSource variant) variant.Saved -= OnVariantSaved;
        Canvas.Dispose();
        Source.Dispose();
    }
}
