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
    private LoadedIngredient _ing;
    private readonly IngredientDraft _draft;
    private readonly Dictionary<string, EditHistory> _history = new(StringComparer.Ordinal);

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
    private bool _isSaving;

    /// <summary>Raised after a successful Save with the newly-spliced graph, so a listener
    /// (Explorer) can rebuild its tree in place instead of reloading the whole archive.</summary>
    public event Action<LoadedCookBook>? Saved;

    /// <summary>Save is offered only for edited dynamic/static ingredients with a known source file.
    /// Custom (full-colour) layers are blocked: the draft is a grayscale value-map and would overwrite
    /// their colour PNGs.</summary>
    public bool CanSave => IsDirty && _session.SourcePath is not null
        && _ing.Manifest.Kind != LayerKind.Custom && !IsSaving;

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

    public IngredientEditorViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, INavigationService nav, INotYetWired notify, ICookBookSession session,
        IDialogService dialogs)
    {
        _ing = ing; _bridge = bridge; _nav = nav; _notify = notify;
        _recipe = recipe; _session = session; _dialogs = dialogs;

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
    // Custom (Colorization is null) layers are reduced to value/alpha by ValueMap.FromImage — a known
    // limitation (spec §6): full-colour custom painting is out of scope for this slice.
    private Bitmap RenderCanvas()
    {
        using var img = ActiveMap!.ToImage();
        return _bridge.ToBitmap(img);
    }

    private Bitmap RenderPreview()
    {
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

    partial void OnSelectedVariantChanged(EditorVariant? value)
    {
        RebuildSurfaces();
        UndoCommand?.NotifyCanExecuteChanged();
        RedoCommand?.NotifyCanExecuteChanged();
        DuplicateVariantCommand?.NotifyCanExecuteChanged();
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

    partial void OnHueMinChanged(double value) => RebuildSurfaces();
    partial void OnHueMaxChanged(double value) => RebuildSurfaces();
    partial void OnSatMinChanged(double value) => RebuildSurfaces();
    partial void OnSatMaxChanged(double value) => RebuildSurfaces();
    partial void OnFixedColorChanged(string value) => RebuildSurfaces();

    [RelayCommand] private void SelectTool(EditorTool tool) => ActiveTool = tool;

    /// <summary>Commit one completed gesture as a Core edit command against the active variant.</summary>
    public void ApplyToolStroke(IReadOnlyList<(int x, int y)> points)
    {
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
        var ev = new EditorVariant(vd.Id, vd.Name, vd.Weight, RenderThumb(vd.Map));
        Variants.Add(ev);
        SelectedVariant = ev;
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMutateSelected))]
    private void DuplicateVariant()
    {
        var src = ActiveDraft!;
        var vd = _draft.DuplicateVariant(src.Id, NextVariantId(), $"{src.Name} copy");
        _history[vd.Id] = new EditHistory();
        var ev = new EditorVariant(vd.Id, vd.Name, vd.Weight, RenderThumb(vd.Map));
        Variants.Add(ev);
        SelectedVariant = ev;
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
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
        Variants.Remove(target);
        target.Thumbnail.Dispose();
        SelectedVariant = Variants.Count == 0 ? null : Variants[Math.Max(0, idx - 1)];
        IsDirty = true;
        DeleteVariantCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        // Guarded by CanSave; belt-and-suspenders against a bypassed CanExecute (never export a
        // grayscale value-map over a Custom layer's full-colour PNGs).
        if (_session.SourcePath is not string dest || _ing.Manifest.Kind == LayerKind.Custom) return;
        IsSaving = true;
        var tmp = dest + ".tmp";
        try
        {
            // 1. Export the draft → a loaded ingredient (we own the images until Upsert adopts them).
            var (manifest, images) = IngredientDraftExporter.Export(_draft);
            var newIng = new LoadedIngredient { Manifest = manifest, VariantImages = images };

            // 2. Splice into the live book.
            var book2 = CookBookEdits.UpsertIngredient(_session.Current!, _recipe.Manifest.Id, newIng);

            // 3. Crash-safe write: sibling temp, then atomic replace.
            await CookBookArchive.WriteAsync(tmp, book2.Manifest, book2.Recipes);
            File.Move(tmp, dest, overwrite: true);

            // 4. Recompute the archive hash in-app (mirrors ArchiveIo.HashFile exactly).
            string sha;
            using (var s = File.OpenRead(dest)) sha = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
            // LoadedCookBook is a class with init-only props (not a record) — construct explicitly.
            var book3 = new LoadedCookBook { Manifest = book2.Manifest, Recipes = book2.Recipes, SourceSha256 = sha };

            // 5. Swap the in-memory graph (no dispose — book3 shares the other ingredients' images),
            //    then free the replaced ingredient's now-orphaned images exactly once.
            var replaced = _ing;
            _session.Replace(book3);
            _ing = newIng;                                     // subsequent saves target the new ingredient
            foreach (var img in replaced.VariantImages.Values) img.Dispose();

            IsDirty = false;
            Saved?.Invoke(book3);
        }
        catch (Exception ex)
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
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
    [RelayCommand] private void EnlargePreview() => _notify.Report("Enlarge preview");
    [RelayCommand] private void FillPanePreview() => _notify.Report("Fill pane");

    public void Dispose()
    {
        Saved = null;   // release any subscriber (e.g. the Explorer) when the editor is navigated away
        foreach (var v in Variants) v.Thumbnail.Dispose();
        Canvas?.Dispose(); Preview?.Dispose();
    }
}
