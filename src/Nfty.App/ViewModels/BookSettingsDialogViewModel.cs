using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

/// <summary>What the Explorer reports back after a settings save.</summary>
/// <param name="Saved">Whether the archive was written.</param>
/// <param name="Problems">Why not, when it was not: the sentences the card prints.</param>
public sealed record BookSettingsOutcome(bool Saved, IReadOnlyList<string> Problems);

/// <summary>One recipe's selection weight, as the settings card edits it.</summary>
public sealed partial class RecipeWeightRow : ObservableObject
{
    private readonly Action _changed;

    /// <summary>The recipe's id - what the weight is stored under.</summary>
    public string Id { get; }

    /// <summary>The recipe's display name.</summary>
    public string Name { get; }

    /// <summary>The weight the book holds right now, so "changed" has something to compare with.</summary>
    public double SavedWeight { get; }

    /// <summary>The weight being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShelved))]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private double _weight;

    /// <summary>This recipe's share of the mix, 0..100, among the weights as edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareText))]
    private double _sharePercent;

    /// <summary>Whether the weight is zero: the recipe stays in the book and is never rolled.</summary>
    public bool IsShelved => Weight <= 0;

    /// <summary>Whether the edited weight differs from the saved one.</summary>
    public bool IsChanged => Weight != SavedWeight;

    /// <summary>The share as the row prints it - <c>shelved</c> at zero, invariant otherwise.</summary>
    public string ShareText => IsShelved
        ? "shelved"
        : SharePercent.ToString(SharePercent >= 10 ? "0" : "0.#", CultureInfo.InvariantCulture) + "%";

    partial void OnWeightChanged(double value) => _changed();

    /// <summary>Creates a row.</summary>
    /// <param name="id">The recipe id.</param>
    /// <param name="name">The recipe name.</param>
    /// <param name="weight">The saved weight.</param>
    /// <param name="changed">Raised when the edited weight changes, so the card can recompute every share.</param>
    public RecipeWeightRow(string id, string name, double weight, Action changed)
    {
        Id = id;
        Name = name;
        SavedWeight = weight;
        _weight = weight;
        _changed = changed;
    }
}

/// <summary>One swatch of the book's palette, with the command that removes it.</summary>
public sealed class BookSwatch
{
    /// <summary>The color, in Core's terms.</summary>
    public RgbColor Rgb { get; }

    /// <summary>The color as the view paints it.</summary>
    public Avalonia.Media.Color Color { get; }

    /// <summary>The prefixed spec, for the tooltip.</summary>
    public string Spec { get; }

    /// <summary>Removes this swatch from the draft palette. Carried by the cell, like
    /// <c>PaletteSwatch.ForgetCommand</c>, so no ancestor lookup is needed from inside a template.</summary>
    public System.Windows.Input.ICommand RemoveCommand { get; }

    /// <summary>Creates a swatch cell.</summary>
    /// <param name="rgb">The color.</param>
    /// <param name="remove">Removes it.</param>
    public BookSwatch(RgbColor rgb, Action<BookSwatch> remove)
    {
        Rgb = rgb;
        Color = Avalonia.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B);
        Spec = ColorSpec.Format(rgb);
        RemoveCommand = new RelayCommand(() => remove(this));
    }
}

/// <summary>
/// The book settings card: a book's two names, symbol, description and intended supply, each
/// recipe's selection weight, and the swatches the book carries.
/// </summary>
/// <remarks>
/// <para><b>This closes the gap the "book carries two names" rule left open.</b> The CookBook card
/// shows both the book's name and the collection name it mints under, and nothing in the GUI could
/// change either.</para>
///
/// <para><b>Identity and palette cannot change what a seed produces; weights can.</b> That is why the
/// card has a reserved line, lit whenever a weight differs from the saved one, saying so: the roll's
/// cumulative table is built from the weights, so a changed weight moves which draw lands on which
/// recipe.</para>
///
/// <para><b>The lock is checked twice, and the second time is the one that counts.</b> The Save
/// button is dimmed while the book is locked, but a dimmed button is a label - a generated
/// <c>RelayCommand</c>'s <c>ExecuteAsync</c> does not consult its own <c>CanExecute</c> - so the
/// command re-asks <c>isEditing</c> itself before anything is committed.</para>
/// </remarks>
public sealed partial class BookSettingsDialogViewModel : ViewModelBase, IUnsavedWork
{
    /// <inheritdoc />
    public bool HasUnsavedWork => IsDirty;
    /// <inheritdoc />
    bool IUnsavedWork.IsBusy => IsSaving;
    /// <inheritdoc />
    public Task<bool> ConfirmLeaveAsync() => Task.FromResult(!IsDirty && !IsSaving);

