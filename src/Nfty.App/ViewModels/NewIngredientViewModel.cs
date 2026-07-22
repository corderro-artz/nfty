using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

/// <summary>Phase-1 New Ingredient wizard: collects the fields an Ingredient manifest needs (name,
/// kind, colorization config, and where it's saved). Create is a stub — Phase 2 builds the manifest
/// and writes the .igt.</summary>
public partial class NewIngredientViewModel : WizardViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private LayerKind _kind = LayerKind.Dynamic;
    [ObservableProperty] private RecipeDestination _destination = RecipeDestination.IntoCookBook;
    [ObservableProperty] private double _hueMin, _hueMax = 360, _satMin = 40, _satMax = 100;
    [ObservableProperty] private string _fixedColor = "hex:d6249f";

    /// <summary>Dynamic layers roll a colour per asset from a hue/sat range.</summary>
    public bool ShowColourRange => Kind == LayerKind.Dynamic;

    /// <summary>Static layers apply one fixed colour deterministically.</summary>
    public bool ShowFixedColour => Kind == LayerKind.Static;

    /// <summary>A loose Ingredient saved on its own has no CookBook canvas to inherit, so it needs
    /// its own canvas size.</summary>
    public bool ShowCanvas => Destination == RecipeDestination.LooseKitchen;

    /// <summary>Backs the "Dynamic" radio button.</summary>
    public bool IsKindDynamic
    {
        get => Kind == LayerKind.Dynamic;
        set { if (value) Kind = LayerKind.Dynamic; }
    }

    /// <summary>Backs the "Static" radio button.</summary>
    public bool IsKindStatic
    {
        get => Kind == LayerKind.Static;
        set { if (value) Kind = LayerKind.Static; }
    }

    /// <summary>Backs the "Custom" radio button.</summary>
    public bool IsKindCustom
    {
        get => Kind == LayerKind.Custom;
        set { if (value) Kind = LayerKind.Custom; }
    }

    /// <summary>Backs the "Into CookBook" radio button.</summary>
    public bool IsIntoCookBook
    {
        get => Destination == RecipeDestination.IntoCookBook;
        set { if (value) Destination = RecipeDestination.IntoCookBook; }
    }

    /// <summary>Backs the "Loose (Kitchen)" radio button.</summary>
    public bool IsLooseKitchen
    {
        get => Destination == RecipeDestination.LooseKitchen;
        set { if (value) Destination = RecipeDestination.LooseKitchen; }
    }

    public NewIngredientViewModel(IDialogService dialogs, INotYetWired notify) : base(dialogs, notify) { }

    partial void OnKindChanged(LayerKind value)
    {
        OnPropertyChanged(nameof(ShowColourRange));
        OnPropertyChanged(nameof(ShowFixedColour));
        OnPropertyChanged(nameof(IsKindDynamic));
        OnPropertyChanged(nameof(IsKindStatic));
        OnPropertyChanged(nameof(IsKindCustom));
    }

    partial void OnDestinationChanged(RecipeDestination value)
    {
        OnPropertyChanged(nameof(ShowCanvas));
        OnPropertyChanged(nameof(IsIntoCookBook));
        OnPropertyChanged(nameof(IsLooseKitchen));
    }

    [RelayCommand] private void Create() { Notify.Report("Create Ingredient"); Dialogs.Close(null); }
}
