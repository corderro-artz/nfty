using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

public record LayerRow(int Index, string Id, string Layer, string Kind, int VariantCount)
{
    public bool IsDynamic => Kind == "Dynamic";
    public bool IsStatic => Kind == "Static";
    public bool IsCustom => Kind == "Custom";
}
public record RuleTargetRow(string Ingredient, string Variant);
public record RuleRow(bool IsExclude, RuleTargetRow When, IReadOnlyList<RuleTargetRow> Targets);

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

    /// <summary>The hero's factor arithmetic (mockup .rfactors): one kind-tinted chip per layer,
    /// multiplied together to reach <see cref="TotalText"/>.</summary>
    public IReadOnlyList<FactorChip> Factors { get; }

    /// <summary>Product of the layers' variant counts - the combinations this recipe's art alone can
    /// make, before colour. Deliberately NOT UniqueSpace.Count: that folds in each dynamic layer's
    /// quantized colour buckets, and this line exists to explain the chips beside it, which are
    /// variant counts. The colour-inclusive figure is the cookbook detail's business.</summary>
    public string TotalText { get; }
    public string LayerCountText { get; }
    public string VariantCountText { get; }
    public string RuleCountText { get; }

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

        Rules = recipe.Manifest.Rules.Select(MapRule).ToList();

        var ordered = recipe.Manifest.LayerOrder.Where(ingById.ContainsKey).Select(id => ingById[id]).ToList();
        Factors = ordered
            .Select((ing, idx) => new FactorChip(ing.Manifest.Name, ing.Manifest.Variants.Count,
                                                 ing.Manifest.Kind, ShowTimes: idx > 0))
            .ToList();
        // long, not int: a dozen 5-variant layers already overflows int.
        long total = ordered.Aggregate(1L, (acc, ing) => acc * Math.Max(1, ing.Manifest.Variants.Count));
        TotalText = total.ToString("N0");
        LayerCountText = Layers.Count == 1 ? "1 layer" : $"{Layers.Count} layers";
        int variants = ordered.Sum(i => i.Manifest.Variants.Count);
        VariantCountText = variants == 1 ? "1 variant" : $"{variants} variants";
        RuleCountText = Rules.Count == 1 ? "1 rule" : $"{Rules.Count} rules";

        _hero = BuildHero();
    }

    private Bitmap BuildHero()
    {
        try
        {
            var opts = new GenerateOptions(Count: 1, Seed: RollSeed.ToString(),
                RecipeId: _recipe.Manifest.Id, EnforceUniqueDna: false);
            using var asset = Generator.GenerateStreaming(_book, opts).First();
            return _bridge.ToBitmap(asset.Image);
        }
        catch (Exception)
        {
            // The book isn't generatable yet — e.g. a freshly-added empty recipe with no layers (its
            // detail is selected right after Add), or another recipe is empty (Generator validates the
            // whole book). Show a blank canvas-sized placeholder rather than crash the detail view.
            using var blank = new Image<Rgba32>(_book.Manifest.Canvas.Width, _book.Manifest.Canvas.Height);
            return _bridge.ToBitmap(blank);
        }
    }

    private static RuleRow MapRule(IncompatibilityRule rule) => new(
        rule.Type == RuleType.Exclude,
        new RuleTargetRow(rule.When.IngredientId, rule.When.VariantId),
        rule.Targets.Select(t => new RuleTargetRow(t.IngredientId, t.VariantId)).ToList());

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
