using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

public enum EditorTool { Brush, Eraser, Rectangle, Circle, Triangle, Select, Fill }

/// <summary>A variant in the editor filmstrip, backed by a real loaded variant and its rendered
/// thumbnail.</summary>
public record EditorVariant(string Id, string Name, double Weight, Bitmap Thumbnail);

/// <summary>Ingredient Editor: the canvas/colorize/preview screen reached from an Ingredient's
/// detail pane. Wired to the real opened ingredient — the filmstrip is its actual variants with
/// rendered thumbnails. Painting, undo/redo, and variant list mutation remain stubs; canvas/live
/// preview bitmaps arrive in Task 7.</summary>
public partial class IngredientEditorViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _nav;
    private readonly INotYetWired _notify;
    private readonly IImageBridge _bridge;
    private readonly LoadedIngredient _ing;

    [ObservableProperty] private EditorTool _activeTool = EditorTool.Brush;
    [ObservableProperty] private int _brushValue = 128;
    [ObservableProperty] private LayerKind _mode;
    [ObservableProperty] private double _hueMin, _hueMax = 360, _satMin = 40, _satMax = 100;
    [ObservableProperty] private int _hueQuantize = 12, _satQuantize = 4;
    [ObservableProperty] private string _fixedColor = "hex:d6249f";
    [ObservableProperty] private EditorVariant? _selectedVariant;
    [ObservableProperty] private Bitmap _canvas = default!;
    [ObservableProperty] private Bitmap _preview = default!;
    private int _previewSalt;

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
        IImageBridge bridge, INavigationService nav, INotYetWired notify)
    {
        _ing = ing; _bridge = bridge; _nav = nav; _notify = notify;

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

    private Image<Rgba32> SelectedMap => _ing.VariantImages[(SelectedVariant ?? Variants[0]).Id];

    private Bitmap RenderCanvas() =>
        VariantImagery.RenderWith(_bridge, SelectedMap, Mode == LayerKind.Dynamic,
            HueMin, HueMax, SatMin, SatMax, FixedColor, _previewSalt);

    private Bitmap RenderPreview() => RenderCanvas();   // same source; preview is the small companion

    private void RebuildSurfaces()
    {
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

    partial void OnSelectedVariantChanged(EditorVariant? value) => RebuildSurfaces();
    partial void OnHueMinChanged(double value) => RebuildSurfaces();
    partial void OnHueMaxChanged(double value) => RebuildSurfaces();
    partial void OnSatMinChanged(double value) => RebuildSurfaces();
    partial void OnSatMaxChanged(double value) => RebuildSurfaces();
    partial void OnFixedColorChanged(string value) => RebuildSurfaces();

    [RelayCommand] private void SelectTool(EditorTool tool) => ActiveTool = tool;
    [RelayCommand] private void Undo() => _notify.Report("Undo");
    [RelayCommand] private void Redo() => _notify.Report("Redo");

    [RelayCommand]
    private void SelectVariant(EditorVariant v) => SelectedVariant = v;

    // Real draft-variant mutation (add/duplicate/delete against the ingredient's own variant
    // list) is a later editor slice; these remain notify stubs for now.
    [RelayCommand] private void AddVariant() => _notify.Report("Add variant");
    [RelayCommand] private void DuplicateVariant() => _notify.Report("Duplicate variant");
    [RelayCommand] private void DeleteVariant() => _notify.Report("Delete variant");

    [RelayCommand] private void ApplyStroke() => _notify.Report("Paint");
    [RelayCommand] private void Save() => _notify.Report("Save ingredient");
    [RelayCommand] private void Back() => _nav.Back();

    [RelayCommand] private void RerollPreview() { _previewSalt++; RebuildSurfaces(); }
    [RelayCommand] private void EnlargePreview() => _notify.Report("Enlarge preview");
    [RelayCommand] private void FillPanePreview() => _notify.Report("Fill pane");

    public void Dispose()
    {
        foreach (var v in Variants) v.Thumbnail.Dispose();
        Canvas?.Dispose(); Preview?.Dispose();
    }
}
