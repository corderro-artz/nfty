using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

public enum EditorTool { Brush, Eraser, Rectangle, Circle, Triangle, Select, Fill }

/// <summary>Phase-1 in-memory stand-in for a variant card in the editor's filmstrip. Real variant
/// drafts (backed by <c>Nfty.Core</c> loaded variants) arrive in Phase 2.</summary>
public record EditorVariant(string Id, string Name, double Weight);

/// <summary>Phase-1 Ingredient Editor: the canvas/colorize/preview screen reached from an Ingredient's
/// detail pane. Painting, undo/redo, and variant editing are stubs — Phase 2 wires real pixel editing
/// and colorization against Nfty.Core.</summary>
public partial class IngredientEditorViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly INotYetWired _notify;

    [ObservableProperty] private EditorTool _activeTool = EditorTool.Brush;
    [ObservableProperty] private int _brushValue = 128;
    [ObservableProperty] private LayerKind _mode = LayerKind.Dynamic;
    [ObservableProperty] private double _hueMin, _hueMax = 360, _satMin = 40, _satMax = 100;
    [ObservableProperty] private int _hueQuantize = 12, _satQuantize = 4;
    [ObservableProperty] private string _fixedColor = "hex:d6249f";

    /// <summary>Left-hand filmstrip: the variants belonging to this ingredient. Phase-1 in-memory
    /// list — real persistence arrives with Phase 2 draft editing.</summary>
    public ObservableCollection<EditorVariant> Variants { get; } =
    [
        new("glow", "Glow", 1),
        new("spark", "Spark", 1),
    ];

    [ObservableProperty] private EditorVariant? _selectedVariant;

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

    public IngredientEditorViewModel(INavigationService nav, INotYetWired notify)
    { _nav = nav; _notify = notify; SelectedVariant = Variants.Count > 0 ? Variants[0] : null; }

    partial void OnModeChanged(LayerKind value)
    {
        OnPropertyChanged(nameof(ShowColourRange));
        OnPropertyChanged(nameof(ShowFixedColour));
        OnPropertyChanged(nameof(IsModeStatic));
        OnPropertyChanged(nameof(IsModeDynamic));
    }

    [RelayCommand] private void SelectTool(EditorTool tool) => ActiveTool = tool;
    [RelayCommand] private void Undo() => _notify.Report("Undo");
    [RelayCommand] private void Redo() => _notify.Report("Redo");

    [RelayCommand]
    private void SelectVariant(EditorVariant v) => SelectedVariant = v;

    [RelayCommand]
    private void AddVariant()
    {
        var added = new EditorVariant($"v{Variants.Count + 1}", "New", 1);
        Variants.Add(added);
        SelectedVariant = added;
    }

    [RelayCommand]
    private void DuplicateVariant()
    {
        if (SelectedVariant is not { } source) return;
        var copy = source with { Id = $"{source.Id}-copy{Variants.Count}" };
        Variants.Add(copy);
        SelectedVariant = copy;
    }

    [RelayCommand]
    private void DeleteVariant()
    {
        if (SelectedVariant is not { } victim) return;
        Variants.Remove(victim);
        SelectedVariant = Variants.Count > 0 ? Variants[0] : null;
    }

    [RelayCommand] private void ApplyStroke() => _notify.Report("Paint");
    [RelayCommand] private void RerollPreview() => _notify.Report("Preview roll");
    [RelayCommand] private void EnlargePreview() => _notify.Report("Enlarge preview");
    [RelayCommand] private void FillPanePreview() => _notify.Report("Fill pane");
    [RelayCommand] private void Save() => _notify.Report("Save ingredient");
    [RelayCommand] private void Back() => _nav.Back();
}
