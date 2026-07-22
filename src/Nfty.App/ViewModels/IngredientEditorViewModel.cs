using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

public enum EditorTool { Brush, Eraser, Rectangle, Circle, Triangle, Select, Fill }

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

    public IngredientEditorViewModel(INavigationService nav, INotYetWired notify) { _nav = nav; _notify = notify; }

    partial void OnModeChanged(LayerKind value)
    {
        OnPropertyChanged(nameof(ShowColourRange));
        OnPropertyChanged(nameof(ShowFixedColour));
        OnPropertyChanged(nameof(IsModeStatic));
        OnPropertyChanged(nameof(IsModeDynamic));
    }

    [RelayCommand] private void SelectTool(EditorTool tool) => ActiveTool = tool;
    [RelayCommand] private void Undo() { /* EditHistory in P2 */ }
    [RelayCommand] private void Redo() { /* EditHistory in P2 */ }
    [RelayCommand] private void AddVariant() { /* in-memory drafts in P2 */ }
    [RelayCommand] private void DuplicateVariant() { /* P2 */ }
    [RelayCommand] private void DeleteVariant() { /* P2 */ }
    [RelayCommand] private void ApplyStroke() => _notify.Report("Paint");
    [RelayCommand] private void RerollPreview() => _notify.Report("Preview roll");
    [RelayCommand] private void EnlargePreview() { /* ui-state P2 */ }
    [RelayCommand] private void FillPanePreview() { /* ui-state P2 */ }
    [RelayCommand] private void Save() => _notify.Report("Save ingredient");
    [RelayCommand] private void Back() => _nav.Back();
}
