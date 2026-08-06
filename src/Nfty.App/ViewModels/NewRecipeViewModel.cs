using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Where a new Recipe is written.</summary>
public enum RecipeDestination
{
    /// <summary>Added to the open CookBook, joining its weighted recipes.</summary>
    IntoCookBook,

    /// <summary>Saved as a loose <c>.rcp</c>, into the open Kitchen when there is one.</summary>
    LooseKitchen,
}

/// <summary>One row of the "Resulting mix" readout: a Recipe and the share of the collection its
/// weight buys once the book normalises every weight.</summary>
public record ShareRow(string Name, double Percent, bool IsCurrent);

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

    /// <summary>The recipes already in the open CookBook, with their raw weights. Empty when the
    /// wizard is opened from the Landing view, where no book is open to weigh against.</summary>
    private readonly IReadOnlyList<(string Name, double Weight)> _siblings;

    /// <summary>Creates the New Recipe wizard.</summary>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="notify">The not-yet-wired channel.</param>
    /// <param name="siblings">The open book's recipes and weights, so the form can show what
    /// the new one dilutes; null when creating a loose Recipe with no book to join.</param>
    public NewRecipeViewModel(IDialogService dialogs, INotYetWired notify,
        IReadOnlyList<(string Name, double Weight)>? siblings = null) : base(dialogs, notify)
    {
        _siblings = siblings ?? Array.Empty<(string, double)>();
    }

    /// <summary>The mockup's .share readout. A weight is meaningless on its own — it is RELATIVE to
    /// its siblings and the book normalises the set, so the number the user actually cares about is
    /// the share it buys. The control this replaces was a ProgressBar bound Value=Weight Maximum=100,
    /// which rendered a weight of 100 as a full bar and so read as "100% of the collection" no matter
    /// what the siblings weighed.</summary>
    public IReadOnlyList<ShareRow> ShareRows
    {
        get
        {
            var total = Weight + _siblings.Sum(s => s.Weight);
            if (total <= 0) return Array.Empty<ShareRow>();

            var mine = new ShareRow(
                string.IsNullOrWhiteSpace(Name) ? "This recipe" : Name,
                Weight / total * 100, true);
            return new[] { mine }
                .Concat(_siblings.Select(s => new ShareRow(s.Name, s.Weight / total * 100, false)))
                .ToList();
        }
    }

    /// <summary>Hidden for a loose Recipe: there is no collection for it to be a share OF.</summary>
    public bool ShowShare => WeightEnabled && _siblings.Count > 0;

    partial void OnWeightChanged(double value) => OnPropertyChanged(nameof(ShareRows));

    partial void OnDestinationChanged(RecipeDestination value)
    {
        OnPropertyChanged(nameof(WeightEnabled));
        OnPropertyChanged(nameof(ShowShare));
        OnPropertyChanged(nameof(IsIntoCookBook));
        OnPropertyChanged(nameof(IsLooseKitchen));
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DerivedId));
        OnPropertyChanged(nameof(ShareRows));   // the current row is labelled with the name
        CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The recipe id derived from the name: lower-case, spaces to dashes.</summary>
    public string DerivedId => string.Join('-',
        Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private bool CanCreate() => !string.IsNullOrWhiteSpace(DerivedId);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => Dialogs.Close(this);
}