    /// <summary>How many weight rows a page shows. Stated, not measured: the card is a modal and
    /// cannot scroll, so its height must be a pure function of this number.</summary>
    public const int PageSize = 4;

    /// <summary>How many palette swatches the strip draws before it says how many it is not showing.</summary>
    public const int SwatchSlots = 14;

    /// <summary>The target supply a stepper starts from when the book had none and the author
    /// switches one on.</summary>
    public const int DefaultTargetSupply = 1000;

    private readonly IDialogService _dialogs;
    private readonly IPaletteService? _palette;
    private readonly Func<bool> _isEditing;
    private readonly Func<Func<LoadedCookBook, LoadedCookBook>, Task<BookSettingsOutcome>> _commit;
    private readonly CookBookManifest _original;
    private readonly IReadOnlyList<RgbColor> _originalPalette;
    private bool _syncing;

    /// <summary>The book's own name.</summary>
    [ObservableProperty] private string _name;

    /// <summary>The name every asset is published under.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MintName))]
    private string _collectionName;

    /// <summary>The ticker-style symbol.</summary>
    [ObservableProperty] private string _symbol;

    /// <summary>The marketplace description.</summary>
    [ObservableProperty] private string _description;

    /// <summary>Whether the book commits to no supply at all. An explicit choice, never a stepper
    /// resting on zero: a target of zero is a number nothing can mint, and Validator refuses it.</summary>
    [ObservableProperty] private bool _noTarget;

    /// <summary>The intended supply while one is stated. At least one.</summary>
    [ObservableProperty] private int _targetSupply;

    /// <summary>The live "mints as" line, from the same formatter the CookBook card uses.</summary>
    public string MintName => CookBookDetailViewModel.MintNameFor(CollectionName);

    /// <summary>Every recipe's weight row, in the book's own order.</summary>
    public IReadOnlyList<RecipeWeightRow> Weights { get; }

    /// <summary>The swatches the book carries as edited.</summary>
    public ObservableCollection<BookSwatch> Swatches { get; } = new();

    /// <summary>Why the card cannot save, or what the save reported. Empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(FooterNote))]
    private string _statusLine = string.Empty;

    /// <summary>Whether <see cref="StatusLine"/> has anything to show.</summary>
    public bool HasStatus => StatusLine.Length > 0;

    /// <summary>Whether a save is in flight.</summary>
    [ObservableProperty] private bool _isSaving;

    // ---- paging --------------------------------------------------------------------------------

    /// <summary>Which page of weights is showing, zero-based.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleWeights))]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _pageIndex;

    /// <summary>The rows this page shows.</summary>
    public IReadOnlyList<RecipeWeightRow> VisibleWeights =>
        Weights.Skip(PageIndex * PageSize).Take(PageSize).ToList();

    /// <summary>"1-4 of 12", or empty for a book with no recipes.</summary>
    public string PageLabel
    {
        get
        {
            if (Weights.Count == 0) return string.Empty;
            int from = PageIndex * PageSize;
            return $"{from + 1}–{Math.Min(from + PageSize, Weights.Count)} of {Weights.Count}";
        }
    }

    /// <summary>Shows the next page of weights.</summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage() => PageIndex++;
    private bool CanGoNext() => (PageIndex + 1) * PageSize < Weights.Count;

    /// <summary>Shows the previous page of weights.</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PreviousPage() => PageIndex--;
    private bool CanGoBack() => PageIndex > 0;

    // ---- the reserved line ---------------------------------------------------------------------

    /// <summary>Whether any weight differs from the saved one.</summary>
    public bool HasWeightChange => Weights.Any(w => w.IsChanged);

    /// <summary>The sentence that line carries. Always the same words, so the card can reserve the
    /// space and toggle only the ink.</summary>
    public string WeightNote => "Changing a weight changes which assets a seed produces.";

