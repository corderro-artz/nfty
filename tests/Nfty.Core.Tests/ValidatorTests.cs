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

    // --- id uniqueness (finding 3) ---

    private static LoadedIngredient Ing(string id, params Variant[] variants)
    {
        // Built duplicate-tolerantly: these fixtures deliberately carry duplicate variant ids,
        // and the throw under test belongs in Validator, not in the fixture.
        var images = new Dictionary<string, Image<Rgba32>>();
        foreach (var v in variants) images[v.Id] = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255));
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, variants),
            VariantImages = images,
        };
    }

    private static LoadedCookBook BookOf(params LoadedRecipe[] recipes) => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
            new Collection("B", "", "B"), recipes.ToDictionary(r => r.Manifest.Id, _ => 1.0)),
        Recipes = recipes,
    };

    private static LoadedRecipe Rec(string id, params LoadedIngredient[] ings) => new()
    {
        Manifest = new RecipeManifest(id, id, ings.Select(i => i.Manifest.Id).Distinct().ToList(),
            Array.Empty<IncompatibilityRule>()),
        Ingredients = ings,
    };

    [Fact]
    public void Duplicate_ingredient_ids_reported_not_thrown()
    {
        // Validator must REPORT this, never throw — `nfty validate` exists to explain a broken
        // book, so crashing on one defeats its whole purpose.
        var book = BookOf(Rec("cat", Ing("bg", new Variant("a", "A", 1)), Ing("bg", new Variant("b", "B", 1))));

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("bg", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_variant_ids_reported()
    {
        var book = BookOf(Rec("cat", Ing("bg", new Variant("a", "A", 1), new Variant("a", "A2", 1))));

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("'a'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicated_variant_id_reports_its_image_problem_once()
    {
        // Duplicate ids resolve to the same image, so checking per variant ENTRY says the same
        // thing twice. The duplicate id is already reported on its own; repeating the
        // consequence just pads the list a human has to read.
        var wrongSize = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1), new Variant("a", "A2", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 0, 255)),   // canvas is 4x4
            },
        };
        var book = BookOf(Rec("cat", wrongSize));

        var dimensionProblems = Validator.Validate(book)
            .Where(p => p.Contains("dimensions", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(dimensionProblems);
    }

    [Fact]
    public void Duplicate_recipe_ids_reported()
    {
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["cat"] = 1.0 }),
            Recipes = new[]
            {
                Rec("cat", Ing("bg", new Variant("a", "A", 1))),
                Rec("cat", Ing("bg", new Variant("b", "B", 1))),
            },
        };

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("cat", StringComparison.Ordinal));
    }

    [Fact]
    public void Ingredient_missing_from_layerOrder_reported()
    {
        // Generation only rolls layerOrder, so an ingredient the archive carries but layerOrder
        // omits is dead weight the author almost certainly meant to include.
        var book = BookOf(Rec("cat", Ing("bg", new Variant("a", "A", 1))));
        var orphan = Ing("orphan", new Variant("z", "Z", 1));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = book.Recipes[0].Ingredients.Append(orphan).ToList(),
        };
        var withOrphan = new LoadedCookBook
        {
            Manifest = book.Manifest,
            Recipes = new[] { recipe },
        };

        Assert.Contains(Validator.Validate(withOrphan),
            p => p.Contains("orphan", StringComparison.Ordinal)
                 && p.Contains("layerOrder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Empty_layerOrder_reported()
    {
        // A recipe with no layers composites nothing and generates a fully-transparent asset.
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["cat"] = 1.0 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "cat", Array.Empty<string>(),
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = Array.Empty<LoadedIngredient>(),
                },
            },
        };

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("layerOrder", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_layerOrder_entry_reported()
    {
        // Generation iterates layerOrder, so a repeated entry rolls the layer twice: two trait
        // selections for one category, doubled rarity counts, and only the last roll reaching
        // the rules. A repeat has no coherent meaning, so it is rejected rather than defined.
        var ing = Ing("bg", new Variant("a", "A", 1));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "cat", new[] { "bg", "bg" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["cat"] = 1.0 }),
            Recipes = new[] { recipe },
        };

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("more than once", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("layerOrder", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("'bg'", StringComparison.Ordinal));
    }

    // --- negative weights (weights are zero-or-greater; zero shelves, negative corrupts) ---

    [Fact]
    public void Negative_variant_weight_reported()
    {
        // The roller accumulates weights in order, so a negative one walks the running total
        // backwards and steals the share of every variant after it. The total can still be
        // positive, so the zero-total check alone never sees this.
        var book = BookOf(Rec("cat", Ing("bg",
            new Variant("a", "A", 1), new Variant("b", "B", -1), new Variant("c", "C", 1))));

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("negative", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("'b'", StringComparison.Ordinal));
    }

    [Fact]
    public void Negative_recipe_weight_reported() =>
        Assert.Contains(Validator.Validate(Book(4, 4, 10, -1)),
            p => p.Contains("negative", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("recipe weight", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Negative_colorization_entry_weight_reported() =>
        Assert.Contains(
            Validator.Validate(Wrap(new IngredientManifest("d", "D", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 5, 5, new[]
                {
                    new ColorEntry(1, new ColorRange(0, 10, 0, 10), null),
                    new ColorEntry(-1, new ColorRange(20, 30, 0, 10), null),
                }),
                new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("negative weight", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Zero_weight_variant_alongside_a_rollable_one_is_not_a_problem() =>
        // Zero is how an author shelves a variant without deleting it. It is the counting code's
        // job to stop promising it, not the validator's job to outlaw it.
        Assert.Empty(Validator.Validate(BookOf(Rec("cat", Ing("bg",
            new Variant("a", "A", 1), new Variant("b", "B", 0))))));

    // --- colour spec parsing (finding 4, spec section 9) ---

    [Fact]
    public void Unprefixed_fixed_colour_reported()
    {
        // "d6249f" without a prefix must be a validation error, never guessed.
        var book = Wrap(new IngredientManifest("s", "S", LayerKind.Static,
            Fixed("d6249f"), new[] { new Variant("v", "v", 1) }));

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("prefix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unknown_colour_prefix_reported()
    {
        var book = Wrap(new IngredientManifest("s", "S", LayerKind.Static,
            Fixed("cmyk:1,2,3"), new[] { new Variant("v", "v", 1) }));

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("prefix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Malformed_fixed_colour_on_a_dynamic_entry_reported()
    {
        var book = Wrap(new IngredientManifest("d", "D", LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, null, "hex:zzz") }),
            new[] { new Variant("v", "v", 1) }));

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("hex", StringComparison.OrdinalIgnoreCase));
    }

    // --- dynamic entries (finding 5, spec sections 4.1 / 5.1) ---

    [Fact]
    public void Dynamic_with_empty_entries_reported() =>
        Assert.Contains(
            Validator.Validate(Wrap(new IngredientManifest("d", "D", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 5, 5, Array.Empty<ColorEntry>()),
                new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("at least one", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Dynamic_with_zero_total_entry_weight_reported() =>
        Assert.Contains(
            Validator.Validate(Wrap(new IngredientManifest("d", "D", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 5, 5,
                    new[] { new ColorEntry(0, new ColorRange(0, 10, 0, 10), null) }),
                new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("zero total", StringComparison.OrdinalIgnoreCase));

    // --- quantize fields (finding 2) ---

    [Fact]
    public void Zero_hueQuantize_reported() =>
        Assert.Contains(
            Validator.Validate(Wrap(new IngredientManifest("d", "D", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 0, 5, new[] { new ColorEntry(1, new ColorRange(0, 10, 0, 10), null) }),
                new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("hueQuantize", StringComparison.Ordinal));

    [Fact]
    public void Negative_satQuantize_reported() =>
        Assert.Contains(
            Validator.Validate(Wrap(new IngredientManifest("d", "D", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 5, -3, new[] { new ColorEntry(1, new ColorRange(0, 10, 0, 10), null) }),
                new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("satQuantize", StringComparison.Ordinal));

    [Fact]
    public void Positive_quantize_values_have_no_quantize_problem() =>
        Assert.DoesNotContain(
            Validator.Validate(Wrap(new IngredientManifest("d", "D", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, new ColorRange(0, 10, 0, 10), null) }),
                new[] { new Variant("v", "v", 1) }))),
            p => p.Contains("Quantize", StringComparison.Ordinal));

    // --- grayscale value-maps (Task 12) ---

    [Fact]
    public void Non_grayscale_dynamic_variant_is_reported()
    {
        // A dynamic ingredient whose only variant image has a coloured (non-grayscale) pixel.
        // Everything else about the book is valid, so the grayscale check should be the only
        // problem reported.
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("dyn", "Dynamic", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 5, 5,
                    new[] { new ColorEntry(1, new ColorRange(0, 10, 0, 10), null) }),
                new[] { new Variant("a", "A", 10) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(8, 8, new Rgba32(200, 10, 10, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("test", "Test", new[] { "dyn" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["test"] = 10 }),
            Recipes = new[] { recipe },
        };

        var problems = Validator.Validate(book);

        Assert.Contains(problems, p => p.Contains("grayscale", StringComparison.OrdinalIgnoreCase));
        Assert.Single(problems);
    }

    [Fact]
    public void Non_grayscale_custom_variant_is_allowed()
    {
        // Custom layers are full-colour RGBA composited as-is, never colorized, so the
        // Kind != Custom exemption should let a genuinely non-grayscale image through clean.
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("cus", "Custom", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 10) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(8, 8, new Rgba32(200, 10, 10, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("test", "Test", new[] { "cus" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["test"] = 10 }),
            Recipes = new[] { recipe },
        };

        var problems = Validator.Validate(book);

        Assert.DoesNotContain(problems, p => p.Contains("grayscale", StringComparison.OrdinalIgnoreCase));
    }

    // --- canvas dimensions ---

    [Fact]
    public void Non_positive_canvas_dimension_reported()
    {
        // Canvas is the single source of truth for every image size, and the GUI feeds it from
        // user input, so a zero (or negative) dimension must be named for what it is rather than
        // surfacing only as a downstream variant-size mismatch or an allocation throw.
        var ing = Ing("bg", new Variant("a", "A", 1));
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(0, 4),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["cat"] = 1.0 }),
            Recipes = new[] { Rec("cat", ing) },
        };

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("canvas", StringComparison.OrdinalIgnoreCase)
                 && p.Contains("positive", StringComparison.OrdinalIgnoreCase));
    }

    // --- per-level validation (authoring CLI) ---

    private static LoadedIngredient DynIng(string id, Colorization col, params (string id, Rgba32 fill, int w, int h)[] vs)
    {
        var images = new Dictionary<string, Image<Rgba32>>();
        foreach (var v in vs) images[v.id] = new Image<Rgba32>(v.w, v.h, v.fill);
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Dynamic, col,
                vs.Select(v => new Variant(v.id, v.id, 1)).ToArray()),
            VariantImages = images,
        };
    }

    private static Colorization Range() =>
        new(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, new ColorRange(0, 10, 0, 10), null) });

    [Fact]
    public void ValidateIngredient_passes_a_clean_grayscale_dynamic_ingredient() =>
        Assert.Empty(Validator.ValidateIngredient(
            DynIng("d", Range(), ("a", new Rgba32(120, 120, 120, 255), 4, 4))));

    [Fact]
    public void ValidateIngredient_reports_a_non_grayscale_dynamic_variant() =>
        Assert.Contains(
            Validator.ValidateIngredient(DynIng("d", Range(), ("a", new Rgba32(200, 10, 10, 255), 4, 4))),
            p => p.Contains("grayscale", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ValidateIngredient_reports_variant_images_of_differing_sizes() =>
        Assert.Contains(
            Validator.ValidateIngredient(DynIng("d", Range(),
                ("a", new Rgba32(120, 120, 120, 255), 4, 4),
                ("b", new Rgba32(120, 120, 120, 255), 8, 8))),
            p => p.Contains("differing sizes", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ValidateIngredient_reports_a_custom_layer_carrying_a_colorization() =>
        Assert.Contains(
            Validator.ValidateIngredient(new LoadedIngredient
            {
                Manifest = new IngredientManifest("c", "C", LayerKind.Custom, Range(),
                    new[] { new Variant("a", "A", 1) }),
                VariantImages = new Dictionary<string, Image<Rgba32>>
                    { ["a"] = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 255)) },
            }),
            p => p.Contains("custom", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ValidateRecipe_passes_a_clean_single_layer_recipe()
    {
        var ing = DynIng("d", Range(), ("a", new Rgba32(120, 120, 120, 255), 4, 4));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "d" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        Assert.Empty(Validator.ValidateRecipe(recipe));
    }

    [Fact]
    public void ValidateRecipe_reports_cross_ingredient_size_mismatch()
    {
        var a = DynIng("a", Range(), ("x", new Rgba32(120, 120, 120, 255), 4, 4));
        var b = DynIng("b", Range(), ("y", new Rgba32(120, 120, 120, 255), 8, 8));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "a", "b" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { a, b },
        };
        Assert.Contains(Validator.ValidateRecipe(recipe),
            p => p.Contains("differing sizes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRecipe_reports_a_dangling_rule_reference()
    {
        var ing = DynIng("d", Range(), ("a", new Rgba32(120, 120, 120, 255), 4, 4));
        var rule = new IncompatibilityRule(RuleType.Exclude,
            new RuleTarget("nope", "x"), new[] { new RuleTarget("d", "a") });
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "d" }, new[] { rule }),
            Ingredients = new[] { ing },
        };
        Assert.Contains(Validator.ValidateRecipe(recipe),
            p => p.Contains("rule references unknown", StringComparison.OrdinalIgnoreCase));
    }
}
