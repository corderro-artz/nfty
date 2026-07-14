using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class RarityCalculatorTests
{
    private static LoadedIngredient Ing(string id, params (string vid, double w)[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variants.Select(v => new Variant(v.vid, v.vid, v.w)).ToList()),
        VariantImages = variants.ToDictionary(v => v.vid, _ => new Image<Rgba32>(1, 1)),
    };

    private static LoadedCookBook Book() => new()
    {
        Manifest = new CookBookManifest("cb", "B", new Dimensions(1, 1),
            new Collection("B", "", "B"),
            new Dictionary<string, double> { ["cat"] = 75, ["robot"] = 25 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("bg", ("a", 80), ("b", 20)) },
            },
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("robot", "Robot", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("bg", ("a", 50), ("b", 50)) },
            },
        },
    };

    [Fact]
    public void Recipe_percent_is_weight_over_total()
    {
        var report = RarityCalculator.Compute(Book());
        Assert.Equal(75.0, report.Recipes.Single(r => r.RecipeId == "cat").Percent);
        Assert.Equal(25.0, report.Recipes.Single(r => r.RecipeId == "robot").Percent);
    }

    [Fact]
    public void Overall_trait_percent_multiplies_recipe_and_within()
    {
        var report = RarityCalculator.Compute(Book());
        var catA = report.Traits.Single(t => t.RecipeId == "cat" && t.VariantId == "a");
        Assert.Equal(80.0, catA.WithinRecipePercent);
        Assert.Equal(60.0, catA.OverallPercent); // 75% * 80%
    }
}
