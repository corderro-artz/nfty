using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class RecipeDetailViewModelTests
{
    [Fact]
    public void Layer_table_follows_layer_order()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        var vm = new RecipeDetailViewModel(cat, book, new FakeNotYetWired(), _ => { });
        Assert.Equal(new[] { "bg", "aura" }, vm.Layers.Select(l => l.Layer));
        Assert.Equal(new[] { "bg", "aura" }, vm.Layers.Select(l => l.Id));   // Id drives OpenIngredient, not the display name
        Assert.All(vm.Layers, l => Assert.Equal(1, l.VariantCount));
        Assert.Empty(vm.Rules);   // TwoRecipeBook has no rules
    }

    [Fact]
    public void Reroll_changes_the_roll_seed_and_open_ingredient_invokes_callback()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        string? opened = null;
        var vm = new RecipeDetailViewModel(cat, book, new FakeNotYetWired(), id => opened = id);
        var before = vm.RollSeed; vm.RerollCommand.Execute(null); Assert.NotEqual(before, vm.RollSeed);
        vm.OpenIngredientCommand.Execute("aura"); Assert.Equal("aura", opened);
    }

    [Fact]
    public void Rules_render_exclude_and_require_with_their_operators()
    {
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "day"),
                new[] { new RuleTarget("aura", "none") }),
            new IncompatibilityRule(RuleType.Require, new RuleTarget("bg", "night"),
                new[] { new RuleTarget("aura", "glow") }),
        };
        LoadedIngredient Ing(string id, params string[] variantIds) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                variantIds.Select(v => new Variant(v, v, 1)).ToArray()),
            VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(4, 4)),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura" }, rules),
            Ingredients = new[] { Ing("bg", "day", "night"), Ing("aura", "none", "glow") },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };

        var vm = new RecipeDetailViewModel(recipe, book, new FakeNotYetWired(), _ => { });

        Assert.Equal(2, vm.Rules.Count);
        Assert.Contains(vm.Rules, r => r.Text.Contains("✕") && r.Text.Contains("bg:day") && r.Text.Contains("aura:none"));
        Assert.Contains(vm.Rules, r => r.Text.Contains("→") && r.Text.Contains("bg:night") && r.Text.Contains("aura:glow"));
    }
}
