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
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

/// <summary>The editor's drawing tools.</summary>
public enum EditorTool
{
    /// <summary>Freehand painting.</summary>
    Brush,

    /// <summary>Freehand erasing.</summary>
    Eraser,

    /// <summary>A filled rectangle.</summary>
    Rectangle,

    /// <summary>A filled ellipse.</summary>
    Circle,

    /// <summary>A filled triangle.</summary>
    Triangle,

    /// <summary>Selects a region to move.</summary>
    Select,

    /// <summary>Flood-fills the region under the pointer.</summary>
    Fill,
}

/// <summary>A variant in the editor filmstrip. Observable so rename/reweight update the bound
/// filmstrip entry in place (no collection-item replacement / selection churn).</summary>
public partial class EditorVariant : ObservableObject
{
    /// <summary>The variant's id.</summary>
    public string Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private double _weight;
    [ObservableProperty] private Bitmap _thumbnail;

    /// <summary>Drives the .vcard selected treatment. The filmstrip is an ItemsControl (not a
    /// Selector), so selection has to travel on the item itself.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Creates a row in the editor's variant strip.</summary>
    /// <param name="id">The variant's id.</param>
    /// <param name="name">Its display name.</param>
    /// <param name="weight">Its roll weight.</param>
    /// <param name="thumbnail">A rendered swatch.</param>
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
    // Not readonly: a save that adds a layer produces a new graph, and this must point at the recipe
    // in THAT graph or the reference panel keeps describing the one before it.
    private LoadedRecipe _recipe;
    private readonly ICookBookSession _session;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _picker;
    // Set → save straight to this .igt, not into a cookbook. Not readonly: saving colour art as a
    // NEW ingredient on the loose path means writing a different file, and the editor then targets it.
    private string? _looseSavePath;
    private readonly LoadedCookBook? _ownedBook;   // the synthetic wrapper book, owned only on the loose path
    private LoadedIngredient _ing;
    private readonly IngredientDraft _draft;

    // One undo stack per variant per surface. The two are separate on purpose: undoing a colour
    // stroke must not reach back into value-map edits made before the mode was switched, and they
    // hold different pixel types, so a single shared stack could not compile in the first place.
    private readonly Dictionary<string, EditHistory<GrayPixel>> _history = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditHistory<Rgba32>> _colorHistory = new(StringComparer.Ordinal);

    [ObservableProperty] private EditorTool _activeTool = EditorTool.Brush;
    [ObservableProperty] private int _brushValue = 128;
    // Set from the canvas in the constructor: a fixed 8 covers an entire 8x8 variant in one stamp,
    // so the brush arrived unusable on a small canvas and every author's first act was to shrink it.
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

    /// <summary>Raised when this editor is disposed, so a listener holding it can let go. Without it
    /// the Explorer would keep a disposed editor and call into it on the next save.</summary>
    public event Action? Closed;

    /// <summary>
    /// Re-points this editor at a freshly-spliced graph and rebuilds what it derives from it.
    /// </summary>
    /// <remarks>
    /// The reference panel lists the recipe's OTHER layers, read once when the editor opened. A
    /// colour save adds a layer to that recipe, so without this the panel goes on describing a
    /// recipe that no longer exists — missing the layer the author just made. The draft, the undo
    /// stacks and the canvas are untouched: only the surroundings changed.
    /// </remarks>
    /// <param name="book">The graph the save produced.</param>
    internal void RefreshFromBook(LoadedCookBook book)
    {
        if (book.Recipes.FirstOrDefault(r => r.Manifest.Id == _recipe.Manifest.Id) is not { } fresh) return;
        _recipe = fresh;
        DisposeStackCaches();
        BuildReferences();
        RebuildSurfaces();
    }

    /// <summary>Save is offered for any edited ingredient with a known source file.
    ///
    /// <para>There is no longer a per-variant image gate. It existed because a session-added Custom
    /// variant genuinely had no pixels anywhere; now every variant of a Custom draft carries a
    /// <see cref="ColorMap"/> from the moment it is added, so the worst case is saving a blank
    /// layer — exactly what adding an unpainted variant to a Dynamic layer has always done.</para></summary>
    public bool CanSave => IsDirty && !IsSaving
        && (_looseSavePath is not null || _session.SourcePath is not null);

