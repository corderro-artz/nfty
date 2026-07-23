using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

public record LayerRow(int Index, string Id, string Layer, string Kind, int VariantCount);
public record RuleRow(string Text);

public partial class RecipeDetailViewModel : ViewModelBase, IDisposable
{
    private readonly INotYetWired _notify;
    private readonly Action<string> _openIngredient;
    private readonly IImageBridge _bridge;
    private readonly LoadedRecipe _recipe;
    private readonly LoadedCookBook _book;

    [ObservableProperty] private int _rollSeed = 1;
    [ObservableProperty] private Bitmap _hero;

    public string Name { get; }
    public IReadOnlyList<LayerRow> Layers { get; }
    public IReadOnlyList<RuleRow> Rules { get; }

    public RecipeDetailViewModel(LoadedRecipe recipe, LoadedCookBook book, IImageBridge bridge,
        INotYetWired notify, Action<string> openIngredient)
    {
        _recipe = recipe; _book = book; _bridge = bridge; _notify = notify; _openIngredient = openIngredient;
        Name = recipe.Manifest.Name;

        var ingById = recipe.Ingredients.ToDictionary(i => i.Manifest.Id, StringComparer.Ordinal);
        Layers = recipe.Manifest.LayerOrder
            .Where(ingById.ContainsKey)
            .Select((id, i) => new LayerRow(i + 1, id, ingById[id].Manifest.Name,
                ingById[id].Manifest.Kind.ToString(), ingById[id].Manifest.Variants.Count))
            .ToList();

        Rules = recipe.Manifest.Rules.Select(RuleText).ToList();
        _hero = BuildHero();
    }

    private Bitmap BuildHero()
    {
        var opts = new GenerateOptions(Count: 1, Seed: RollSeed.ToString(),
            RecipeId: _recipe.Manifest.Id, EnforceUniqueDna: false);
        using var asset = Generator.GenerateStreaming(_book, opts).First();
        return _bridge.ToBitmap(asset.Image);
    }

    private static RuleRow RuleText(IncompatibilityRule rule)
    {
        string op = rule.Type == RuleType.Exclude ? "✕ never with" : "→ always with";
        string targets = string.Join(", ", rule.Targets.Select(t => $"{t.IngredientId}:{t.VariantId}"));
        return new RuleRow($"{rule.When.IngredientId}:{rule.When.VariantId}  {op}  {targets}");
    }

    [RelayCommand]
    private void Reroll()
    {
        RollSeed++;
        var old = Hero;
        Hero = BuildHero();
        old.Dispose();
    }

    [RelayCommand] private void OpenIngredient(string id) => _openIngredient(id);

    public void Dispose() => Hero.Dispose();
}
