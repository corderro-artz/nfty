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

/// <summary>One row of the recipe's layer stack.</summary>
/// <param name="Index">Its position in layerOrder, 1-based, as the table numbers it.</param>
/// <param name="Id">The ingredient's id.</param>
/// <param name="Layer">Its display name.</param>
/// <param name="Kind">The layer kind, as a lower-case word.</param>
/// <param name="VariantCount">How many variants it offers.</param>
public record LayerRow(int Index, string Id, string Layer, string Kind, int VariantCount)
{
    /// <summary>Whether this layer rolls its colour per asset.</summary>
    public bool IsDynamic => Kind == "Dynamic";
    /// <summary>Whether this layer applies one fixed colour.</summary>
    public bool IsStatic => Kind == "Static";
    /// <summary>Whether this layer composites as-is.</summary>
    public bool IsCustom => Kind == "Custom";
}
/// <summary>One side of a rule, named the way the panel reads it.</summary>
/// <param name="Ingredient">The layer's display name.</param>
/// <param name="Variant">The variant's display name.</param>
public record RuleTargetRow(string Ingredient, string Variant);
/// <summary>One incompatibility rule as the Rules panel shows it.</summary>
/// <param name="IsExclude">True for an exclude rule, false for a require — which picks the glyph.</param>
/// <param name="When">The trigger.</param>
/// <param name="Targets">What it forbids or requires.</param>
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

    /// <summary>The recipe's display name.</summary>
    public string Name { get; }
    /// <summary>Its layer stack, in composite order.</summary>
    public IReadOnlyList<LayerRow> Layers { get; }
    /// <summary>Its incompatibility rules.</summary>
    public IReadOnlyList<RuleRow> Rules { get; }

    /// <summary>The hero's factor arithmetic (mockup .rfactors): one kind-tinted chip per layer,
    /// multiplied together to reach <see cref="TotalText"/>.</summary>
    public IReadOnlyList<FactorChip> Factors { get; }

    /// <summary>Product of the layers' variant counts - the combinations this recipe's art alone can
    /// make, before colour. Deliberately NOT UniqueSpace.Count: that folds in each dynamic layer's
    /// quantized colour buckets, and this line exists to explain the chips beside it, which are
    /// variant counts. The colour-inclusive figure is the cookbook detail's business.</summary>
    public string TotalText { get; }
    /// <summary>"N layers", pluralised.</summary>
    public string LayerCountText { get; }
    /// <summary>"N variants", pluralised.</summary>
    public string VariantCountText { get; }
    /// <summary>"N rules", pluralised.</summary>
    public string RuleCountText { get; }

    /// <summary>Builds the Recipe detail pane.</summary>
    /// <param name="recipe">The recipe to describe.</param>
    /// <param name="book">Its owning book, for the canvas and a sample roll.</param>
    /// <param name="bridge">Converts an ImageSharp frame to an Avalonia bitmap.</param>
    /// <param name="notify">The not-yet-wired channel.</param>
    /// <param name="openIngredient">Selects a layer in the tree when a rule row is clicked.</param>
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

        Rules = recipe.Manifest.Rules.Select(r => MapRule(r, recipe)).ToList();

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

    private static RuleRow MapRule(IncompatibilityRule rule, LoadedRecipe recipe) => new(
        rule.Type == RuleType.Exclude,
        Target(rule.When.IngredientId, rule.When.VariantId, recipe),
        rule.Targets.Select(t => Target(t.IngredientId, t.VariantId, recipe)).ToList());

    /// <summary>Resolves a rule's stored IDs to the names the mockup's rule chips show. Rules
    /// reference ids because that is what survives a rename in the archive; a chip that prints the
    /// id is only correct for books whose ids happen to equal their names, which is true of the
    /// hand-authored test fixtures and not of real art. Falls back to the id when the reference
    /// dangles, since a rule pointing at a deleted layer should still be visible rather than blank —
    /// that is exactly the state the user needs to see in order to fix it.</summary>
    private static RuleTargetRow Target(string ingredientId, string variantId, LoadedRecipe recipe)
    {
        var ing = recipe.Ingredients.FirstOrDefault(i => i.Manifest.Id == ingredientId);
        var variant = ing?.Manifest.Variants.FirstOrDefault(v => v.Id == variantId);
        // The ingredient caption is uppercased here: the mockup's .rcl carries text-transform,
        // and Avalonia has none. The variant keeps its own casing - .rcv does not transform.
        return new RuleTargetRow(
            (ing?.Manifest.Name ?? ingredientId).ToUpperInvariant(),
            variant?.Name ?? variantId);
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

    /// <summary>Frees the sample-roll bitmap.</summary>
    public void Dispose() => Hero.Dispose();
}
