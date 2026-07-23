using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;

namespace Nfty.App.ViewModels;

public record VariantRow(string Name, double Weight, double WithinPercent, double OverallPercent);

public partial class IngredientDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    private readonly Action _editIngredient;
    private readonly Func<bool> _isEditing;
    private readonly IReadOnlyList<VariantRow> _variants;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Variants))]
    private string _sortColumn = "Variant";

    public string Name { get; }
    public string KindText { get; }
    public string ColorwaysText { get; }

    /// <summary>Variant rows ordered by the active sort column: "Weight" (heaviest first) or,
    /// by default, "Variant" (name, ordinal).</summary>
    public IReadOnlyList<VariantRow> Variants => SortColumn == "Weight"
        ? _variants.OrderByDescending(v => v.Weight).ThenBy(v => v.Name, StringComparer.Ordinal).ToList()
        : _variants.OrderBy(v => v.Name, StringComparer.Ordinal).ToList();

    public IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        INotYetWired notify, Action editIngredient, Func<bool> isEditing)
    {
        _notify = notify; _editIngredient = editIngredient; _isEditing = isEditing;
        Name = ing.Manifest.Name;
        KindText = ing.Manifest.Kind.ToString();
        ColorwaysText = Colorways(ing.Manifest);

        var traits = RarityCalculator.Compute(book).Traits
            .Where(t => t.RecipeId == recipe.Manifest.Id && t.IngredientId == ing.Manifest.Id)
            .ToDictionary(t => t.VariantId, StringComparer.Ordinal);

        _variants = ing.Manifest.Variants.Select(v =>
        {
            traits.TryGetValue(v.Id, out var t);
            return new VariantRow(v.Name, v.Weight,
                Math.Round(t?.WithinRecipePercent ?? 0, 1), Math.Round(t?.OverallPercent ?? 0, 1));
        }).ToList();
    }

    private static string Colorways(IngredientManifest m) => m.Kind switch
    {
        LayerKind.Dynamic => "HSV · rolled  (value ← value-map)",
        LayerKind.Static => "HSV · fixed  (value ← value-map)",
        _ => "no colorize · composited as-is",
    };

    public void RaiseCanExecuteChanged() => DeleteVariantCommand.NotifyCanExecuteChanged();

    [RelayCommand] private void SortBy(string col) => SortColumn = col;
    [RelayCommand] private void SelectVariant(string id) { /* ui-state: active variant */ }
    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteVariant() => _notify.Report("Delete variant");
    [RelayCommand] private void JumpToRules() { /* nav within the recipe rail */ }
    [RelayCommand] private void EditIngredient() => _editIngredient();
    private bool CanEdit() => _isEditing();
}
