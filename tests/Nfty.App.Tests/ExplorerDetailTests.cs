using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerDetailTests
{
    // Cook_reports_not_yet_wired moved to CookBookDetailViewModelTests, which now
    // constructs CookBookDetailViewModel with a real LoadedCookBook (Task 3).

    // Reroll_changes_the_roll_seed moved to RecipeDetailViewModelTests, which now
    // constructs RecipeDetailViewModel with a real LoadedRecipe/LoadedCookBook (Task 4).

    // Delete_variant_enabled_only_when_editing moved to IngredientDetailViewModelTests, which now
    // constructs IngredientDetailViewModel with a real LoadedIngredient/LoadedRecipe/LoadedCookBook (Task 5).

    private static (LoadedCookBook book, LoadedRecipe recipe, LoadedIngredient ing) Fixture()
    {
        LoadedIngredient Ing(string id, params (string vid, string name, double w)[] vs) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                vs.Select(v => new Variant(v.vid, v.name, v.w)).ToArray()),
            VariantImages = vs.ToDictionary(v => v.vid, _ => new Image<Rgba32>(4, 4)),
        };
        var aura = Ing("aura", ("glow", "Glow", 3), ("spark", "Spark", 1));
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
    public void Sort_sets_the_active_column()
    {
        var (book, recipe, ing) = Fixture();
        using var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNotYetWired(), () => { }, () => false);
        vm.SortByCommand.Execute("Weight");
        Assert.Equal("Weight", vm.SortColumn);
    }
}
