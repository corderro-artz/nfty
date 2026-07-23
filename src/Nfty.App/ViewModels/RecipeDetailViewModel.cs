using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

public record LayerRow(int Index, string Id, string Layer, string Kind, int VariantCount);
public record RuleRow(string Text);

public partial class RecipeDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    private readonly Action<string> _openIngredient;
    [ObservableProperty] private int _rollSeed = 1;

    public string Name { get; }
    public IReadOnlyList<LayerRow> Layers { get; }
    public IReadOnlyList<RuleRow> Rules { get; }

    public RecipeDetailViewModel(LoadedRecipe recipe, LoadedCookBook book, INotYetWired notify, Action<string> openIngredient)
    {
        _notify = notify; _openIngredient = openIngredient;
        Name = recipe.Manifest.Name;

        var ingById = recipe.Ingredients.ToDictionary(i => i.Manifest.Id, StringComparer.Ordinal);
        Layers = recipe.Manifest.LayerOrder
            .Where(ingById.ContainsKey)
            .Select((id, i) => new LayerRow(i + 1, id, ingById[id].Manifest.Name,
                ingById[id].Manifest.Kind.ToString(), ingById[id].Manifest.Variants.Count))
            .ToList();

        Rules = recipe.Manifest.Rules.Select(RuleText).ToList();
    }

    private static RuleRow RuleText(IncompatibilityRule rule)
    {
        string op = rule.Type == RuleType.Exclude ? "✕ never with" : "→ always with";
        string targets = string.Join(", ", rule.Targets.Select(t => $"{t.IngredientId}:{t.VariantId}"));
        return new RuleRow($"{rule.When.IngredientId}:{rule.When.VariantId}  {op}  {targets}");
    }

    [RelayCommand] private void Reroll() => RollSeed++;
    [RelayCommand] private void OpenIngredient(string id) => _openIngredient(id);
}
