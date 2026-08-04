using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class RecipeDetailViewModelTests
{
    [AvaloniaFact]
    public void Layer_table_follows_layer_order()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        using var vm = new RecipeDetailViewModel(cat, book, new ImageBridge(), new FakeNotYetWired(), _ => { });
        Assert.Equal(new[] { "bg", "aura" }, vm.Layers.Select(l => l.Layer));
        Assert.Equal(new[] { "bg", "aura" }, vm.Layers.Select(l => l.Id));   // Id drives OpenIngredient, not the display name
        Assert.All(vm.Layers, l => Assert.Equal(1, l.VariantCount));
        Assert.Empty(vm.Rules);   // TwoRecipeBook has no rules
    }

    [AvaloniaFact]
    public void Reroll_changes_the_roll_seed_and_open_ingredient_invokes_callback()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        string? opened = null;
        using var vm = new RecipeDetailViewModel(cat, book, new ImageBridge(), new FakeNotYetWired(), id => opened = id);
        var before = vm.RollSeed; vm.RerollCommand.Execute(null); Assert.NotEqual(before, vm.RollSeed);
        vm.OpenIngredientCommand.Execute("aura"); Assert.Equal("aura", opened);
    }

    [AvaloniaFact]
    public void Hero_is_built_and_reroll_rebuilds_it()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        using var vm = new RecipeDetailViewModel(cat, book, new ImageBridge(), new FakeNotYetWired(), _ => { });
        Assert.NotNull(vm.Hero);
        var before = vm.RollSeed;
        vm.RerollCommand.Execute(null);
        Assert.NotEqual(before, vm.RollSeed);
        Assert.NotNull(vm.Hero);   // rebuilt; old disposed internally
    }

    [AvaloniaFact]
    public void Rules_expose_operator_and_traits()
    {
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "day"),
                new[] { new RuleTarget("aura", "none") }),
            new IncompatibilityRule(RuleType.Require, new RuleTarget("bg", "night"),
                new[] { new RuleTarget("aura", "glow") }),
        };
        // Ids and display names are deliberately DIFFERENT here. They used to be the same string
        // (IngredientManifest(id, id, ...), Variant(v, v, 1)), which meant the assertions below could
        // not tell whether the chips rendered the stored id or the resolved name — and they were in
        // fact rendering the raw id, which is only ever correct for fixtures like this one. Rules
        // reference ids because that is what survives a rename; the chips must show names.
        LoadedIngredient Ing(string id, string name, params (string Id, string Name)[] variants) => new()
        {
            Manifest = new IngredientManifest(id, name, LayerKind.Custom, null,
                variants.Select(v => new Variant(v.Id, v.Name, 1)).ToArray()),
            VariantImages = variants.ToDictionary(v => v.Id, _ => new Image<Rgba32>(4, 4)),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura" }, rules),
            Ingredients = new[]
            {
                Ing("bg", "Background", ("day", "Daylight"), ("night", "Nightfall")),
                Ing("aura", "Aura", ("none", "None"), ("glow", "Glowing")),
            },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };

        using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), new FakeNotYetWired(), _ => { });

        Assert.Equal(2, vm.Rules.Count);
        var exclude = vm.Rules.Single(r => r.IsExclude);
        var require = vm.Rules.Single(r => !r.IsExclude);

        // Names, not the "bg"/"day" ids the rules are stored against. The ingredient caption is
        // uppercased because the mockup's .rcl carries text-transform and Avalonia has none; the
        // variant keeps its own casing, as .rcv does not transform.
        Assert.Equal("BACKGROUND", exclude.When.Ingredient);
        Assert.Equal("Daylight", exclude.When.Variant);
        Assert.Contains(exclude.Targets, t => t.Ingredient == "AURA" && t.Variant == "None");

        Assert.Equal("BACKGROUND", require.When.Ingredient);
        Assert.Equal("Nightfall", require.When.Variant);
        Assert.Contains(require.Targets, t => t.Ingredient == "AURA" && t.Variant == "Glowing");

        // A rule whose target has been deleted must still be visible - that is the state the user
        // needs to see in order to fix it - so an unresolvable reference falls back to its id.
        var orphan = new IncompatibilityRule(RuleType.Exclude, new RuleTarget("gone", "missing"),
            new[] { new RuleTarget("bg", "day") });
        var orphanRecipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura" }, new[] { orphan }),
            Ingredients = recipe.Ingredients,
        };
        using var orphanVm = new RecipeDetailViewModel(orphanRecipe, book, new ImageBridge(),
            new FakeNotYetWired(), _ => { });
        Assert.Equal("GONE", orphanVm.Rules[0].When.Ingredient);
        Assert.Equal("missing", orphanVm.Rules[0].When.Variant);
    }
}
