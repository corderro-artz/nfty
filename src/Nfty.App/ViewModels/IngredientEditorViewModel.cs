using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

public enum EditorTool { Brush, Eraser, Rectangle, Circle, Triangle, Select, Fill }

/// <summary>A variant in the editor filmstrip. Observable so rename/reweight update the bound
/// filmstrip entry in place (no collection-item replacement / selection churn).</summary>
public partial class EditorVariant : ObservableObject
{
    public string Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private double _weight;
    [ObservableProperty] private Bitmap _thumbnail;

    /// <summary>Drives the .vcard selected treatment. The filmstrip is an ItemsControl (not a
    /// Selector), so selection has to travel on the item itself.</summary>
    [ObservableProperty] private bool _isSelected;

    public EditorVariant(string id, string name, double weight, Bitmap thumbnail)
    { Id = id; _name = name; _weight = weight; _thumbnail = thumbnail; }
}

/// <summary>Ingredient Editor: the canvas/colorize/preview screen reached from an Ingredient's
/// detail pane. Wired to the real opened ingredient — the filmstrip is its actual variants with
/// rendered thumbnails. Painting, undo/redo, and variant list mutation remain stubs; canvas/live
/// preview bitmaps arrive in Task 7.</summary>
public partial class IngredientEditorViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _nav;
    private readonly INotYetWired _notify;
    private readonly IImageBridge _bridge;
    private readonly LoadedRecipe _recipe;
    private readonly ICookBookSession _session;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _picker;
    private readonly string? _looseSavePath;   // set → save straight to this .igt, not into a cookbook
    private readonly LoadedCookBook? _ownedBook;   // the synthetic wrapper book, owned only on the loose path
    private LoadedIngredient _ing;
    private readonly IngredientDraft _draft;
    private readonly Dictionary<string, EditHistory> _history = new(StringComparer.Ordinal);

    // Custom-kind imports: VM-owned full-colour images, keyed by variant id. Never routed through
    // ValueMap (which is grayscale by construction) — this is what keeps a custom import's colour
    // intact end to end. Disposed on replace and in Dispose; the originals in _ing.VariantImages are
    // never disposed here (the session/loose wrapper owns them).
    private readonly Dictionary<string, Image<Rgba32>> _importedCustom = new(StringComparer.Ordinal);

    [ObservableProperty] private EditorTool _activeTool = EditorTool.Brush;
    [ObservableProperty] private int _brushValue = 128;
    [ObservableProperty] private int _brushSize = 8;
    [ObservableProperty] private LayerKind _mode;
    [ObservableProperty] private double _hueMin, _hueMax = 360, _satMin = 40, _satMax = 100;
    [ObservableProperty] private int _hueQuantize = 12, _satQuantize = 4;
    [ObservableProperty] private string _fixedColor = "hex:d6249f";
    [ObservableProperty] private EditorVariant? _selectedVariant;
    [ObservableProperty] private Bitmap _canvas = default!;
    [ObservableProperty] private Bitmap _preview = default!;
    private int _previewSalt;

    // Inline rename/reweight of the selected variant. Mirror the selection; write valid changes
    // through to the draft + filmstrip. _syncingSelection suppresses write-back while we push a new
    // selection's values into these fields.
    private bool _syncingSelection;
    [ObservableProperty] private string _selectedName = "";
    [ObservableProperty] private double _selectedWeight = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportImageCommand))]
    private bool _isSaving;

    /// <summary>Raised after a successful Save with the newly-spliced graph, so a listener
    /// (Explorer) can rebuild its tree in place instead of reloading the whole archive.</summary>
    public event Action<LoadedCookBook>? Saved;

    /// <summary>Save is offered for any edited ingredient with a known source file. Custom is
    /// additionally gated so a session-added variant that was never imported into can't save a blank
    /// raster (every variant must have an effective image — imported or original).</summary>
    public bool CanSave => IsDirty && !IsSaving && (!IsCustom || AllCustomVariantsHaveImages)
        && (_looseSavePath is not null || _session.SourcePath is not null);

    /// <summary>Custom (full-colour, composited-as-is) ingredients are import-only — painting them
    /// would require routing through the grayscale <see cref="ValueMap"/>, silently destroying colour.</summary>
    private bool IsCustom => _ing.Manifest.Kind == LayerKind.Custom;

    /// <summary>Backs the view's tool-strip <c>IsEnabled</c>: false for custom ingredients.</summary>
    public bool CanPaint => !IsCustom;

    /// <summary>The full-colour image a custom variant currently shows: this session's import if any,
    /// else its original archive image. Null only for a session-added variant never imported into.</summary>
    private Image<Rgba32>? EffectiveCustomImage(string variantId) =>
        _importedCustom.TryGetValue(variantId, out var imported) ? imported
        : _ing.VariantImages.TryGetValue(variantId, out var original) ? original
        : null;

    /// <summary>Custom Save gate: every draft variant (including any added this session) must have an
    /// effective image, so Save never writes a blank raster into the archive.</summary>
    private bool AllCustomVariantsHaveImages => FirstCustomVariantWithoutImage is null;

    /// <summary>The first custom variant still missing an image, so the UI can say which one blocks
    /// Save instead of just greying the button out.</summary>
    private VariantDraft? FirstCustomVariantWithoutImage =>
        _draft.Variants.FirstOrDefault(v => EffectiveCustomImage(v.Id) is null);

    /// <summary>Why Save is unavailable on a custom ingredient, or null when it is available.
    /// (Custom Save depends on the per-variant image set, which changes on import/add/duplicate/delete —
    /// every one of those must re-notify <see cref="SaveCommand"/>.)</summary>
    public string? SaveBlockedReason => IsCustom && FirstCustomVariantWithoutImage is { } v
        ? $"Import an image for “{v.Name}” before saving."
        : null;

    /// <summary>Re-evaluate Save's availability after anything that changes the custom image set.</summary>
    private void NotifySaveAvailability()
    {
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SaveBlockedReason));
    }

    /// <summary>Left-hand filmstrip: the ingredient's real variants, rendered the way the cook
    /// path would (colorized for dynamic/static, raw for custom).</summary>
    public ObservableCollection<EditorVariant> Variants { get; } = new();

    /// <summary>Dynamic layers roll a colour per asset from a hue/sat range.</summary>
    public bool ShowColourRange => Mode == LayerKind.Dynamic;

    /// <summary>Static layers apply one fixed colour deterministically.</summary>
    public bool ShowFixedColour => Mode == LayerKind.Static;

    /// <summary>Backs the "Static" toggle.</summary>
    public bool IsModeStatic
    {
        get => Mode == LayerKind.Static;
        set { if (value) Mode = LayerKind.Static; }
    }

    /// <summary>Backs the "Dynamic" toggle.</summary>
    public bool IsModeDynamic
    {
        get => Mode == LayerKind.Dynamic;
        set { if (value) Mode = LayerKind.Dynamic; }
    }

    /// <summary>Per-tool flags for the toolstrip's active state. The old vertical text-button column
    /// showed no selection at all, so the user could not tell which tool was armed.</summary>
    public bool IsToolBrush => ActiveTool == EditorTool.Brush;
    public bool IsToolEraser => ActiveTool == EditorTool.Eraser;
    public bool IsToolRectangle => ActiveTool == EditorTool.Rectangle;
    public bool IsToolCircle => ActiveTool == EditorTool.Circle;
    public bool IsToolTriangle => ActiveTool == EditorTool.Triangle;
    public bool IsToolSelect => ActiveTool == EditorTool.Select;
    public bool IsToolFill => ActiveTool == EditorTool.Fill;

    partial void OnActiveToolChanged(EditorTool value)
    {
        OnPropertyChanged(nameof(IsToolBrush));
        OnPropertyChanged(nameof(IsToolEraser));
        OnPropertyChanged(nameof(IsToolRectangle));
        OnPropertyChanged(nameof(IsToolCircle));
        OnPropertyChanged(nameof(IsToolTriangle));
        OnPropertyChanged(nameof(IsToolSelect));
        OnPropertyChanged(nameof(IsToolFill));
    }

    /// <summary>The segmented Dynamic/Static control is two Buttons, not two RadioButtons, so it
    /// needs commands rather than two-way IsChecked bindings.</summary>
    [RelayCommand] private void SetModeDynamic() => Mode = LayerKind.Dynamic;
    [RelayCommand] private void SetModeStatic() => Mode = LayerKind.Static;

    public IngredientEditorViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, INavigationService nav, INotYetWired notify, ICookBookSession session,
        IDialogService dialogs, IFilePickerService picker, string? looseSavePath = null)
    {
        _ing = ing; _bridge = bridge; _nav = nav; _notify = notify;
        _recipe = recipe; _session = session; _dialogs = dialogs; _picker = picker;
        _looseSavePath = looseSavePath;
        // A loose (standalone .igt) editor owns its synthetic wrapper book — dispose it with the editor.
        if (looseSavePath is not null) _ownedBook = book;

        _draft = new IngredientDraft(ing.Manifest.Id, ing.Manifest.Name, ing.Manifest.Kind, ing.Manifest.Colorization,
            book.Manifest.Canvas,
            ing.Manifest.Variants.Select(v => new VariantDraft(v.Id, v.Name, v.Weight,
                ValueMap.FromImage(ing.VariantImages[v.Id]))));
        foreach (var v in _draft.Variants) _history[v.Id] = new EditHistory();

        foreach (var v in ing.Manifest.Variants)
            Variants.Add(new EditorVariant(v.Id, v.Name, v.Weight, VariantImagery.Render(bridge, ing, v.Id)));
        // Set BEFORE Mode below: OnSelectedVariantChanged's RebuildSurfaces() needs Variants
        // populated, and it runs (and safely no-ops on the still-null Canvas/Preview) before
        // Mode's own hook does its rebuild.
        SelectedVariant = Variants.Count > 0 ? Variants[0] : null;

        // The editor only toggles between Dynamic and Static; a Custom layer (composited as-is,
        // never colorized) defaults to Dynamic so the toggle has a sensible starting point.
        // Assigning Mode (as opposed to a field initializer) fires OnModeChanged, which rebuilds
        // the surfaces with the final colour state.
        Mode = ing.Manifest.Kind == LayerKind.Custom ? LayerKind.Dynamic : ing.Manifest.Kind;
        // Fallback: if Mode's incoming value equalled its field default, OnModeChanged never
        // fired and Canvas/Preview are still unset from the ctor's perspective — build them now.
        if (Canvas is null) RebuildSurfaces();
    }

    private VariantDraft? ActiveDraft =>
        SelectedVariant is null ? null : _draft.Variants.FirstOrDefault(d => d.Id == SelectedVariant.Id);
    private ValueMap? ActiveMap => ActiveDraft?.Map;
    internal byte ValueAt(int x, int y) => ActiveMap!.GetValue(x, y);   // test hook

    // Canvas shows the grayscale VALUE-MAP being painted; Preview shows the colorized companion.
    // Custom ingredients are import-only: both surfaces render the effective full-colour image
    // (this session's import, else the original) rather than a value-map round-trip, so nothing
    // reduces their colour to grayscale.
    private Bitmap RenderCanvas()
    {
        if (IsCustom && EffectiveCustomImage(SelectedVariant!.Id) is { } eff)
            return _bridge.ToBitmap(eff);
        using var img = ActiveMap!.ToImage();
        return _bridge.ToBitmap(img);
    }

    private Bitmap RenderPreview()
    {
        if (IsCustom && EffectiveCustomImage(SelectedVariant!.Id) is { } eff)
            return _bridge.ToBitmap(eff);
        using var img = ActiveMap!.ToImage();
        return _ing.Manifest.Colorization is null
            ? _bridge.ToBitmap(img)
            : VariantImagery.RenderWith(_bridge, img, Mode == LayerKind.Dynamic,
                HueMin, HueMax, SatMin, SatMax, FixedColor, _previewSalt);
    }

    private void RebuildSurfaces()
    {
        if (SelectedVariant is null) return;   // nothing to render (zero-variant ingredient)
        var oldCanvas = Canvas; var oldPreview = Preview;
        Canvas = RenderCanvas();
        Preview = RenderPreview();
        oldCanvas?.Dispose(); oldPreview?.Dispose();
    }

    partial void OnModeChanged(LayerKind value)
    {
        OnPropertyChanged(nameof(ShowColourRange));
        OnPropertyChanged(nameof(ShowFixedColour));
        OnPropertyChanged(nameof(IsModeStatic));
        OnPropertyChanged(nameof(IsModeDynamic));
        RebuildSurfaces();
    }

    partial void OnSelectedVariantChanged(EditorVariant? oldValue, EditorVariant? newValue)
    {
        // The filmstrip is an ItemsControl, so the selected treatment rides on the item.
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        RebuildSurfaces();
        UndoCommand?.NotifyCanExecuteChanged();
        RedoCommand?.NotifyCanExecuteChanged();
        DuplicateVariantCommand?.NotifyCanExecuteChanged();
        ImportImageCommand?.NotifyCanExecuteChanged();
        SyncSelectedFields();
    }

    partial void OnSelectedNameChanged(string value)
    {
        if (_syncingSelection) return;
        if (ActiveDraft is not { } d) return;
        if (string.IsNullOrWhiteSpace(value)) { SyncSelectedFields(); return; }   // reject → restore
        d.Name = value;
        SelectedVariant!.Name = value;      // observable → filmstrip updates in place
        IsDirty = true;
    }

    partial void OnSelectedWeightChanged(double value)
    {
        if (_syncingSelection) return;
        if (ActiveDraft is not { } d) return;
        if (value <= 0) { SyncSelectedFields(); return; }                         // reject → restore
        d.Weight = value;
        SelectedVariant!.Weight = value;    // observable → filmstrip updates in place
        IsDirty = true;
    }

    private void SyncSelectedFields()
    {
        _syncingSelection = true;
        try
        {
            SelectedName = SelectedVariant?.Name ?? "";
            SelectedWeight = SelectedVariant?.Weight ?? 1;
        }
        finally { _syncingSelection = false; }   // never latch the guard on if an assignment throws
    }

    partial void OnHueMinChanged(double value) { RebuildSurfaces(); OnPropertyChanged(nameof(HueRangeText)); }
    partial void OnHueMaxChanged(double value) { RebuildSurfaces(); OnPropertyChanged(nameof(HueRangeText)); }
    partial void OnSatMinChanged(double value) { RebuildSurfaces(); OnPropertyChanged(nameof(SatRangeText)); }
    partial void OnSatMaxChanged(double value) { RebuildSurfaces(); OnPropertyChanged(nameof(SatRangeText)); }
    partial void OnFixedColorChanged(string value) => RebuildSurfaces();
    partial void OnHueQuantizeChanged(int value) => OnPropertyChanged(nameof(ApproxColorsText));
    partial void OnSatQuantizeChanged(int value) => OnPropertyChanged(nameof(ApproxColorsText));
    partial void OnBrushValueChanged(int value) => OnPropertyChanged(nameof(BrushSwatch));

    /// <summary>Live readouts beside each range control (mockup .cv), so the sliders' current span is
    /// legible without reading the handles' positions off the track.</summary>
    public string HueRangeText => $"{HueMin:0}–{HueMax:0}°";
    public string SatRangeText => $"{SatMin:0}–{SatMax:0}%";

    /// <summary>How many distinct colours the quantize settings actually admit - the product of the
    /// two bucket counts. This is the number that decides how much of the colour space survives into
    /// DNA, so the editor states it rather than leaving the user to multiply two steppers.</summary>
    public string ApproxColorsText => $"≈ {HueQuantize * SatQuantize} colors";

    /// <summary>The paint value as a swatch (mockup .swatch). A value-map is grayscale, so the brush
    /// swatch is the grey it will actually lay down.</summary>
    public Avalonia.Media.Color BrushSwatch =>
        Avalonia.Media.Color.FromRgb((byte)BrushValue, (byte)BrushValue, (byte)BrushValue);

    [RelayCommand] private void SelectTool(EditorTool tool) => ActiveTool = tool;

    private bool CanImport() => SelectedVariant is not null && !IsSaving;

    /// <summary>Replaces the selected variant's raster from a PNG on disk. Custom: the image is kept
    /// verbatim, full colour, in the VM-owned <see cref="_importedCustom"/> dict — never routed through
    /// <see cref="ValueMap"/>. Dynamic/static: the PNG becomes the variant's value-map (clearing its
    /// undo history — the old snapshots describe pixels that no longer exist).</summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportImage()
    {
        if (ActiveDraft is not { } target) return;
        string? path;
        try { path = await _picker.OpenFileAsync("Import variant image", ".png"); }
        catch (Exception ex) { await ShowErrorAsync("Could not import", ex.Message); return; }
        if (path is null) return;   // cancelled

        Image<Rgba32> img;
        try { img = Image.Load<Rgba32>(path); }
        catch (Exception ex) { await ShowErrorAsync("Could not import", ex.Message); return; }
        try
        {
            var canvas = _draft.Canvas;
            if (img.Width != canvas.Width || img.Height != canvas.Height)
            {
                await ShowErrorAsync("Wrong size",
                    $"This image is {img.Width}×{img.Height}; the canvas is {canvas.Width}×{canvas.Height}.");
                return;
            }

            if (IsCustom)
            {
                if (_importedCustom.TryGetValue(target.Id, out var prev))
                { _importedCustom.Remove(target.Id); prev.Dispose(); }
                _importedCustom[target.Id] = img.Clone();   // VM owns this copy
                IsDirty = true;
                RebuildSurfaces();
                RefreshThumbnail(target.Id);
                NotifySaveAvailability();   // an import can satisfy the custom Save gate
                return;   // img itself is disposed by the finally below
            }

            // Dynamic/static: the PNG becomes the variant's value-map, which stores lightness only,
            // so a colour source has to be collapsed to one channel.
            //
            // Desaturate FIRST rather than handing the colour image straight to ValueMap.FromImage.
            // FromImage reads the RED channel - exact and lossless for its real job, round-tripping
            // this layer's own already-grayscale PNG, but arbitrary for foreign art: pure green would
            // import as pure BLACK and pure red as pure WHITE, though both read as mid-bright to the
            // eye. Grayscale() is ITU-R BT.709 luminance, so R==G==B afterwards and FromImage's own
            // contract is left exactly as it was.
            bool hadColour = HasColour(img);
            if (hadColour) img.Mutate(x => x.Grayscale());

            var src = ValueMap.FromImage(img);
            for (int y = 0; y < canvas.Height; y++)
                for (int x = 0; x < canvas.Width; x++)
                    target.Map.Set(x, y, src.GetValue(x, y), src.GetAlpha(x, y));
            _history[target.Id] = new EditHistory();   // old snapshots describe pixels that are gone
            UndoCommand.NotifyCanExecuteChanged();

            if (hadColour)
            {
                await ShowErrorAsync("Colour flattened",
                    "This layer is a value-map: it stores lightness only, and its colour is chosen at "
                    + "generation time. The imported image was converted to its lightness, so its own "
                    + "colours are gone. Import into a custom layer instead to keep the image exactly "
                    + "as-is.");
            }
            RedoCommand.NotifyCanExecuteChanged();
            IsDirty = true;
            RebuildSurfaces();
            RefreshThumbnail(target.Id);
            NotifySaveAvailability();
        }
        finally { img.Dispose(); }
    }

    /// <summary>True if any pixel carries colour (channels not all equal). Used only to warn on a
    /// value-map import; a fully transparent pixel cannot show colour, so it is skipped.</summary>
    private static bool HasColour(Image<Rgba32> img)
    {
        bool found = false;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !found; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A != 0 && (p.R != p.G || p.G != p.B)) { found = true; break; }
                }
            }
        });
        return found;
    }

    private async Task ShowErrorAsync(string title, string message) =>
        await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, title, message));

    /// <summary>Re-render one filmstrip entry's thumbnail after its pixels changed.</summary>
    private void RefreshThumbnail(string variantId)
    {
        var entry = Variants.FirstOrDefault(v => v.Id == variantId);
        if (entry is null) return;
        Bitmap? next = IsCustom
            ? (EffectiveCustomImage(variantId) is { } eff ? _bridge.ToBitmap(eff) : null)
            : (_draft.Variants.FirstOrDefault(v => v.Id == variantId) is { } vd ? RenderThumb(vd.Map) : null);
        if (next is null) return;
        var old = entry.Thumbnail;
        entry.Thumbnail = next;
        old.Dispose();
    }

    /// <summary>Commit one completed gesture as a Core edit command against the active variant.</summary>
    public void ApplyToolStroke(IReadOnlyList<(int x, int y)> points)
    {
        if (IsCustom) return;   // import-only — painting would route through the grayscale ValueMap
        if (ActiveDraft is null || points.Count == 0) return;
        var map = ActiveDraft.Map;
        var hist = _history[ActiveDraft.Id];
        IEditCommand? cmd = ActiveTool switch
        {
            EditorTool.Brush => new BrushStroke(new Brush(BrushSize, (byte)BrushValue), points),
            EditorTool.Eraser => new EraseStroke(BrushSize, points),
            EditorTool.Fill => new FloodFill(points[0].x, points[0].y, (byte)BrushValue),
            EditorTool.Rectangle => new DrawShape(ShapeKind.Rectangle, BoundsOf(points), (byte)BrushValue),
            EditorTool.Circle => new DrawShape(ShapeKind.Ellipse, BoundsOf(points), (byte)BrushValue),
            EditorTool.Triangle => new DrawShape(ShapeKind.Triangle, BoundsOf(points), (byte)BrushValue),
            _ => null,   // Select — no-op this slice
        };
        if (cmd is null) return;
        if (!hist.Do(cmd, map)) return;   // no-op edit changed nothing — don't dirty history, rebuild, or mark dirty
        IsDirty = true;
        RebuildSurfaces();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private static PixelRect BoundsOf(IReadOnlyList<(int x, int y)> pts)
    {
        var (ax, ay) = pts[0]; var (bx, by) = pts[^1];
        int x = System.Math.Min(ax, bx), y = System.Math.Min(ay, by);
        int w = System.Math.Abs(bx - ax) + 1, h = System.Math.Abs(by - ay) + 1;
        return new PixelRect(x, y, w, h);
    }

    private bool CanUndo() => ActiveDraft is not null && _history[ActiveDraft.Id].CanUndo;
    private bool CanRedo() => ActiveDraft is not null && _history[ActiveDraft.Id].CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _history[ActiveDraft!.Id].Undo(ActiveDraft.Map);
        RebuildSurfaces();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _history[ActiveDraft!.Id].Redo(ActiveDraft.Map);
        RebuildSurfaces();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectVariant(EditorVariant v) => SelectedVariant = v;

    // Smallest unused "variant-N" over the draft's current ids (deterministic, no RNG).
    private string NextVariantId()
    {
        for (int n = 1; ; n++) { var id = $"variant-{n}"; if (_draft.Variants.All(v => v.Id != id)) return id; }
    }

    // A filmstrip thumbnail for a draft variant's value-map — colorized like the preview for
    // dynamic/static, grayscale for custom (a freshly added variant has no entry in
    // _ing.VariantImages to render from, so render from the draft map like RenderPreview does).
    /// <summary>Thumbnail for a variant that may not have a filmstrip entry yet (add/duplicate):
    /// custom renders its effective full-colour image, everything else its value-map.</summary>
    private Bitmap RenderThumbFor(VariantDraft vd) =>
        IsCustom && EffectiveCustomImage(vd.Id) is { } eff ? _bridge.ToBitmap(eff) : RenderThumb(vd.Map);

    private Bitmap RenderThumb(ValueMap map)
    {
        using var img = map.ToImage();
        return _ing.Manifest.Colorization is null
            ? _bridge.ToBitmap(img)
            : VariantImagery.RenderWith(_bridge, img, Mode == LayerKind.Dynamic,
                HueMin, HueMax, SatMin, SatMax, FixedColor, _previewSalt);
    }

    private bool CanMutateSelected() => SelectedVariant is not null;
    private bool CanDeleteVariant() => Variants.Count > 1;

    [RelayCommand]
    private void AddVariant()
    {
        var vd = _draft.AddVariant(NextVariantId(), $"Variant {_draft.Variants.Count + 1}", 1);
        _history[vd.Id] = new EditHistory();
        var ev = new EditorVariant(vd.Id, vd.Name, vd.Weight, RenderThumbFor(vd));
        Variants.Add(ev);
        SelectedVariant = ev;
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
        NotifySaveAvailability();   // a custom variant with no image yet blocks Save
    }

    [RelayCommand(CanExecute = nameof(CanMutateSelected))]
    private void DuplicateVariant()
    {
        var src = ActiveDraft!;
        var vd = _draft.DuplicateVariant(src.Id, NextVariantId(), $"{src.Name} copy");
        _history[vd.Id] = new EditHistory();
        // A custom variant's pixels live in the image set, not the (grayscale) value-map the draft
        // copied — so duplicate the effective image too, or the copy would render a grey ghost and
        // silently block Save.
        if (IsCustom && EffectiveCustomImage(src.Id) is { } srcImage)
            _importedCustom[vd.Id] = srcImage.Clone();
        var ev = new EditorVariant(vd.Id, vd.Name, vd.Weight, RenderThumbFor(vd));
        Variants.Add(ev);
        SelectedVariant = ev;
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
        NotifySaveAvailability();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteVariant))]
    private async Task DeleteVariant()
    {
        if (SelectedVariant is not { } target) return;
        var ok = await _dialogs.ShowAsync<bool>(new ConfirmDialogViewModel(_dialogs,
            "Delete variant?", $"Remove “{target.Name}” from this ingredient.", "Delete"));
        if (!ok) return;
        var idx = Variants.IndexOf(target);
        _draft.RemoveVariant(target.Id);
        _history.Remove(target.Id);
        // NextVariantId reuses the smallest free id, so a stale import would be inherited by the
        // next added variant — drop it with the variant.
        if (_importedCustom.Remove(target.Id, out var droppedImage)) droppedImage.Dispose();
        Variants.Remove(target);
        target.Thumbnail.Dispose();
        SelectedVariant = Variants.Count == 0 ? null : Variants[Math.Max(0, idx - 1)];
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
        NotifySaveAvailability();   // removing an image-less custom variant can unblock Save
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        // Guarded by CanSave; belt-and-suspenders against a bypassed CanExecute (need a save target).
        if (_looseSavePath is null && _session.SourcePath is null) return;
        IsSaving = true;
        try
        {
            var (manifest, images) = IngredientDraftExporter.Export(_draft);
            if (IsCustom)
            {
                // The exporter's images are a grayscale ValueMap round-trip of the draft — never what
                // Custom writes. Discard them and rebuild the image set from each variant's effective
                // full-colour image (this session's import, else the original), one fresh clone we own.
                foreach (var i in images.Values) i.Dispose();
                if (FirstCustomVariantWithoutImage is { } missing)
                    throw new InvalidOperationException($"Import an image for “{missing.Name}” before saving.");
                images = _draft.Variants.ToDictionary(v => v.Id,
                    v => EffectiveCustomImage(v.Id)!.Clone(), StringComparer.Ordinal);
            }

            // Loose (.igt) save: write the ingredient straight back to its own archive.
            if (_looseSavePath is string loosePath)
            {
                var tmp = loosePath + ".tmp";
                try
                {
                    await IngredientArchive.WriteAsync(tmp, manifest, images);
                    File.Move(tmp, loosePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
                    foreach (var i in images.Values) i.Dispose();   // our copies — ours to free
                }
                IsDirty = false;
                return;   // loose has no cookbook/session/Explorer to refresh
            }

            // Splice a loaded ingredient over the draft's export into the live book (we own its images
            // until Upsert adopts them), then persist (temp write → atomic move → rehash → Replace).
            var newIng = new LoadedIngredient { Manifest = manifest, VariantImages = images };
            var book2 = CookBookEdits.UpsertIngredient(_session.Current!, _recipe.Manifest.Id, newIng);

            var replaced = _ing;
            var book3 = await CookBookPersistence.PersistAsync(_session, book2);
            _ing = newIng;                                     // subsequent saves target the new ingredient
            foreach (var img in replaced.VariantImages.Values) img.Dispose();   // free the orphaned images

            IsDirty = false;
            Saved?.Invoke(book3);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not save", ex.Message));
        }
        finally { IsSaving = false; }
    }

    [RelayCommand]
    private async Task Back()
    {
        if (IsDirty)
        {
            var ok = await _dialogs.ShowAsync<bool>(
                new ConfirmDialogViewModel(_dialogs, "Discard edits?",
                    "You have unsaved changes to this ingredient.", "Discard"));
            if (!ok) return;
        }
        _nav.Back();
    }

    [RelayCommand] private void RerollPreview() { _previewSalt++; RebuildSurfaces(); }

    // Preview presentation state (no effect on the draft or the rendered bitmaps). Both buttons are
    // toggles: the mockup gives them no separate "restore" affordance, so each undoes itself.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewHeight))]
    private bool _previewEnlarged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPaintCanvas))]
    private bool _previewFillsPane;

    /// <summary>Height of the colorize-rail preview: normal inset, or enlarged in place.</summary>
    public double PreviewHeight => PreviewEnlarged ? 320 : 120;

    /// <summary>The canvas pane shows the paint canvas unless the preview has taken it over.</summary>
    public bool ShowPaintCanvas => !PreviewFillsPane;

    [RelayCommand] private void EnlargePreview() => PreviewEnlarged = !PreviewEnlarged;
    [RelayCommand] private void FillPanePreview() => PreviewFillsPane = !PreviewFillsPane;

    public void Dispose()
    {
        Saved = null;   // release any subscriber (e.g. the Explorer) when the editor is navigated away
        foreach (var v in Variants) v.Thumbnail.Dispose();
        foreach (var i in _importedCustom.Values) i.Dispose();
        Canvas?.Dispose(); Preview?.Dispose();
        _ownedBook?.Dispose();   // loose path only: free the synthetic wrapper book (→ the ingredient)
    }
}
