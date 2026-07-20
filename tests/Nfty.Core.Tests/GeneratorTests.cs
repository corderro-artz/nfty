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
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
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
        Assert.Throws<UniqueSpaceExhaustedException>(
            () => Generator.Generate(OneRecipeBook(), new GenerateOptions(5, "seed-1")));

    [Fact]
    public void Exhausted_space_error_states_the_true_maximum()
    {
        // OneRecipeBook allows exactly 4 unique DNA (2 bg x 2 body, both custom).
        var ex = Assert.Throws<UniqueSpaceExhaustedException>(
            () => Generator.Generate(OneRecipeBook(), new GenerateOptions(5, "seed-1")));

        Assert.Equal(4, ex.Available);
        Assert.Equal(5, ex.Requested);
        Assert.True(ex.IsExact);
        Assert.Contains("4", ex.Message);
    }

    [Fact]
    public void Exhausted_space_max_counts_only_the_chosen_recipe_in_single_recipe_mode()
    {
        // TwoRecipeBook's recipes are 1 unique each; restricting to 'robot' caps the max at 1.
        var ex = Assert.Throws<UniqueSpaceExhaustedException>(
            () => Generator.Generate(TwoRecipeBook(), new GenerateOptions(2, "s", RecipeId: "robot")));

        Assert.Equal(1, ex.Available);
    }

    [Fact]
    public void Exhausted_space_max_ignores_a_recipe_weighted_zero()
    {
        // 'robot' is weighted zero, so WeightedRoller can never return it: its 4 combinations are
        // unreachable and must not be added to the maximum this run could have produced. Only
        // 'cat' is in play, and cat allows exactly 1.
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"),
                new Dictionary<string, double> { ["cat"] = 1, ["robot"] = 0 }),
            Recipes = new[]
            {
                Recipe("cat", Ing("bg", "a"), Ing("body", "x")),
                Recipe("robot", Ing("bg", "a", "b"), Ing("body", "x", "y")),
            },
        };

        var ex = Assert.Throws<UniqueSpaceExhaustedException>(
            () => Generator.Generate(book, new GenerateOptions(2, "seed-1")));

        Assert.Equal(1, ex.Available);
        Assert.True(ex.IsExact);
    }

    [Fact]
    public void Unsatisfiable_rules_report_a_conflict_not_exhaustion()
    {
        // The only body excludes the only hat, so no combination is legal.
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("body", "fox"),
                new[] { new RuleTarget("hat", "cap") }),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "body", "hat" }, rules),
            Ingredients = new[] { Ing("body", "fox"), Ing("hat", "cap") },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };

        var ex = Assert.Throws<RuleConflictException>(
            () => Generator.Generate(book, new GenerateOptions(1, "seed-1")));

        Assert.Contains("cat", ex.Message);
        Assert.Contains("cat", ex.RecipeIds);
    }

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

    [Fact]
    public void Negative_count_is_rejected_by_generate()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Generator.Generate(OneRecipeBook(), new GenerateOptions(-5, "seed-1")));
        Assert.Contains("-5", ex.Message);
    }

    [Fact]
    public void Zero_count_is_rejected_by_generate()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Generator.Generate(OneRecipeBook(), new GenerateOptions(0, "seed-1")));
        Assert.Contains("0", ex.Message);
    }

    [Fact]
    public async Task Negative_count_is_rejected_by_generate_async()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Generator.GenerateAsync(OneRecipeBook(), new GenerateOptions(-5, "seed-1")));
        Assert.Contains("-5", ex.Message);
    }

    [Fact]
    public async Task Zero_count_is_rejected_by_generate_async()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Generator.GenerateAsync(OneRecipeBook(), new GenerateOptions(0, "seed-1")));
        Assert.Contains("0", ex.Message);
    }

    [Fact]
    public void Negative_count_is_rejected_by_generate_streaming_at_call_time()
    {
        // GenerateStreaming's validation lives in a non-iterator wrapper so it runs when the
        // method is CALLED, not on first MoveNext. Asserting the throw happens on the call
        // itself (never touching the returned IEnumerable) is what catches a regression where
        // the check got moved inside the iterator body instead.
        Assert.Throws<InvalidOperationException>(
            () => Generator.GenerateStreaming(OneRecipeBook(), new GenerateOptions(-5, "seed-1")));
    }

    [Fact]
    public void Zero_count_is_rejected_by_generate_streaming_at_call_time()
    {
        Assert.Throws<InvalidOperationException>(
            () => Generator.GenerateStreaming(OneRecipeBook(), new GenerateOptions(0, "seed-1")));
    }
}
