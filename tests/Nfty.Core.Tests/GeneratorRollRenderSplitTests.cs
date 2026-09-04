using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// RollOne runs in two phases: a roll phase that consumes the RNG and decides the selection, then
/// a render phase that colorizes and composites. The rules are applied between them, so a roll the
/// rules reject never renders anything.
///
/// The split is only safe because legality is a function of the variant selection alone. These
/// tests pin the two things that would break if the phases were ever resequenced: the RNG draws
/// must stay in the roll phase (or a rejected roll would consume a different amount of the stream
/// and the same seed would stop reproducing), and a rejected roll must still leave nothing behind.
/// </summary>
public class GeneratorRollRenderSplitTests
{
    // "bg" is dynamic, so it rolls a color — an extra RNG draw per layer per attempt, which is
    // exactly what a resequenced phase would disturb. Its value-map is grayscale, as Validator
    // requires. "body" is custom and composites as-is.
    private static LoadedIngredient Bg() => new()
    {
        Manifest = new IngredientManifest("bg", "bg", LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 8, 4,
                new[] { new ColorEntry(1, new ColorRange(0, 360, 0, 100), null) }),
            new[] { new Variant("a", "A", 1), new Variant("b", "B", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            ["a"] = new Image<Rgba32>(2, 2, new Rgba32(64, 64, 64, 255)),
            ["b"] = new Image<Rgba32>(2, 2, new Rgba32(192, 192, 192, 255)),
        },
    };

    private static LoadedIngredient Body() => new()
    {
        Manifest = new IngredientManifest("body", "body", LayerKind.Custom, null,
            new[] { new Variant("x", "X", 1), new Variant("y", "Y", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            ["x"] = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 128)),
            ["y"] = new Image<Rgba32>(2, 2, new Rgba32(30, 20, 10, 128)),
        },
    };

    /// <summary>
    /// One recipe whose rule excludes bg=a with body=x, so a quarter of all rolls are rejected —
    /// each rejection exercising the path that returns before the render phase.
    /// </summary>
    private static LoadedCookBook RuleBook() => new()
    {
        Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"),
            new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "body" },
                    new[]
                    {
                        new IncompatibilityRule(RuleType.Exclude,
                            new RuleTarget("bg", "a"),
                            new[] { new RuleTarget("body", "x") }),
                    }),
                Ingredients = new[] { Bg(), Body() },
            },
        },
    };

    [Fact]
    public void Same_seed_reproduces_identical_dna_when_rules_reject_rolls()
    {
        // Rejected rolls consume RNG. If the rolls ever moved relative to the rule check, the
        // stream would advance by a different amount on a rejection and this would diverge.
        var opts = new GenerateOptions(12, "reroll-seed");
        using var first = Generator.Generate(RuleBook(), opts);
        using var second = Generator.Generate(RuleBook(), opts);

        Assert.Equal(first.Assets.Select(a => a.Dna), second.Assets.Select(a => a.Dna));
    }

    [Fact]
    public void Same_seed_reproduces_identical_rolled_colors_when_rules_reject_rolls()
    {
        // The color roll is the RNG draw most sensitive to a resequenced phase, because it comes
        // after the variant draw within the same layer.
        var opts = new GenerateOptions(12, "reroll-seed");
        using var first = Generator.Generate(RuleBook(), opts);
        using var second = Generator.Generate(RuleBook(), opts);

        Assert.Equal(
            first.Assets.SelectMany(a => a.ColorRolls).Select(c => (c.H, c.S)),
            second.Assets.SelectMany(a => a.ColorRolls).Select(c => (c.H, c.S)));
    }

    [Fact]
    public void Streaming_and_buffered_generation_agree_when_rules_reject_rolls()
    {
        // Both entry points share the roll/render core and the per-run precomputation, so they
        // must walk the same RNG stream and produce the same assets in the same order.
        var opts = new GenerateOptions(12, "reroll-seed");
        using var buffered = Generator.Generate(RuleBook(), opts);

        using var book = RuleBook();
        var streamed = new List<string>();
        foreach (var asset in Generator.GenerateStreaming(book, opts))
        {
            streamed.Add(asset.Dna);
            asset.Dispose();
        }

        Assert.Equal(buffered.Assets.Select(a => a.Dna), streamed);
    }

    [Fact]
    public void No_generated_asset_violates_the_recipe_rules()
    {
        using var set = Generator.Generate(RuleBook(), new GenerateOptions(12, "reroll-seed"));

        foreach (var asset in set.Assets)
        {
            var byIngredient = asset.Traits.ToDictionary(t => t.IngredientId, t => t.VariantId);
            Assert.False(byIngredient["bg"] == "a" && byIngredient["body"] == "x");
        }
    }

    [Fact]
    public void A_run_full_of_rejected_rolls_leaks_no_images()
    {
        // A rejected roll returns before the render phase, so it should allocate no layer image at
        // all; an accepted one must dispose its per-layer images once composited. Asserted through
        // ImageSharp's process-wide leak counter, as in GeneratorRollOneDisposalTests.
        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;

        using (var book = RuleBook())
        using (var set = Generator.Generate(book, new GenerateOptions(12, "reroll-seed")))
        {
            Assert.Equal(12, set.Assets.Count);
        }

        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    [Fact]
    public void Rerolling_for_a_duplicate_dna_stays_deterministic()
    {
        // A duplicate DNA is discarded and re-rolled, another path that advances the stream
        // without emitting. Requesting close to the whole space forces several of them.
        var opts = new GenerateOptions(24, "dup-seed");
        using var first = Generator.Generate(RuleBook(), opts);
        using var second = Generator.Generate(RuleBook(), opts);

        Assert.Equal(first.Assets.Select(a => a.Dna), second.Assets.Select(a => a.Dna));
        Assert.Equal(24, first.Assets.Select(a => a.Dna).Distinct().Count());
    }
}
