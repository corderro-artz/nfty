using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;

namespace Nfty.App.ViewModels;

public record VariantRow(string Id, string Name, double Weight, double WithinPercent, double OverallPercent, Bitmap Thumbnail);

public partial class IngredientDetailViewModel : ViewModelBase, IDisposable
{
    private readonly INotYetWired _notify;
    private readonly Action _editIngredient;
    private readonly Func<bool> _isEditing;
    private readonly IImageBridge _bridge;
    private readonly LoadedIngredient _ing;
    private readonly IReadOnlyList<VariantRow> _variants;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Variants))]
    private string _sortColumn = "Variant";

    [ObservableProperty] private Bitmap _hero;

    public string Name { get; }
    public string KindText { get; }
    public string ColorwaysText { get; }
    public IReadOnlyList<Bitmap> Colorways { get; }

    /// <summary>Variant rows ordered by the active sort column: "Weight" (heaviest first) or,
    /// by default, "Variant" (name, ordinal).</summary>
    public IReadOnlyList<VariantRow> Variants => SortColumn == "Weight"
        ? _variants.OrderByDescending(v => v.Weight).ThenBy(v => v.Name, StringComparer.Ordinal).ToList()
        : _variants.OrderBy(v => v.Name, StringComparer.Ordinal).ToList();

    public IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, INotYetWired notify, Action editIngredient, Func<bool> isEditing)
    {
        _ing = ing; _bridge = bridge;
        _notify = notify; _editIngredient = editIngredient; _isEditing = isEditing;
        Name = ing.Manifest.Name;
        KindText = ing.Manifest.Kind.ToString();
        ColorwaysText = ColorwaysLabel(ing.Manifest);

        var traits = RarityCalculator.Compute(book).Traits
            .Where(t => t.RecipeId == recipe.Manifest.Id && t.IngredientId == ing.Manifest.Id)
            .ToDictionary(t => t.VariantId, StringComparer.Ordinal);

        _variants = ing.Manifest.Variants.Select(v =>
        {
            traits.TryGetValue(v.Id, out var t);
            return new VariantRow(v.Id, v.Name, v.Weight,
                Math.Round(t?.WithinRecipePercent ?? 0, 1), Math.Round(t?.OverallPercent ?? 0, 1),
                VariantImagery.Render(bridge, ing, v.Id));
        }).ToList();

        Colorways = VariantImagery.Colorways(bridge, ing);
        _hero = VariantImagery.Render(bridge, ing, ing.Manifest.Variants[0].Id);
    }

    private static string ColorwaysLabel(IngredientManifest m) => m.Kind switch
    {
        LayerKind.Dynamic => "HSV · rolled  (value ← value-map)",
        LayerKind.Static => "HSV · fixed  (value ← value-map)",
        _ => "no colorize · composited as-is",
    };

    public void RaiseCanExecuteChanged() => DeleteVariantCommand.NotifyCanExecuteChanged();

    [RelayCommand] private void SortBy(string col) => SortColumn = col;

    [RelayCommand]
    private void SelectVariant(string id)
    {
        var old = Hero;
        Hero = VariantImagery.Render(_bridge, _ing, id);
        old.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteVariant() => _notify.Report("Delete variant");
    [RelayCommand] private void JumpToRules() { /* nav within the recipe rail */ }
    [RelayCommand] private void EditIngredient() => _editIngredient();
    private bool CanEdit() => _isEditing();

    public void Dispose()
    {
        Hero.Dispose();
        foreach (var v in _variants) v.Thumbnail.Dispose();
        foreach (var b in Colorways) b.Dispose();
    }
}
