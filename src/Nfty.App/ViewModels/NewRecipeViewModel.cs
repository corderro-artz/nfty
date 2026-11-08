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
/// weight buys once the book normalizes every weight.</summary>
/// <param name="Name">The displayed Recipe name.</param>
/// <param name="Percent">Its normalized share, in percent.</param>
/// <param name="IsCurrent">Whether this row represents the Recipe being created.</param>
public record ShareRow(string Name, double Percent, bool IsCurrent);

/// <summary>Collects a Recipe name, selection weight and destination. Create returns this form;
/// the caller builds the manifest and persists the archive.</summary>
public partial class NewRecipeViewModel : WizardViewModelBase
{
    /// <summary>The Recipe display name and source of its derived id.</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>Raw selection weight relative to the open book’s other Recipes.</summary>
    [ObservableProperty] private double _weight = 100;
    /// <summary>Whether the caller inserts the Recipe or saves a loose archive.</summary>
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
    /// <param name="siblings">The open book's recipes and weights, so the form can show what
    /// the new one dilutes; null when creating a loose Recipe with no book to join.</param>
    public NewRecipeViewModel(IDialogService dialogs,
        IReadOnlyList<(string Name, double Weight)>? siblings = null) : base(dialogs)
    {
        _siblings = siblings ?? Array.Empty<(string, double)>();
        RebuildShare();
    }

    /// <summary>The new Recipe followed by its siblings, normalized together from their raw weights.
    /// Recomputed when the name or weight changes; an empty/zero-total mix has no rows.</summary>
    public IReadOnlyList<ShareRow> ShareRows => _shareRows;

    private IReadOnlyList<ShareRow> _shareRows = [];
    private IReadOnlyList<ShareRow> _shownShareRows = [];

    private void RebuildShare()
    {
        // Preserve the original summation order: the new weight plus the siblings' sum.
        var total = Weight + _siblings.Sum(s => s.Weight);
        _shareRows = total <= 0 ? [] : new[]
        {
            new ShareRow(string.IsNullOrWhiteSpace(Name) ? "This recipe" : Name, Weight / total * 100, true),
        }.Concat(_siblings.Select(s => new ShareRow(s.Name, s.Weight / total * 100, false))).ToList();
        _shownShareRows = _shareRows.Count <= ShownShareRowCount ? _shareRows
            : _shareRows.Take(1).Concat(_shareRows.Skip(1).OrderByDescending(r => r.Percent)
                .Take(ShownShareRowCount - 1)).ToList();
    }

    /// <summary>How many rows of the mix legend are listed. The bar above it always carries every
    /// recipe; the list is what grows the card, so it is bounded and says how many it left out.</summary>
    public const int ShownShareRowCount = 5;

    /// <summary>The legend rows actually listed: the new recipe first, then the heaviest siblings.</summary>
    public IReadOnlyList<ShareRow> ShownShareRows => _shownShareRows;

    /// <summary>"+7 more recipes" under a capped legend; one line is always laid out, so the card is the same height with or without it.</summary>
    public string MoreSharesText
    {
        get
        {
            var more = ShareRows.Count - ShownShareRows.Count;
            return more > 0 ? $"+{more} more recipe{(more == 1 ? "" : "s")}" : "";
        }
    }

    /// <summary>Hidden for a loose Recipe: there is no collection for it to be a share OF.</summary>
    public bool ShowShare => WeightEnabled && _siblings.Count > 0;

    partial void OnWeightChanged(double value) => NotifyShare();

    private void NotifyShare()
    {
        RebuildShare();
        OnPropertyChanged(nameof(ShareRows));
        OnPropertyChanged(nameof(ShownShareRows));
        OnPropertyChanged(nameof(MoreSharesText));
    }

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
        OnPropertyChanged(nameof(DerivedIdText));
        NotifyShare();   // the current row is labeled with the name
        CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The recipe id derived from the name: lower-case, spaces to dashes.</summary>
    public string DerivedId => DeriveId(Name);

    /// <summary>The derived id as the Identifier chip prints it.</summary>
    public string DerivedIdText => IdChipText(DerivedId);

    private bool CanCreate() => !string.IsNullOrWhiteSpace(DerivedId);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => Dialogs.Close(this, this);
}
