using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

// Covers GenerateOptions.EnforceUniqueDna = false ("unlimited mode"): every roll is accepted even
// when its DNA repeats, identity is carried by the sequential token id, and the unique-space count
// no longer gates the run. Incompatibility rules are still enforced.
public class GeneratorUnlimitedModeTests
{
    private static LoadedIngredient Ing(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255))),
    };

    private static LoadedRecipe Recipe(string id, IReadOnlyList<IncompatibilityRule> rules, params LoadedIngredient[] ings) => new()
    {
        Manifest = new RecipeManifest(id, id, ings.Select(i => i.Manifest.Id).ToList(), rules),
        Ingredients = ings,
    };

    // One recipe "cat": 2 bg x 2 body = exactly 4 unique DNA. Requesting more than 4 exhausts the
    // space under normal mode; unlimited mode accepts repeats instead.
    private static LoadedCookBook FourDnaBook() => new()
    {
        Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[]
        {
            Recipe("cat", Array.Empty<IncompatibilityRule>(), Ing("bg", "a", "b"), Ing("body", "x", "y")),
        },
    };

    private static GenerateOptions Unlimited(int count, string seed = "seed-1") =>
        new(count, seed, EnforceUniqueDna: false);

    [Fact]
    public void Unlimited_mode_generates_past_the_unique_space()
    {
        // 4 unique DNA available, 10 requested — normal mode throws here, unlimited fills all 10.
        using var set = Generator.Generate(FourDnaBook(), Unlimited(10));
        Assert.Equal(10, set.Assets.Count);
    }

    [Fact]
    public void Unlimited_mode_accepts_repeated_dna()
    {
        using var set = Generator.Generate(FourDnaBook(), Unlimited(10));

        int distinct = set.Assets.Select(a => a.Dna).Distinct().Count();
        // The space holds only 4 distinct DNA, so 10 assets must contain repeats.
        Assert.True(distinct <= 4);
        Assert.True(distinct < set.Assets.Count, "expected repeated DNA when generating past the space");
    }

    [Fact]
    public void Unlimited_mode_keeps_token_ids_unique_and_contiguous()
    {
        using var set = Generator.Generate(FourDnaBook(), Unlimited(10));

        // Even where DNA repeats, the token id (set number) is the unique, contiguous identity.
        Assert.Equal(Enumerable.Range(1, 10), set.Assets.Select(a => a.SetNumber));
    }

    [Fact]
    public void Unlimited_mode_is_still_deterministic()
    {
        var a = Generator.Generate(FourDnaBook(), Unlimited(10)).Assets.Select(x => x.Dna).ToList();
        var b = Generator.Generate(FourDnaBook(), Unlimited(10)).Assets.Select(x => x.Dna).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Normal_mode_still_exhausts_the_space_honestly()
    {
        // Guard: turning the flag OFF (the default) must not change the exhaustion behaviour.
        var ex = Assert.Throws<UniqueSpaceExhaustedException>(
            () => Generator.Generate(FourDnaBook(), new GenerateOptions(5, "seed-1")));
        Assert.Equal(4, ex.Available);
    }

    [Fact]
    public void Unlimited_mode_still_enforces_incompatibility_rules()
    {
        // The only body excludes the only hat, so no legal combination exists. Unlimited mode does
        // not weaken rules: this is a genuine impossibility, reported as a conflict, not exhaustion.
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("body", "fox"),
                new[] { new RuleTarget("hat", "cap") }),
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { Recipe("cat", rules, Ing("body", "fox"), Ing("hat", "cap")) },
        };

        var ex = Assert.Throws<RuleConflictException>(
            () => Generator.Generate(book, Unlimited(1)));
        Assert.Contains("cat", ex.RecipeIds);
    }

    [Fact]
    public void Unlimited_mode_extends_past_the_space_ignoring_existing_dna()
    {
        // Pretend two assets already exist (numbers 1-2). The book holds only 4 unique DNA, so
        // asking for 6 more would be impossible under normal dedup; unlimited accepts repeats and
        // continues the numbering from 3.
        var existing = new[] { "dead-dna-1", "dead-dna-2" };
        using var set = Generator.Generate(FourDnaBook(), Unlimited(6), existing, startNumber: 3);

        Assert.Equal(6, set.Assets.Count);
        Assert.Equal(Enumerable.Range(3, 6), set.Assets.Select(a => a.SetNumber));
    }
}
