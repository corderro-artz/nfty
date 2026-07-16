using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class UniqueSpaceTests
{
    private static LoadedIngredient Custom(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255))),
    };

    private static LoadedIngredient StaticIng(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Static,
            new Colorization(ColorModel.Hsv, 10, 10, new[] { new ColorEntry(1, null, "hex:d6249f") }),
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255))),
    };

    private static LoadedIngredient Dynamic(string id, ColorRange range, int hueQ, int satQ, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, hueQ, satQ, new[] { new ColorEntry(1, range, null) }),
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255))),
    };

    private static LoadedRecipe Recipe(string id, IReadOnlyList<IncompatibilityRule> rules, params LoadedIngredient[] ings) => new()
    {
        Manifest = new RecipeManifest(id, id, ings.Select(i => i.Manifest.Id).ToList(), rules),
        Ingredients = ings,
    };

    private static LoadedCookBook Book(params LoadedRecipe[] recipes) => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(2, 2),
            new Collection("B", "d", "B"),
            recipes.ToDictionary(r => r.Manifest.Id, _ => 1.0)),
        Recipes = recipes,
    };

    [Fact]
    public void Custom_layers_multiply_variant_counts()
    {
        // 2 bg x 3 body = 6
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Custom("bg", "a", "b"), Custom("body", "x", "y", "z")));

        Assert.Equal(6, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void Recipes_sum_together()
    {
        var book = Book(
            Recipe("cat", Array.Empty<IncompatibilityRule>(), Custom("bg", "a", "b")),
            Recipe("robot", Array.Empty<IncompatibilityRule>(), Custom("bg", "x", "y", "z")));

        Assert.Equal(5, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void Static_layer_contributes_one_bucket_not_more()
    {
        // A static layer's colour is constant, so it adds no cross-asset uniqueness:
        // 2 bg x 2 skin variants = 4, NOT 4 x (colour buckets).
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Custom("bg", "a", "b"), StaticIng("skin", "p", "q")));

        Assert.Equal(4, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void Dynamic_layer_multiplies_by_quantized_colour_buckets()
    {
        // hue 0..90 at quantize 30 => buckets 0,1,2 (90 lands in bucket 3) => 4 hue buckets.
        // sat 0..0 at quantize 10 => 1 sat bucket. 1 variant x 4 = 4.
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Dynamic("aura", new ColorRange(0, 90, 0, 0), hueQ: 30, satQ: 10, "glow")));

        Assert.Equal(4, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void Exclude_rule_removes_illegal_combinations()
    {
        // 2 x 2 = 4, minus the single (fox, visor) pair = 3.
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("body", "fox"),
                new[] { new RuleTarget("hat", "visor") }),
        };
        var book = Book(Recipe("cat", rules,
            Custom("body", "fox", "cat"), Custom("hat", "visor", "cap")));

        Assert.Equal(3, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void Unsatisfiable_recipe_counts_zero()
    {
        // Requires a variant of an ingredient that has only the forbidden one.
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("body", "fox"),
                new[] { new RuleTarget("hat", "cap") }),
        };
        var book = Book(Recipe("cat", rules, Custom("body", "fox"), Custom("hat", "cap")));

        var count = UniqueSpace.Count(book);
        Assert.Equal(0, count.Total);
        Assert.True(count.IsExact);
    }

    [Fact]
    public void Huge_space_is_capped_and_reported_inexact()
    {
        // 40 hue buckets x 100 sat buckets x 40 variants across two dynamic layers
        // blows past a small cap; the count saturates and reports itself inexact.
        var many = Enumerable.Range(0, 40).Select(i => $"v{i}").ToArray();
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Dynamic("a", new ColorRange(0, 360, 0, 100), hueQ: 1, satQ: 1, many),
            Dynamic("b", new ColorRange(0, 360, 0, 100), hueQ: 1, satQ: 1, many)));

        var count = UniqueSpace.Count(book, cap: 1000);
        Assert.False(count.IsExact);
        Assert.Equal(1000, count.Total);
    }
}
