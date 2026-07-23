using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientDetailViewModelTests
{
    private static (LoadedCookBook book, LoadedRecipe recipe, LoadedIngredient ing) Fixture()
    {
        LoadedIngredient Ing(string id, params (string vid, string name, double w)[] vs) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                vs.Select(v => new Variant(v.vid, v.name, v.w)).ToArray()),
            VariantImages = vs.ToDictionary(v => v.vid, _ => new Image<Rgba32>(4, 4)),
        };
        var aura = Ing("aura", ("glow", "Glow", 3), ("spark", "Spark", 1));   // 75% / 25% within
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { aura },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe, aura);
    }

    [AvaloniaFact]
    public void Variant_rows_carry_within_recipe_rarity()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
        Assert.Equal(2, vm.Variants.Count);
        var glow = vm.Variants.Single(v => v.Name == "Glow");
        Assert.Equal(75.0, glow.WithinPercent, 1);   // 3/(3+1)
    }

    [AvaloniaFact]
    public void Sorting_reorders_variants_by_the_chosen_column()
    {
        LoadedIngredient Ing(string id, params (string vid, string name, double w)[] vs) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                vs.Select(v => new Variant(v.vid, v.name, v.w)).ToArray()),
            VariantImages = vs.ToDictionary(v => v.vid, _ => new Image<Rgba32>(4, 4)),
        };
        var aura = Ing("aura", ("a", "Apple", 1), ("z", "Zephyr", 5));   // name order ≠ weight order
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { aura },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        using var vm = new IngredientDetailViewModel(aura, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);

        Assert.Equal(new[] { "Apple", "Zephyr" }, vm.Variants.Select(v => v.Name));   // default "Variant": by name
        vm.SortByCommand.Execute("Weight");
        Assert.Equal(new[] { "Zephyr", "Apple" }, vm.Variants.Select(v => v.Name));   // "Weight": heaviest first
    }

    [AvaloniaFact]
    public void Delete_variant_enabled_only_when_editing()
    {
        var (book, recipe, ing) = Fixture();
        bool editing = false;
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => editing);
        Assert.False(vm.DeleteVariantCommand.CanExecute(null));
        editing = true; vm.RaiseCanExecuteChanged();
        Assert.True(vm.DeleteVariantCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Hero_thumbnails_and_colorways_are_built()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
        Assert.NotNull(vm.Hero);
        Assert.All(vm.Variants, v => Assert.NotNull(v.Thumbnail));
        Assert.NotEmpty(vm.Colorways);
    }

    [AvaloniaFact]
    public void Zero_variant_ingredient_does_not_crash_the_detail_pane()
    {
        var aura = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                Array.Empty<Variant>()),
            VariantImages = new Dictionary<string, Image<Rgba32>>(),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { aura },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };

        using var vm = new IngredientDetailViewModel(aura, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);

        Assert.Empty(vm.Variants);
        Assert.Null(vm.Hero);
        Assert.Empty(vm.Colorways);
    }

    [AvaloniaFact]
    public void Selecting_a_variant_swaps_the_hero()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
        var first = vm.Hero;
        vm.SelectVariantCommand.Execute(ing.Manifest.Variants[^1].Id);
        Assert.NotNull(vm.Hero);   // rebuilt; old disposed internally
        Assert.NotSame(first, vm.Hero);   // each render builds a fresh Bitmap instance
    }
}
