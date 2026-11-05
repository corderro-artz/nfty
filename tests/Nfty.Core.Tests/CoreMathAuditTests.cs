using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class CoreMathAuditTests
{
    private static LoadedIngredient Ingredient(string id, params (string Id, double Weight)[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variants.Select(v => new Variant(v.Id, v.Id, v.Weight)).ToList()),
        VariantImages = variants.ToDictionary(v => v.Id,
            _ => new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255))),
    };

    private static LoadedRecipe Recipe(string id, IReadOnlyList<IncompatibilityRule> rules,
        IReadOnlyDictionary<string, double>? absent, params LoadedIngredient[] ingredients) => new()
    {
        Manifest = new RecipeManifest(id, id, ingredients.Select(i => i.Manifest.Id).ToList(), rules,
            AbsentPercent: absent),
        Ingredients = ingredients,
    };

    private static LoadedCookBook Book(IReadOnlyDictionary<string, double> weights, params LoadedRecipe[] recipes) => new()
    {
        Manifest = new CookBookManifest("book", "Book", new Dimensions(2, 2),
            new Collection("Book", "", "B"), weights),
        Recipes = recipes,
    };

    private static readonly IncompatibilityRule ExcludeB = new(RuleType.Exclude,
        new RuleTarget("hat", "b"), [new RuleTarget("bg", "x")]);

    [Fact]
    public void Fractional_weights_report_a_guaranteed_legal_selection_as_one()
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = 3 },
            Recipe("r", [ExcludeB], null, Ingredient("hat", ("a", 7), ("b", 3)), Ingredient("bg", ("x", 1))));
        var odds = SelectionOdds.Prepare(PeekedCookBook.Of(book));

        var chance = odds.Of("r", new Dictionary<string, string> { ["hat"] = "a", ["bg"] = "x" }, []);
        Assert.True(chance.IsExact);
        Assert.Equal(1, chance.Probability);
        Assert.False(odds.Of("r", new Dictionary<string, string> { ["hat"] = "b", ["bg"] = "x" }, []).IsKnown);
    }

    [Fact]
    public void Optional_ruled_and_recipe_weighted_chances_match_analytic_masses_and_normalize()
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = 3, ["s"] = 1 },
            Recipe("r", [ExcludeB], new Dictionary<string, double> { ["hat"] = 40 },
                Ingredient("hat", ("a", 7), ("b", 3)), Ingredient("bg", ("x", 1))),
            Recipe("s", [], null, Ingredient("other", ("y", 1))));
        var odds = SelectionOdds.Prepare(PeekedCookBook.Of(book));
        var wearing = odds.Of("r", new Dictionary<string, string> { ["hat"] = "a", ["bg"] = "x" }, []);
        var absent = odds.Of("r", new Dictionary<string, string> { ["bg"] = "x" }, ["hat"]);
        var other = odds.Of("s", new Dictionary<string, string> { ["other"] = "y" }, []);

        Assert.All(new[] { wearing, absent, other }, c => Assert.True(c.IsExact));
        Assert.Equal(63.0 / 173, wearing.Probability, 14);
        Assert.Equal(60.0 / 173, absent.Probability, 14);
        Assert.Equal(50.0 / 173, other.Probability, 14);
        Assert.Equal(1, wearing.Probability + absent.Probability + other.Probability, 14);
    }

    [Theory]
    [InlineData(1e-10, SpaceCertainty.Exact, "1E-8%")]
    [InlineData(1e-10, SpaceCertainty.AtLeast, "at least 1E-8%")]
    [InlineData(1e-8, SpaceCertainty.Exact, "0.000001%")]
    [InlineData(1e-8, SpaceCertainty.AtLeast, "at least 0.000001%")]
    [InlineData(0, SpaceCertainty.Exact, "")]
    public void Positive_chances_below_decimal_precision_keep_their_magnitude_and_bound(
        double probability, SpaceCertainty certainty, string expected) =>
        Assert.Equal(expected, SelectionOdds.Describe(new SelectionChance(probability, certainty), asOdds: false));

    [Fact]
    public void A_saturation_interval_collapsed_by_sampling_is_a_finite_point_mass()
    {
        var colorization = new Colorization(ColorModel.Hsv, 1, 1,
            [new ColorEntry(1, new ColorRange(0, 0, 1.5640000000000631, 1.5640000000000633), null)]);
        var shares = ColorName.Shares(colorization);

        Assert.All(shares.Values, p => Assert.True(double.IsFinite(p)));
        Assert.Equal(1, shares.Values.Sum(), 14);
        Assert.Equal(1, shares["Gray"]);
        var rng = new SplitMix64Rng(123);
        for (int i = 0; i < 20; i++)
        {
            var color = ColorRoller.Roll(colorization, rng);
            Assert.Equal("Gray", ColorName.Of(color.H, color.S));
        }
    }

    private static ColorEntry Range(double lo, double hi) => new(1, new ColorRange(lo, hi, 100, 100), null);
    private static Colorization Colors(params ColorEntry[] entries) => new(ColorModel.Hsv, 30, 10, entries);

    private static LoadedIngredient Dynamic(string id, Colorization colors) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic, colors, [new Variant("v", "v", 1)]),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            ["v"] = new Image<Rgba32>(2, 2, new Rgba32(128, 128, 128, 255)),
        },
    };

    private sealed class ConstantRng(double sample) : IRng
    {
        public double NextDouble() => sample;
    }

    [Fact]
    public void Rounded_saturation_endpoint_counts_as_a_real_dna_bucket()
    {
        var colors = Colors(new ColorEntry(1, new ColorRange(0, 0, 0, 30), null));
        var dnas = new HashSet<string>(StringComparer.Ordinal);
        foreach (double sample in new[] { 0, 0.4, 0.8, Math.BitDecrement(1.0) })
        {
            var color = ColorRoller.Roll(colors, new ConstantRng(sample));
            dnas.Add(Dna.Compute("r", [new LayerSelection("layer", "v", color.H, color.S, 30, 10)]));
        }
        Assert.Equal(4, dnas.Count);
        Assert.Equal((4L, true), UniqueSpace.CountColors(colors));
    }

    [Fact]
    public void A_one_ulp_hue_range_really_rolls_both_sides_of_its_upper_bucket_boundary()
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [], null, Dynamic("layer", Colors(Range(Math.BitDecrement(30.0), 30)))));
        Assert.Empty(Validator.Validate(book));
        using var set = Generator.Generate(book, new GenerateOptions(2, "one-ulp"));
        Assert.Equal(2, set.Assets.Select(a => a.Dna).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void A_rule_forcing_an_undercounted_color_layer_absent_keeps_the_result_exact()
    {
        var exclusion = new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "x"),
            [new RuleTarget("color", "v")]);
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [exclusion], new Dictionary<string, double> { ["color"] = 50 },
                Dynamic("color", Colors(Range(0, 90))), Ingredient("bg", ("x", 1))));
        // Two combinations can be walked; the three color buckets exceed the same budget.
        var count = UniqueSpace.Count(book, 2);
        Assert.Equal(1, count.Total);
        Assert.Equal(SpaceCertainty.Exact, count.Certainty);
    }

    [Fact]
    public void An_always_absent_undercounted_color_layer_does_not_create_a_strict_floor()
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [], new Dictionary<string, double> { ["color"] = 100 },
                Dynamic("color", Colors(Range(0, 90))), Ingredient("bg", ("x", 1))));
        var count = UniqueSpace.Count(book, 2);
        Assert.Equal(1, count.Total);
        Assert.Equal(SpaceCertainty.Exact, count.Certainty);
    }

    [Fact]
    public void No_legal_selections_is_exactly_zero_even_if_a_color_bucket_set_was_truncated()
    {
        var excludeAll = new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "x"),
            [new RuleTarget("other", "y")]);
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [excludeAll], new Dictionary<string, double> { ["color"] = 50 },
                Dynamic("color", Colors(Range(0, 90))), Ingredient("bg", ("x", 1)), Ingredient("other", ("y", 1))));
        var count = UniqueSpace.Count(book, 2);
        Assert.Equal(0, count.Total);
        Assert.Equal(0, count["r"].Combos);
        Assert.Equal(SpaceCertainty.Exact, count.Certainty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_single_draw_with_an_unrepresentable_variant_share_is_not_reported_exact(bool tinyFirst)
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [], null, Ingredient("layer", ("a", tinyFirst ? 1e-300 : 1), ("b", tinyFirst ? 1 : 1e-300))));
        Assert.Empty(Validator.Validate(book));
        Assert.Equal(SpaceCertainty.Unknown, UniqueSpace.Count(book).Certainty);
        var odds = SelectionOdds.Prepare(PeekedCookBook.Of(book));
        Assert.False(odds.Of("r", new Dictionary<string, string> { ["layer"] = "a" }, []).IsKnown);
        Assert.False(odds.Of("r", new Dictionary<string, string> { ["layer"] = "b" }, []).IsKnown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_single_draw_with_an_unrepresentable_recipe_share_makes_book_reporting_unknown(bool tinyFirst)
    {
        using var book = Book(new Dictionary<string, double> { ["a"] = tinyFirst ? 1e-300 : 1, ["b"] = tinyFirst ? 1 : 1e-300 },
            Recipe("a", [], null, Ingredient("layer", ("v", 1))),
            Recipe("b", [], null, Ingredient("layer", ("v", 1))));
        Assert.Empty(Validator.Validate(book));
        var count = UniqueSpace.Count(book);
        Assert.Equal(SpaceCertainty.Unknown, count.Certainty);
        Assert.True(count["a"].IsExact);
        Assert.False(SelectionOdds.Of(PeekedCookBook.Of(book), "a",
            new Dictionary<string, string> { ["layer"] = "v" }, []).IsKnown);
    }

    [Fact]
    public void An_unrepresentable_variant_table_does_not_turn_another_recipes_chance_into_an_invented_floor()
    {
        using var book = Book(new Dictionary<string, double> { ["a"] = 1, ["b"] = 1 },
            Recipe("a", [], null, Ingredient("layer", ("v", 1))),
            Recipe("b", [], null, Ingredient("layer", ("big", 1), ("tiny", 1e-300))));
        Assert.Equal(SpaceCertainty.Unknown, SelectionOdds.Of(PeekedCookBook.Of(book), "a",
            new Dictionary<string, string> { ["layer"] = "v" }, []).Certainty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unrepresentable_color_entry_share_has_no_exact_count_or_color_prediction(bool tinyFirst)
    {
        var colors = Colors(new(tinyFirst ? 1e-300 : 1, null, "hex:ff0000"),
            new(tinyFirst ? 1 : 1e-300, null, "hex:0000ff"));
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [], null, Dynamic("layer", colors)));
        Assert.Empty(Validator.Validate(book));
        Assert.Equal(SpaceCertainty.Unknown, UniqueSpace.Count(book).Certainty);
        Assert.Equal((0L, false), UniqueSpace.CountColors(colors));
        Assert.Empty(ColorName.Shares(colors));
    }

    [Theory]
    [InlineData(1e-300)]
    [InlineData(double.Epsilon)]
    public void A_tiny_absence_branch_is_unavailable_even_when_zero_can_roll_it(double percent)
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [], new Dictionary<string, double> { ["layer"] = percent }, Ingredient("layer", ("v", 1))));
        Assert.Empty(Validator.Validate(book));
        Assert.Equal(SpaceCertainty.Unknown, UniqueSpace.Count(book).Certainty);
        Assert.False(SelectionOdds.Of(PeekedCookBook.Of(book), "r", new Dictionary<string, string>(), ["layer"]).IsKnown);
    }

    [Fact]
    public void Always_absent_layers_skip_their_unused_unrepresentable_variant_and_color_tables()
    {
        var unsafeColors = Colors(new(1, null, "hex:ff0000"), new(1e-300, null, "hex:0000ff"));
        var layer = Ingredient("layer", ("a", 1), ("b", 1e-300));
        var dynamicLayer = new LoadedIngredient
        {
            Manifest = layer.Manifest with { Kind = LayerKind.Dynamic, Colorization = unsafeColors },
            VariantImages = layer.VariantImages,
        };
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [], new Dictionary<string, double> { ["layer"] = 100 }, dynamicLayer, Ingredient("bg", ("x", 1))));
        Assert.Equal(SpaceCertainty.Exact, UniqueSpace.Count(book).Certainty);
        var chance = SelectionOdds.Of(PeekedCookBook.Of(book), "r",
            new Dictionary<string, string> { ["bg"] = "x" }, ["layer"]);
        Assert.True(chance.IsExact);
        Assert.Equal(1, chance.Probability);
    }

    [Fact]
    public void A_small_joint_probability_from_many_representable_draws_is_still_known()
    {
        var layers = Enumerable.Range(0, 20).Select(i => Ingredient("layer" + i,
            Enumerable.Range(0, 10).Select(v => ("v" + v, 1.0)).ToArray())).ToArray();
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 }, Recipe("r", [], null, layers));
        var traits = layers.ToDictionary(i => i.Manifest.Name, _ => "v0", StringComparer.Ordinal);
        var chance = SelectionOdds.Of(PeekedCookBook.Of(book), "r", traits, []);
        Assert.True(chance.IsExact);
        Assert.InRange(chance.Probability / 1e-20, 0.999999999999, 1.000000000001);
        Assert.Equal(SpaceCertainty.Exact, UniqueSpace.Count(book).Certainty);
    }

    [Fact]
    public void A_subnormal_draw_scale_does_not_claim_nominal_half_probabilities()
    {
        // With total 2*epsilon, u=0.4 rounds its needle to epsilon and selects b, although
        // nominal equal weights put u=0.4 in a. The draw scale has lost useful precision.
        var weights = new Dictionary<string, double> { ["a"] = double.Epsilon, ["b"] = double.Epsilon };
        Assert.Equal("b", WeightedRoller.Roll(weights, new ScriptedRng(0.4)));
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [], null, Ingredient("layer", ("a", double.Epsilon), ("b", double.Epsilon))));
        Assert.Empty(Validator.Validate(book));
        Assert.Equal(SpaceCertainty.Unknown, UniqueSpace.Count(book).Certainty);
        Assert.False(SelectionOdds.Of(PeekedCookBook.Of(book), "r",
            new Dictionary<string, string> { ["layer"] = "a" }, []).IsKnown);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    public void Invalid_recipe_weights_do_not_pass_the_all_shelved_reporting_case(double weight)
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = weight },
            Recipe("r", [], null, Ingredient("layer", ("v", 1))));
        Assert.Equal(SpaceCertainty.Unknown, UniqueSpace.Count(book).Certainty);
    }

    [Fact]
    public void Color_bucket_unions_are_exact_at_the_budget_and_bound_only_after_another_distinct_bucket()
    {
        Assert.Equal((1L, true), UniqueSpace.CountColors(Colors(Range(0, 0)), 1));
        Assert.Equal((2L, true), UniqueSpace.CountColors(Colors(Range(0, 50), Range(30, 50)), 2));
        Assert.Equal((2L, false), UniqueSpace.CountColors(Colors(Range(0, 50), Range(30, 80)), 2));
        // The second interval can round up to 60, adding a real third bucket to this union.
        Assert.Equal((2L, false), UniqueSpace.CountColors(Colors(Range(0, 60), Range(30, 60)), 2));
        Assert.Equal((1L, true), UniqueSpace.CountColors(Colors(new(1, null, "hex:ff0000"), Range(0, 0)), 1));
        Assert.Equal((1L, false), UniqueSpace.CountColors(Colors(new(1, null, "hex:ff0000"), new(1, null, "hex:00ff00")), 1));
        Assert.Equal((1L, false), UniqueSpace.CountColors(Colors(Range(0, 0), new(1, null, "hex:00ff00")), 1));
    }

    [Fact]
    public void A_ruled_walk_with_exactly_the_budgeted_combinations_is_exact()
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = 3 },
            Recipe("r", [ExcludeB], null, Ingredient("hat", ("a", 7), ("b", 3)), Ingredient("bg", ("x", 1))));
        var count = UniqueSpace.Count(book, 2);
        Assert.Equal(SpaceCertainty.Exact, count.Certainty);
        Assert.Equal(1, count.Total);
        var chance = SelectionOdds.Of(PeekedCookBook.Of(book), "r",
            new Dictionary<string, string> { ["hat"] = "a", ["bg"] = "x" }, [], 2);
        Assert.True(chance.IsExact);
        Assert.Equal(1, chance.Probability);
    }

    private sealed class CallbackProgress(Action<GenerationProgress> callback) : IProgress<GenerationProgress>
    {
        public void Report(GenerationProgress value) => callback(value);
    }

    [Fact]
    public void Streaming_disposes_the_unyielded_image_if_a_synchronous_progress_callback_throws()
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [], null, Ingredient("layer", ("v", 1))));
        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var failure = new InvalidOperationException("callback failed");
        using (var stream = Generator.GenerateStreaming(book, new GenerateOptions(1, "stream"),
                   progress: new CallbackProgress(_ => throw failure)).GetEnumerator())
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => stream.MoveNext()));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    [Fact]
    public void A_successful_stream_yield_transfers_image_ownership_to_the_caller()
    {
        using var book = Book(new Dictionary<string, double> { ["r"] = 1 },
            Recipe("r", [], null, Ingredient("layer", ("v", 1))));
        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        GeneratedAsset asset;
        using (var stream = Generator.GenerateStreaming(book, new GenerateOptions(1, "stream"),
                   progress: new CallbackProgress(p => Assert.Equal(1, p.Completed))).GetEnumerator())
        {
            Assert.True(stream.MoveNext());
            asset = stream.Current;
        }
        using (asset)
        {
            Assert.True(MemoryDiagnostics.TotalUndisposedAllocationCount > before);
            Assert.Equal(new Rgba32(10, 20, 30, 255), asset.Image[0, 0]);
        }
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }
}
