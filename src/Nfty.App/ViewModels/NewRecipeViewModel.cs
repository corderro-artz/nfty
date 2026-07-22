using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public enum RecipeDestination { IntoCookBook, LooseKitchen }

/// <summary>Phase-1 New Recipe wizard: collects the fields a Recipe manifest needs (name, weight,
/// and where it's saved). Create is a stub — Phase 2 builds the manifest and writes the .rcp.</summary>
public partial class NewRecipeViewModel : WizardViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _weight = 100;
    [ObservableProperty] private RecipeDestination _destination = RecipeDestination.IntoCookBook;

    /// <summary>Weight only matters when the Recipe joins a CookBook's weighted mix; a loose Recipe
    /// saved on its own has no collection to weigh it against.</summary>
    public bool WeightEnabled => Destination == RecipeDestination.IntoCookBook;

    /// <summary>Backs the "Into CookBook" radio button; plain bool properties keep the two-way
    /// radio binding self-contained without introducing a value converter.</summary>
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

    public NewRecipeViewModel(IDialogService dialogs, INotYetWired notify) : base(dialogs, notify) { }

    partial void OnDestinationChanged(RecipeDestination value)
    {
        OnPropertyChanged(nameof(WeightEnabled));
        OnPropertyChanged(nameof(IsIntoCookBook));
        OnPropertyChanged(nameof(IsLooseKitchen));
    }

    [RelayCommand] private void Create() { Notify.Report("Create Recipe"); Dialogs.Close(null); }
}