    // ---- palette -------------------------------------------------------------------------------

    /// <summary>The first <see cref="SwatchSlots"/> swatches.</summary>
    public IReadOnlyList<BookSwatch> VisibleSwatches => Swatches.Take(SwatchSlots).ToList();

    /// <summary>"+3 not shown" when the palette is longer than the strip, else empty.</summary>
    public string SwatchMoreText => Swatches.Count > SwatchSlots
        ? $"+{Swatches.Count - SwatchSlots} not shown"
        : string.Empty;

    /// <summary>Whether the book has no swatches, so the strip can say so.</summary>
    public bool NoSwatches => Swatches.Count == 0;

    // ---- change tracking -----------------------------------------------------------------------

    /// <summary>Whether anything differs from what the book holds.</summary>
    public bool IsDirty =>
        !string.Equals(Name, _original.Name, StringComparison.Ordinal)
        || !string.Equals(CollectionName, _original.Collection.Name, StringComparison.Ordinal)
        || !string.Equals(Symbol, _original.Collection.Symbol, StringComparison.Ordinal)
        || !string.Equals(Description, _original.Collection.Description, StringComparison.Ordinal)
        || EffectiveTarget != _original.TargetSupply
        || HasWeightChange
        || PaletteChanged;

    private bool PaletteChanged =>
        !Swatches.Select(s => s.Rgb).SequenceEqual(_originalPalette);

    private int? EffectiveTarget => NoTarget ? null : Math.Max(1, TargetSupply);

    private bool NamesPresent =>
        !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(CollectionName);

    /// <summary>Whether Save should be offered: something changed, the names exist, the book can be
    /// edited and no save is running. Advisory - <see cref="Save"/> re-checks the lock itself.</summary>
    public bool CanSave => IsDirty && NamesPresent && _isEditing() && !IsSaving;

    /// <summary>Why Save is dimmed, when the reason is not obvious: the lock.</summary>
    public string LockNote => _isEditing()
        ? string.Empty
        : "Editing is locked. Unlock the book to save changes.";

    /// <summary>The one line under the weight caveat: a refusal when there is one, else the lock note,
    /// else nothing. The line's space is reserved either way.</summary>
    public string FooterNote => HasStatus ? StatusLine : LockNote;

