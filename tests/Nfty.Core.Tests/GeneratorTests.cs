using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class GeneratorTests
{
    // A static ingredient with N variants, each a distinct solid fill.
    private static LoadedIngredient Ing(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Static, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255))),
    };

    private static LoadedRecipe Recipe(string id, params LoadedIngredient[] ings) => new()
    {
        Manifest = new RecipeManifest(id, id, ings.Select(i => i.Manifest.Id).ToList(),
            Array.Empty<IncompatibilityRule>()),
        Ingredients = ings,
    };

    // One recipe "cat": 2 bg variants x 2 body variants = 4 unique combos.
    private static LoadedCookBook OneRecipeBook() => new()
    {
        Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[] { Recipe("cat", Ing("bg", "a", "b"), Ing("body", "x", "y")) },
    };

    // Two recipes with skewed weights, each a single fixed combo (1 unique each).
    private static LoadedCookBook TwoRecipeBook() => new()
    {
        Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"),
            new Dictionary<string, double> { ["cat"] = 80, ["robot"] = 20 }),
        Recipes = new[]
        {
            Recipe("cat", Ing("bg", "a"), Ing("body", "x")),
            Recipe("robot", Ing("bg", "a"), Ing("body", "x")),
        },
    };

    [Fact]
    public void Same_seed_reproduces_identical_dna_sequence()
    {
        var opts = new GenerateOptions(3, "seed-1");
        var a = Generator.Generate(OneRecipeBook(), opts).Assets.Select(x => x.Dna);
        var b = Generator.Generate(OneRecipeBook(), opts).Assets.Select(x => x.Dna);
        Assert.Equal(a, b);
    }

    [Fact]
    public void All_dna_unique()
    {
        var set = Generator.Generate(OneRecipeBook(), new GenerateOptions(4, "seed-1"));
        Assert.Equal(4, set.Assets.Select(x => x.Dna).Distinct().Count());
    }

    [Fact]
    public void Exhausted_space_throws() =>
        Assert.Throws<InvalidOperationException>(
            () => Generator.Generate(OneRecipeBook(), new GenerateOptions(5, "seed-1")));

    [Fact]
    public void Numbering_is_sequential_from_start()
    {
        var set = Generator.Generate(OneRecipeBook(), new GenerateOptions(3, "seed-1"));
        Assert.Equal(new[] { 1, 2, 3 }, set.Assets.Select(a => a.SetNumber));
    }

    [Fact]
    public void Weighted_mix_draws_from_both_recipes()
    {
        // 2 unique combos total (cat and robot); generate both, expect one of each.
        var set = Generator.Generate(TwoRecipeBook(), new GenerateOptions(2, "seed-xyz"));
        var recipes = set.Assets.Select(a => a.RecipeId).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "cat", "robot" }, recipes);
    }

    [Fact]
    public void Single_recipe_mode_only_uses_that_recipe()
    {
        var set = Generator.Generate(TwoRecipeBook(), new GenerateOptions(1, "s", RecipeId: "robot"));
        Assert.Equal("robot", set.Assets.Single().RecipeId);
    }
}
