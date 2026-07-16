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
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
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
            Manifest = new IngredientManifest("empty", "Empty", LayerKind.Custom, null,
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
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
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
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
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
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
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
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
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

    [Fact]
    public void Dynamic_ingredient_without_colorization_reported()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("dyn", "Dynamic", LayerKind.Dynamic, null,
                new[] { new Variant("a", "A", 10) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("test", "Test", new[] { "dyn" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["test"] = 10 }),
            Recipes = new[] { recipe },
        };
        Assert.Contains(Validator.Validate(book),
            p => p.Contains("dynamic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Colorization_entry_with_neither_fixed_nor_range_reported()
    {
        var colorization = new Colorization(ColorModel.Hsv, 8, 8,
            new[] { new ColorEntry(1, null, null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("dyn", "Dynamic", LayerKind.Dynamic, colorization,
                new[] { new Variant("a", "A", 10) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("test", "Test", new[] { "dyn" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["test"] = 10 }),
            Recipes = new[] { recipe },
        };
        Assert.Contains(Validator.Validate(book),
            p => p.Contains("exactly one of fixed or range", StringComparison.OrdinalIgnoreCase));
    }

    // Wraps a single ingredient into an otherwise-valid one-recipe cookbook.
    private static LoadedCookBook Wrap(IngredientManifest m)
    {
        var ing = new LoadedIngredient
        {
            Manifest = m,
            VariantImages = m.Variants.ToDictionary(
                v => v.Id, _ => (Image<Rgba32>)new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255))),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["test"] = 10 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("test", "Test", new[] { m.Id }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
    }

    private static Colorization Fixed(string spec) =>
        new(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, null, spec) });

    [Fact]
    public void Custom_with_colorization_reported() =>
        Assert.Contains(
            Validator.Validate(Wrap(new IngredientManifest("c", "C", LayerKind.Custom,
                Fixed("hex:ffffff"), new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("custom", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Valid_custom_with_null_colorization_has_no_problems() =>
        Assert.Empty(Validator.Validate(Wrap(new IngredientManifest("c", "C", LayerKind.Custom,
            null, new[] { new Variant("v", "v", 1) }))));

    [Fact]
    public void Static_without_colorization_reported() =>
        Assert.Contains(
            Validator.Validate(Wrap(new IngredientManifest("s", "S", LayerKind.Static,
                null, new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("static", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Static_with_range_entry_reported() =>
        Assert.Contains(
            Validator.Validate(Wrap(new IngredientManifest("s", "S", LayerKind.Static,
                new Colorization(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, new ColorRange(0, 10, 0, 10), null) }),
                new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("static", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Static_with_multiple_entries_reported() =>
        Assert.Contains(
            Validator.Validate(Wrap(new IngredientManifest("s", "S", LayerKind.Static,
                new Colorization(ColorModel.Hsv, 5, 5, new[]
                {
                    new ColorEntry(1, null, "hex:ff0000"),
                    new ColorEntry(1, null, "hex:00ff00"),
                }),
                new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("static", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Valid_static_with_single_fixed_has_no_problems() =>
        Assert.Empty(Validator.Validate(Wrap(new IngredientManifest("s", "S", LayerKind.Static,
            Fixed("hex:d6249f"), new[] { new Variant("v", "v", 1) }))));

    [Fact]
    public void Valid_dynamic_with_range_has_no_problems() =>
        Assert.Empty(Validator.Validate(Wrap(new IngredientManifest("d", "D", LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, new ColorRange(0, 10, 0, 10), null) }),
            new[] { new Variant("v", "v", 1) }))));

    // Wraps a dynamic colorization range into an otherwise-valid book.
    private static IReadOnlyList<string> ValidateRange(ColorRange range) =>
        Validator.Validate(Wrap(new IngredientManifest("d", "D", LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, range, null) }),
            new[] { new Variant("v", "v", 1) })));

    [Fact]
    public void Inverted_hue_range_reported() =>
        // The roller samples Min..Max ascending; nothing in the spec grants wrap-around,
        // so hue 350..10 is author error, not a feature.
        Assert.Contains(ValidateRange(new ColorRange(350, 10, 0, 100)),
            p => p.Contains("hueMin", StringComparison.Ordinal)
                 && p.Contains("greater than", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Inverted_sat_range_reported() =>
        Assert.Contains(ValidateRange(new ColorRange(0, 360, 80, 20)),
            p => p.Contains("satMin", StringComparison.Ordinal)
                 && p.Contains("greater than", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Hue_range_outside_axis_bounds_reported() =>
        Assert.Contains(ValidateRange(new ColorRange(-5, 400, 0, 100)),
            p => p.Contains("hue", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("0..360", StringComparison.Ordinal));

    [Fact]
    public void Sat_range_outside_axis_bounds_reported() =>
        Assert.Contains(ValidateRange(new ColorRange(0, 360, -1, 120)),
            p => p.Contains("sat", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("0..100", StringComparison.Ordinal));

    [Fact]
    public void Range_spanning_the_full_axes_has_no_problems() =>
        // The inclusive bounds are legal; only crossing them is not.
        Assert.Empty(ValidateRange(new ColorRange(0, 360, 0, 100)));
}
