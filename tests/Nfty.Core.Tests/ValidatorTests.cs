using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class ValidatorTests
{
    private static LoadedCookBook Book(int imgW, int imgH, double variantWeight, double recipeWeight)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Static, null,
                new[] { new Variant("a", "A", variantWeight) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(imgW, imgH, new Rgba32(0, 0, 0, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["cat"] = recipeWeight }),
            Recipes = new[] { recipe },
        };
    }

    [Fact]
    public void Valid_book_has_no_problems() => Assert.Empty(Validator.Validate(Book(4, 4, 10, 10)));

    [Fact]
    public void Wrong_dimensions_reported() =>
        Assert.Contains(Validator.Validate(Book(8, 8, 10, 10)),
            p => p.Contains("dimension", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Zero_variant_weight_reported() =>
        Assert.Contains(Validator.Validate(Book(4, 4, 0, 10)),
            p => p.Contains("weight", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Zero_recipe_weight_reported() =>
        Assert.Contains(Validator.Validate(Book(4, 4, 10, 0)),
            p => p.Contains("recipe", StringComparison.OrdinalIgnoreCase));
}