    private void Changed(bool editedByAuthor = true)
    {
        // A refusal describes the values it was given; once they move it is a stale sentence.
        if (editedByAuthor && StatusLine.Length > 0) StatusLine = string.Empty;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasWeightChange));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnNameChanged(string value) => Changed();
    partial void OnCollectionNameChanged(string value) => Changed();
    partial void OnSymbolChanged(string value) => Changed();
    partial void OnDescriptionChanged(string value) => Changed();
    partial void OnNoTargetChanged(bool value) => Changed();
    partial void OnIsSavingChanged(bool value) => Changed(editedByAuthor: false);

    partial void OnTargetSupplyChanged(int value)
    {
        // The stepper's floor is 1 and the property enforces it too: a bound control can be
        // bypassed by the keyboard, a property cannot.
        if (value < 1) { TargetSupply = 1; return; }
        Changed();
    }

    private void WeightChanged()
    {
        if (_syncing) return;
        double total = Weights.Sum(w => Math.Max(0, w.Weight));
        foreach (var w in Weights)
            w.SharePercent = total > 0 ? Math.Max(0, w.Weight) / total * 100 : 0;
        Changed();
    }

    // ---- palette actions -----------------------------------------------------------------------

    private void RemoveSwatch(BookSwatch swatch)
    {
        Swatches.Remove(swatch);
        SwatchesChanged();
    }

    private void SwatchesChanged()
    {
        OnPropertyChanged(nameof(VisibleSwatches));
        OnPropertyChanged(nameof(SwatchMoreText));
        OnPropertyChanged(nameof(NoSwatches));
        AddEditorPaletteCommand.NotifyCanExecuteChanged();
        Changed();
    }

    private IReadOnlyList<RgbColor> EditorSwatchesToAdd()
    {
        if (_palette is null) return [];
        var have = Swatches.Select(s => s.Rgb).ToHashSet();
        return Palette.Combine(_palette.SwatchesIn(PaletteMode.Color), _palette.SwatchesIn(PaletteMode.Grayscale))
            .Where(c => !have.Contains(c)).ToArray();
    }

    /// <summary>Whether the editor's saved palette holds anything the book does not already carry.</summary>
    public bool CanAddEditorPalette => EditorSwatchesToAdd().Count > 0;

    /// <summary>Appends the editor's saved swatches (both modes) that the book lacks.</summary>
    [RelayCommand(CanExecute = nameof(CanAddEditorPalette))]
    private void AddEditorPalette()
    {
        foreach (var c in EditorSwatchesToAdd())
            Swatches.Add(new BookSwatch(c, RemoveSwatch));
        SwatchesChanged();
    }

    // ---- save ----------------------------------------------------------------------------------

    /// <summary>Saves the settings through the Explorer.</summary>
    /// <remarks>The lock is re-read HERE, before anything is committed: the button's dimming is not
    /// enforcement, and <c>ExecuteAsync</c> does not consult <c>CanExecute</c>.</remarks>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (!_isEditing())
        {
            StatusLine = LockNote;
            return;
        }
        if (IsSaving || !IsDirty || !NamesPresent) return;

        IsSaving = true;
        StatusLine = string.Empty;
        try
        {
            var outcome = await _commit(Build);
            if (outcome.Saved) { _dialogs.Close(null); return; }
            StatusLine = outcome.Problems.Count switch
            {
                0 => "The book could not be saved.",
                1 => outcome.Problems[0],
                _ => $"{outcome.Problems[0]} (+{outcome.Problems.Count - 1} more)",
            };
        }
        finally { IsSaving = false; }
    }

    /// <summary>Applies the edits to the live book. The Explorer calls this on ITS copy, so the card
    /// never writes a graph it was handed minutes ago.</summary>
    private LoadedCookBook Build(LoadedCookBook live)
    {
        var book = CookBookEdits.EditIdentity(live, Name.Trim(),
            new Collection(CollectionName.Trim(), Description ?? string.Empty, (Symbol ?? string.Empty).Trim()),
            EffectiveTarget);
        foreach (var w in Weights.Where(w => w.IsChanged))
            book = CookBookEdits.SetRecipeWeight(book, w.Id, w.Weight);
        if (PaletteChanged)
            book = CookBookEdits.SetPalette(book, Palette.ToSpecs(Swatches.Select(s => s.Rgb)));
        return book;
    }

    /// <summary>Dismisses the card without saving.</summary>
    [RelayCommand]
    private void Cancel() => _dialogs.Close(null);

    /// <summary>Creates the card over a book.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="book">The open book, read for its current values.</param>
    /// <param name="palette">The editor's saved palette, or null where there is none.</param>
    /// <param name="isEditing">Whether the book is unlocked, asked again at save time.</param>
    /// <param name="commit">Applies an edit to the live book, validates, writes and reports.</param>
    public BookSettingsDialogViewModel(IDialogService dialogs, LoadedCookBook book, IPaletteService? palette,
        Func<bool> isEditing, Func<Func<LoadedCookBook, LoadedCookBook>, Task<BookSettingsOutcome>> commit)
    {
        ArgumentNullException.ThrowIfNull(book);
        _dialogs = dialogs;
        _palette = palette;
        _isEditing = isEditing;
        _commit = commit;
        _original = book.Manifest;
        _originalPalette = Palette.FromSpecs(book.Manifest.Palette);

        _name = _original.Name;
        _collectionName = _original.Collection.Name;
        _symbol = _original.Collection.Symbol;
        _description = _original.Collection.Description;
        _noTarget = _original.TargetSupply is null;
        _targetSupply = _original.TargetSupply is { } t && t >= 1 ? t : DefaultTargetSupply;

        _syncing = true;
        Weights = book.Recipes
            .Select(r => new RecipeWeightRow(r.Manifest.Id, r.Manifest.Name,
                _original.RecipeWeights.GetValueOrDefault(r.Manifest.Id), WeightChanged))
            .ToList();
        _syncing = false;
        WeightChanged();

        foreach (var c in _originalPalette) Swatches.Add(new BookSwatch(c, RemoveSwatch));
    }
}
