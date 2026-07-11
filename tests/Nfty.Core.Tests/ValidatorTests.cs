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

    [Fact]
    public void Empty_ingredient_variants_reported()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("empty", "Empty", LayerKind.Static, null,
                Array.Empty<Variant>()),
            VariantImages = new Dictionary<string, Image<Rgba32>>(),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("test", "Test", new[] { "empty" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["test"] = 10 }),
            Recipes = new[] { recipe },
        };
        Assert.Contains(Validator.Validate(book),
            p => p.Contains("no variants", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dangling_layerorder_reference_reported()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Static, null,
                new[] { new Variant("a", "A", 10) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("test", "Test", new[] { "unknown_ing" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["test"] = 10 }),
            Recipes = new[] { recipe },
        };
        Assert.Contains(Validator.Validate(book),
            p => p.Contains("layerorder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dangling_rule_reference_reported()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Static, null,
                new[] { new Variant("a", "A", 10) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255)),
            },
        };
        var rule = new IncompatibilityRule(
            RuleType.Exclude,
            new RuleTarget("unknown_ing", "v1"),
            new[] { new RuleTarget("bg", "a") });
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("test", "Test", new[] { "bg" }, new[] { rule }),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["test"] = 10 }),
            Recipes = new[] { recipe },
        };
        Assert.Contains(Validator.Validate(book),
            p => p.Contains("rule references unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RecipeWeights_references_unknown_recipe_reported()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Static, null,
                new[] { new Variant("a", "A", 10) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("test", "Test", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["test"] = 10, ["unknown_recipe"] = 5 }),
            Recipes = new[] { recipe },
        };
        Assert.Contains(Validator.Validate(book),
            p => p.Contains("recipeweights", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Recipe_missing_weight_reported()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Static, null,
                new[] { new Variant("a", "A", 10) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("test", "Test", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double>()),
            Recipes = new[] { recipe },
        };
        Assert.Contains(Validator.Validate(book),
            p => p.Contains("no recipe weight", StringComparison.OrdinalIgnoreCase));
    }
}