    /// <summary>Whether what this editor is going to write composites as-is rather than being
    /// colorized.
    ///
    /// <para>Read from the <em>draft</em>, not from the loaded manifest. The two agree on open and
    /// diverge exactly once — when a colour save converts the draft — and from that moment the draft
    /// is the truth: asking the manifest would re-prompt for the conversion on every subsequent save
    /// and offer grayscale painting on a layer whose value-map no longer reaches an archive.</para></summary>
    private bool IsCustom => _draft.Kind == LayerKind.Custom;

    /// <summary>What Save is about to do, when that is not simply "write this layer back". Colour art
    /// can only be stored as a Custom layer, so painting a value-map layer in colour changes which
    /// ingredient Save writes — said here rather than only in the dialog that follows.</summary>
    public string? SaveNoteText => IsColorMode && !IsCustom
        ? "Colour art saves as a Custom ingredient — Save will ask whether to add a new layer or convert this one."
        : null;

    /// <summary>Re-evaluate Save's availability and its note after anything that changes either.</summary>
    private void NotifySaveAvailability()
    {
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SaveNoteText));
    }

    /// <summary>Left-hand filmstrip: the ingredient's real variants, rendered the way the cook
    /// path would (colorized for dynamic/static, raw for custom).</summary>
    public ObservableCollection<EditorVariant> Variants { get; } = new();

    /// <summary>Dynamic layers roll a colour per asset from a hue/sat range.</summary>
    public bool ShowColourRange => ShowColorizeMode && Mode == LayerKind.Dynamic;

    /// <summary>Static layers apply one fixed colour deterministically.</summary>
    public bool ShowFixedColour => ShowColorizeMode && Mode == LayerKind.Static;

    /// <summary>
    /// Whether the rail shows the colorization controls at all.
    /// </summary>
    /// <remarks>
    /// Hidden in two cases, for the same reason: the controls would describe something that is not
    /// going to happen. A <b>Custom</b> layer composites as-is and rolls nothing. And while
    /// <b>colour mode</b> is on, the rail's hue and saturation tracks are the paint colour's own two
    /// axes — leaving the range controls up beside them put two hue sliders on one rail meaning
    /// different things, which is worse than a control that is briefly out of sight. Switching back
    /// to grey brings them straight back; nothing about the colorization has changed meanwhile.
    /// </remarks>
    public bool ShowColorizeMode => !IsCustom && !IsColorMode;

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
    /// <summary>Whether the eraser is selected.</summary>
    public bool IsToolEraser => ActiveTool == EditorTool.Eraser;
    /// <summary>Whether the rectangle tool is selected.</summary>
    public bool IsToolRectangle => ActiveTool == EditorTool.Rectangle;
    /// <summary>Whether the ellipse tool is selected.</summary>
    public bool IsToolCircle => ActiveTool == EditorTool.Circle;
    /// <summary>Whether the triangle tool is selected.</summary>
    public bool IsToolTriangle => ActiveTool == EditorTool.Triangle;
    /// <summary>Whether the selection tool is active.</summary>
    public bool IsToolSelect => ActiveTool == EditorTool.Select;
    /// <summary>Whether the flood fill is selected.</summary>
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

    /// <summary>Pushes the rail's current state into the draft. Called on save rather than on every
    /// slider tick, so a drag across the hue track rebuilds one record at the end instead of one per
    /// pixel of travel.</summary>
    private void CommitColorization()
    {
        if (IsCustom) return;              // a Custom layer carries none, and Save clears it explicitly
        _draft.Kind = Mode;
        _draft.Colorization = BuildColorization();
    }

    /// <summary>Opens an ingredient for editing.</summary>
    /// <param name="ing">The layer being edited.</param>
    /// <param name="recipe">Its owning recipe.</param>
    /// <param name="book">The owning book, whose canvas every variant must match.</param>
    /// <param name="bridge">Converts an ImageSharp frame to an Avalonia bitmap.</param>
    /// <param name="nav">The page stack, for Back.</param>
    /// <param name="notify">The not-yet-wired channel.</param>
    /// <param name="session">Holds the open book, so a save can swap the edited graph in.</param>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="picker">Chooses files to import.</param>
    /// <param name="looseSavePath">Where a loose ingredient saves back to; null inside a CookBook.</param>
    /// <param name="kitchen">The open workspace, whose loose <c>.igt</c> files the reference panel can
    /// borrow. Null is normal — nothing requires a Kitchen, and the panel simply shows no scratch
    /// section.</param>
    /// <param name="palette">The app-wide saved swatches. Null falls back to a palette held entirely
    /// in memory, so a caller that never wires one — every test — cannot reach the user's real store
    /// by omission; the composition root passes the registered service.</param>
    public IngredientEditorViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, INavigationService nav, INotYetWired notify, ICookBookSession session,
        IDialogService dialogs, IFilePickerService picker, string? looseSavePath = null,
        IKitchenSession? kitchen = null, IPaletteService? palette = null)
    {
        _ing = ing; _bridge = bridge; _nav = nav; _notify = notify;
        _recipe = recipe; _session = session; _dialogs = dialogs; _picker = picker;
        _looseSavePath = looseSavePath;
        _kitchen = kitchen;
        _palette = palette ?? new PaletteService(StateStore.InMemory());
        _bookSwatches = Palette.FromSpecs(book.Manifest.Palette);
        // A loose (standalone .igt) editor owns its synthetic wrapper book — dispose it with the editor.
        if (looseSavePath is not null) _ownedBook = book;

        // A Custom ingredient's pixels ARE its colour raster; a value-map layer gets one only if the
        // author switches into colour mode. The grayscale map is built either way, so the two save
        // paths (leave the original alone / convert it) both have something to write.
        _draft = new IngredientDraft(ing.Manifest.Id, ing.Manifest.Name, ing.Manifest.Kind, ing.Manifest.Colorization,
            book.Manifest.Canvas,
            ing.Manifest.Variants.Select(v => new VariantDraft(v.Id, v.Name, v.Weight,
                ValueMap.FromImage(ing.VariantImages[v.Id]),
                ing.Manifest.Kind == LayerKind.Custom ? ColorMap.FromImage(ing.VariantImages[v.Id]) : null)));
        foreach (var v in _draft.Variants)
        {
            _history[v.Id] = new EditHistory<GrayPixel>();
            _colorHistory[v.Id] = new EditHistory<Rgba32>();
        }

        // The rail must show THIS layer's colour configuration, not the field defaults. Without this
        // the editor opened a 170-200 degree layer showing 0-360, rendered its preview from the wrong
        // range, and — since the rail now writes back — would have saved the wrong range too.
        LoadColorization(ing.Manifest.Colorization);

        // Before the first RebuildSurfaces() below: the reference rows and the pinned depth are what
        // the canvas composites against, and building them after would repaint twice on open.
        BuildReferences();

        // The palette follows the layer's kind on open: Custom is authored in colour, everything else
        // in the greys a value-map is made of. Set before the filmstrip below, whose thumbnails are
        // rendered through the mode.
        // A stamp about a thirty-second of the canvas: fine enough to draw with on a large canvas,
        // and a single pixel on a tiny one rather than the whole image.
        BrushSize = Math.Clamp(Math.Min(book.Manifest.Canvas.Width, book.Manifest.Canvas.Height) / 32, 1, 8);

        _paintMode = ing.Manifest.Kind == LayerKind.Custom ? PaletteMode.Color : PaletteMode.Grayscale;
        RebuildRamp();
        RefreshSaved();

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
    /// <summary>The active variant's colour raster, or null if it has never been widened. A plain
    /// READ: widening is the paint-mode change's job and nowhere else's, so a render can
    /// never quietly allocate a raster and mask a variant the mode change missed.</summary>
    private ColorMap? ActiveColor => ActiveDraft?.Color;
    internal byte ValueAt(int x, int y) => ActiveMap!.GetValue(x, y);            // test hook
    internal Rgba32 ColorAt(int x, int y) => ActiveDraft!.EnsureColor().Get(x, y);   // test hook

    /// <summary>The active variant's pixels as an image, taken from whichever surface the current
    /// paint mode is editing. Always a fresh image the caller owns, so both branches are freed the
    /// same way — the value-map's round-trip and the colour map's are equally allocations.</summary>
    private Image<Rgba32> RenderSubject() =>
        IsColorMode && ActiveColor is { } color ? color.ToImage() : ActiveMap!.ToImage();

    // Canvas shows the surface being painted; Preview shows what the cook would make of it. In
    // colour mode both are the same image: a Custom layer composites as-is and is never recoloured,
    // so there is no "colorized companion" to show.
    private Bitmap RenderCanvas()
    {
        EnsureStackCaches();

        // Zero references on is the default, and it must render EXACTLY what the editor drew before
        // this panel existed — not a one-layer composite that happens to look the same.
        if (_belowStack is null && _aboveStack is null)
        {
            using var plain = RenderSubject();
            return _bridge.ToBitmap(plain);
        }

        // With references on: two DrawImage calls around the surface being painted, however many
        // layers are switched on — that is what the two caches buy, and it is what makes this
        // affordable inside RebuildSurfaces (every stroke, every slider tick).
        using var subject = RenderSubject();
        var stack = new List<Image<Rgba32>>(3);
        if (_belowStack is not null) stack.Add(_belowStack);
        stack.Add(subject);
        if (_aboveStack is not null) stack.Add(_aboveStack);

        using var composed = Compositor.Composite(_draft.Canvas, stack);
        return _bridge.ToBitmap(composed);
    }

    private Bitmap RenderPreview()
    {
        using var img = RenderSubject();
        // Colour art, or a layer with no colorization at all, is shown exactly as it is stored.
        return IsColorMode || _ing.Manifest.Colorization is null
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

    // Every one of these is now part of what Save WRITES, not just of what the preview shows, so each
    // marks the draft dirty. _loadingColorization suppresses that while the ctor fills the rail from
    // the layer's own configuration — opening a layer must not make it look edited.
    partial void OnHueMinChanged(double value) { RebuildSurfaces(); OnPropertyChanged(nameof(HueRangeText)); ColorizeEdited(); }
    partial void OnHueMaxChanged(double value) { RebuildSurfaces(); OnPropertyChanged(nameof(HueRangeText)); ColorizeEdited(); }
    partial void OnSatMinChanged(double value) { RebuildSurfaces(); OnPropertyChanged(nameof(SatRangeText)); ColorizeEdited(); }
    partial void OnSatMaxChanged(double value) { RebuildSurfaces(); OnPropertyChanged(nameof(SatRangeText)); ColorizeEdited(); }
    partial void OnFixedColorChanged(string value) { RebuildSurfaces(); ColorizeEdited(); }
    partial void OnHueQuantizeChanged(int value) { OnPropertyChanged(nameof(ApproxColorsText)); ColorizeEdited(); }
    partial void OnSatQuantizeChanged(int value) { OnPropertyChanged(nameof(ApproxColorsText)); ColorizeEdited(); }

    private bool _loadingColorization;

    private void ColorizeEdited()
    {
        if (_loadingColorization || IsCustom) return;
        IsDirty = true;
    }
    // The value ramp is V in colour mode and the whole colour in grayscale mode, so a change to it
    // moves the armed colour either way.
    partial void OnBrushValueChanged(int value) => NotifyBrushChanged();

    /// <summary>
    /// Fills the colorize rail from a layer's stored configuration. Reads the first entry that
    /// carries each kind of value, matching how the detail pane and the colorways band read it.
    /// </summary>
    /// <param name="c">The layer's colorization, or null for a Custom layer (the rail is hidden).</param>
    private void LoadColorization(Colorization? c)
    {
        if (c is null) return;
        _loadingColorization = true;
        try { LoadInto(c); } finally { _loadingColorization = false; }
    }

    private void LoadInto(Colorization c)
    {
        HueQuantize = c.HueQuantize;
        SatQuantize = c.SatQuantize;
        if (c.Entries.FirstOrDefault(e => e.Range is not null)?.Range is { } range)
        {
            HueMin = range.HueMin; HueMax = range.HueMax;
            SatMin = range.SatMin; SatMax = range.SatMax;
        }
        if (c.Entries.FirstOrDefault(e => e.Fixed is not null)?.Fixed is { } fixedSpec)
            FixedColor = fixedSpec;
    }

    /// <summary>
    /// The colorization the rail currently describes, as a layer would store it.
    /// </summary>
    /// <remarks>
    /// The rail edits ONE entry, because that is all it can show: a hue/saturation range for Dynamic,
    /// a fixed colour for Static. Any further entries a hand-authored layer carries are passed
    /// through untouched rather than flattened away — the editor may only change what it can see.
    /// </remarks>
    private Colorization BuildColorization()
    {
        var edited = Mode == LayerKind.Dynamic
            ? new ColorEntry(1, new ColorRange(HueMin, HueMax, SatMin, SatMax), null)
            : new ColorEntry(1, null, FixedColor);

        var previous = _ing.Manifest.Colorization?.Entries ?? Array.Empty<ColorEntry>();
        // Replace the entry the rail was showing; keep every other one exactly as it was.
        int shown = Mode == LayerKind.Dynamic
            ? IndexOf(previous, e => e.Range is not null)
            : IndexOf(previous, e => e.Fixed is not null);

        var entries = previous.ToList();
        if (shown >= 0) entries[shown] = edited with { Weight = previous[shown].Weight };
        else entries.Insert(0, edited);

        return new Colorization(ColorModel.Hsv, HueQuantize, SatQuantize, entries);
    }

    private static int IndexOf(IReadOnlyList<ColorEntry> entries, Func<ColorEntry, bool> match)
    {
        for (int i = 0; i < entries.Count; i++) if (match(entries[i])) return i;
        return -1;
    }

    /// <summary>Live readouts beside each range control (mockup .cv), so the sliders' current span is
    /// legible without reading the handles' positions off the track.</summary>
    public string HueRangeText => $"{HueMin:0}–{HueMax:0}°";
    /// <summary>The saturation range as the panel prints it.</summary>
    public string SatRangeText => $"{SatMin:0}–{SatMax:0}%";

    /// <summary>How many distinct colours the quantize settings actually admit - the product of the
    /// two bucket counts. This is the number that decides how much of the colour space survives into
    /// DNA, so the editor states it rather than leaving the user to multiply two steppers.</summary>
    public string ApproxColorsText => $"≈ {HueQuantize * SatQuantize} colors";

    [RelayCommand] private void SelectTool(EditorTool tool) => ActiveTool = tool;

    private bool CanImport() => SelectedVariant is not null && !IsSaving;

    /// <summary>Replaces the selected variant's raster from a PNG on disk, into whichever surface the
    /// paint mode is editing. Colour mode keeps every channel; grayscale mode reduces the image to its
    /// lightness, because a <see cref="ValueMap"/> stores nothing else. Either way the matching undo
    /// history is cleared — its snapshots describe pixels that no longer exist.</summary>
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

            if (IsColorMode)
            {
                // Colour mode keeps every channel: this is the one import path that does not reduce
                // the image, and the whole reason a Custom layer exists.
                target.Color = ColorMap.FromImage(img);
                _colorHistory[target.Id] = new EditHistory<Rgba32>();   // old snapshots describe pixels that are gone
                UndoCommand.NotifyCanExecuteChanged();
                RedoCommand.NotifyCanExecuteChanged();
                IsDirty = true;
                RebuildSurfaces();
                RefreshThumbnail(target.Id);
                NotifySaveAvailability();
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
            _history[target.Id] = new EditHistory<GrayPixel>();   // old snapshots describe pixels that are gone

            // A colour raster this variant never had a stroke on is a WIDENING of the value-map that
            // was just replaced — keeping it would show the pre-import drawing the moment colour mode
            // is entered. Drop it so it widens again from what is actually there now. A raster the
            // author has painted on is their work and survives: importing a value-map is not a reason
            // to discard colour art.
            if (!_colorHistory[target.Id].CanUndo) target.Color = null;

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
        if (_draft.Variants.FirstOrDefault(v => v.Id == variantId) is not { } vd) return;
        var old = entry.Thumbnail;
        entry.Thumbnail = RenderThumbFor(vd);
        old.Dispose();
    }

    /// <summary>The command a tool builds, for whichever surface is being painted. Written once,
    /// generic over the pixel: the tools differ in geometry, not in what kind of raster they land on,
    /// and two copies of this table is exactly how a tool comes to behave differently in one mode.</summary>
    /// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
    private IEditCommand<TPixel>? BuildCommand<TPixel>(TPixel ink, IReadOnlyList<(int x, int y)> points)
        where TPixel : struct
    {
        var op = OpacityMode;
        return ActiveTool switch
        {
            EditorTool.Brush => new BrushStroke<TPixel>(new Brush<TPixel>(BrushSize, ink), points, op),
            EditorTool.Eraser => new EraseStroke<TPixel>(BrushSize, points, op),
            EditorTool.Fill => new FloodFill<TPixel>(points[0].x, points[0].y, ink, op),
            EditorTool.Rectangle => new DrawShape<TPixel>(ShapeKind.Rectangle, BoundsOf(points), ink, op),
            EditorTool.Circle => new DrawShape<TPixel>(ShapeKind.Ellipse, BoundsOf(points), ink, op),
            EditorTool.Triangle => new DrawShape<TPixel>(ShapeKind.Triangle, BoundsOf(points), ink, op),
            _ => null,   // Select — no-op this slice
        };
    }

    /// <summary>Commit one completed gesture as a Core edit command against the active variant, on
    /// whichever surface the paint mode is editing.</summary>
    /// <param name="points">The gesture's pixel path.</param>
    public void ApplyToolStroke(IReadOnlyList<(int x, int y)> points)
    {
        if (ActiveDraft is not { } target || points.Count == 0) return;

        bool changed;
        if (IsColorMode)
        {
            var cmd = BuildCommand(ColorInk, points);
            changed = cmd is not null && _colorHistory[target.Id].Do(cmd, target.EnsureColor());
        }
        else
        {
            var cmd = BuildCommand(GrayInk, points);
            changed = cmd is not null && _history[target.Id].Do(cmd, target.Map);
        }
        // A no-op edit changed nothing — don't dirty history, rebuild, or mark the ingredient dirty.
        if (!changed) return;

        IsDirty = true;
        RebuildSurfaces();
        RefreshThumbnail(target.Id);
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

    // Undo follows the mode, not the last stroke: each surface keeps its own stack, so switching to
    // colour and undoing walks back colour strokes and leaves the value-map exactly as it was.
    private bool CanUndo() => ActiveDraft is { } d
        && (IsColorMode ? _colorHistory[d.Id].CanUndo : _history[d.Id].CanUndo);
    private bool CanRedo() => ActiveDraft is { } d
        && (IsColorMode ? _colorHistory[d.Id].CanRedo : _history[d.Id].CanRedo);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        var d = ActiveDraft!;
        if (IsColorMode) _colorHistory[d.Id].Undo(d.EnsureColor());
        else _history[d.Id].Undo(d.Map);
        AfterHistoryMove(d);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        var d = ActiveDraft!;
        if (IsColorMode) _colorHistory[d.Id].Redo(d.EnsureColor());
        else _history[d.Id].Redo(d.Map);
        AfterHistoryMove(d);
    }

    private void AfterHistoryMove(VariantDraft d)
    {
        RebuildSurfaces();
        RefreshThumbnail(d.Id);
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

    /// <summary>Thumbnail for a variant that may not have a filmstrip entry yet (add/duplicate).
    /// Rendered through the same rule as the preview, so a strip entry and the canvas never disagree
    /// about what a variant looks like.</summary>
    private Bitmap RenderThumbFor(VariantDraft vd)
    {
        using var img = IsColorMode && vd.Color is { } color ? color.ToImage() : vd.Map.ToImage();
        return IsColorMode || _ing.Manifest.Colorization is null
            ? _bridge.ToBitmap(img)
            : VariantImagery.RenderWith(_bridge, img, Mode == LayerKind.Dynamic,
                HueMin, HueMax, SatMin, SatMax, FixedColor, _previewSalt);
    }

    /// <summary>Re-renders every filmstrip thumbnail — used when the mode changes, which changes what
    /// all of them show at once rather than only the one being painted.</summary>
    private void RefreshThumbnails()
    {
        foreach (var v in Variants) RefreshThumbnail(v.Id);
    }

    private bool CanMutateSelected() => SelectedVariant is not null;
    private bool CanDeleteVariant() => Variants.Count > 1;

    [RelayCommand]
    private void AddVariant()
    {
        var vd = _draft.AddVariant(NextVariantId(), $"Variant {_draft.Variants.Count + 1}", 1);
        // In colour mode the new variant has to be paintable immediately, not on the next mode change.
        if (IsColorMode) vd.EnsureColor();
        _history[vd.Id] = new EditHistory<GrayPixel>();
        _colorHistory[vd.Id] = new EditHistory<Rgba32>();
        var ev = new EditorVariant(vd.Id, vd.Name, vd.Weight, RenderThumbFor(vd));
        Variants.Add(ev);
        SelectedVariant = ev;
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
        NotifySaveAvailability();
    }

    [RelayCommand(CanExecute = nameof(CanMutateSelected))]
    private void DuplicateVariant()
    {
        var src = ActiveDraft!;
        // DuplicateVariant clones BOTH rasters when both exist, so a colour copy is real art rather
        // than the grey ghost a value-map-only copy would produce.
        var vd = _draft.DuplicateVariant(src.Id, NextVariantId(), $"{src.Name} copy");
        _history[vd.Id] = new EditHistory<GrayPixel>();
        _colorHistory[vd.Id] = new EditHistory<Rgba32>();
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
        // NextVariantId reuses the smallest free id, so a stale stack would be inherited by the next
        // added variant — drop both with the variant.
        _history.Remove(target.Id);
        _colorHistory.Remove(target.Id);
        Variants.Remove(target);
        target.Thumbnail.Dispose();
        SelectedVariant = Variants.Count == 0 ? null : Variants[Math.Max(0, idx - 1)];
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
        NotifySaveAvailability();
    }

    /// <summary>
    /// Turns the draft into the Custom ingredient colour art has to be saved as, asking first what
    /// becomes of the original. Returns false when the author backed out, in which case nothing has
    /// been changed and nothing will be written.
    /// </summary>
    private async Task<bool> ConvertToCustomAsync()
    {
        var choice = await _dialogs.ShowAsync<ColorSaveChoice>(
            new ColorSaveDialogViewModel(_dialogs, _draft.Name));
        if (choice != ColorSaveChoice.NewIngredient && choice != ColorSaveChoice.Overwrite) return false;

        if (choice == ColorSaveChoice.NewIngredient)
        {
            // A loose .igt IS one file. "Beside the original" therefore means a second file, and the
            // author picks where — asked before anything is renamed, so cancelling here leaves the
            // draft exactly as it was rather than half-converted.
            if (_looseSavePath is not null)
            {
                var chosen = await _picker.SaveFileAsync($"Save “{_draft.Name}” as a colour ingredient", ".igt");
                if (chosen is null) return false;
                _looseSavePath = chosen;
            }
            // Both must be unique among siblings: the id keys the layer, and the NAME becomes the
            // trait_type every generated item carries, where two layers sharing one merge in the
            // rarity table and ship percentages over 100.
            _draft.Id = Unique($"{_draft.Id}-color", SiblingIds());
            _draft.Name = Unique($"{_draft.Name} (colour)", SiblingNames());
        }

        _draft.Kind = LayerKind.Custom;
        _draft.Colorization = null;   // Validator.CheckKind refuses a Custom layer that carries one

        // The draft's kind drives the whole screen: the colorize rail's controls, whether grayscale
        // is still on offer, and whether Save asks this again. It changes here and nowhere else, so
        // this is the one place that has to announce it.
        OnPropertyChanged(nameof(ShowColourRange));
        OnPropertyChanged(nameof(ShowFixedColour));
        OnPropertyChanged(nameof(ShowColorizeMode));
        OnPropertyChanged(nameof(CanPaintGrayscale));
        OnPropertyChanged(nameof(SaveNoteText));
        return true;
    }

    /// <summary>The ingredient ids already taken in this recipe, read live so a layer added since the
    /// editor opened still counts.</summary>
    private HashSet<string> SiblingIds() => LiveRecipe.Ingredients
        .Select(i => i.Manifest.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>The ingredient names already taken in this recipe.</summary>
    private HashSet<string> SiblingNames() => LiveRecipe.Ingredients
        .Select(i => i.Manifest.Name).ToHashSet(StringComparer.Ordinal);

    private LoadedRecipe LiveRecipe =>
        _session.Current?.Recipes.FirstOrDefault(r => r.Manifest.Id == _recipe.Manifest.Id) ?? _recipe;

    /// <summary>The first of <c>basis</c>, <c>basis 2</c>, <c>basis 3</c>… not already taken.</summary>
    private static string Unique(string basis, ICollection<string> taken)
    {
        if (!taken.Contains(basis)) return basis;
        for (int n = 2; ; n++) { var candidate = $"{basis} {n}"; if (!taken.Contains(candidate)) return candidate; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        // Guarded by CanSave; belt-and-suspenders against a bypassed CanExecute (need a save target).
        if (_looseSavePath is null && _session.SourcePath is null) return;

        // Ask BEFORE anything is written: the answer decides whether this becomes a new ingredient
        // beside the original or replaces it, and the replacement is not recoverable.
        if (IsColorMode && !IsCustom && !await ConvertToCustomAsync()) return;

        IsSaving = true;
        try
        {
            // The colorize rail is part of the layer, not a preview toy: what it shows is what gets
            // written. (A colour save has already converted the draft to Custom, where this no-ops.)
            CommitColorization();

            // The exporter picks each variant's raster from the draft's KIND — colour for Custom,
            // value-map for everything else — so there is nothing to rebuild here.
            var (manifest, images) = IngredientDraftExporter.Export(_draft);

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

            // Only a save that REPLACED this ingredient orphans its images. Saving colour art as a
            // new ingredient leaves the original in the book — disposing its images there would
            // blank the layer it was supposed to leave alone.
            var replaced = string.Equals(_ing.Manifest.Id, manifest.Id, StringComparison.Ordinal) ? _ing : null;
            var book3 = await CookBookPersistence.PersistAsync(_session, book2);
            _ing = newIng;                                     // subsequent saves target the new ingredient
            if (replaced is not null)
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

    /// <summary>Frees every editor bitmap.</summary>
    public void Dispose()
    {
        Closed?.Invoke();   // let the Explorer drop its reference BEFORE we tear anything down
        Saved = null;   // release any subscriber (e.g. the Explorer) when the editor is navigated away
        Closed = null;
        foreach (var v in Variants) v.Thumbnail.Dispose();
        DisposeReferences();   // the two cached stacks + every Kitchen graph opened this session
        Canvas?.Dispose(); Preview?.Dispose();
        _ownedBook?.Dispose();   // loose path only: free the synthetic wrapper book (→ the ingredient)
    }
}
