using Nfty.Core.Stats;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Optional layers: a layer that may be left out of an asset entirely, at a chance the RECIPE sets.
///
/// <para>The first test is the one the whole feature rests on. Adding an outcome to a weighted draw
/// is the kind of change that quietly re-seats every subsequent random number, and this generator's
/// promise is that the same cookbook and the same seed produce byte-identical output forever. So the
/// absent outcome is not a key in the weight table — it is drawn ahead of it, from a total that is
/// the old total plus zero — and that has to be PROVEN rather than reasoned about.</para>
/// </summary>
public class AbsentLayerTests
{
    private static WeightedRoller.WeightTable Table() => WeightedRoller.Prepare(
        new Dictionary<string, double> { ["a"] = 3, ["b"] = 1, ["c"] = 6 });

    [Fact]
    public void At_zero_absent_weight_the_draw_is_the_old_one_number_for_number()
    {
        var table = Table();
        var oldRng = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        var newRng = new SplitMix64Rng(SeedHash.ToUlong("vapor"));

        // Not "statistically the same" — the SAME KEY on every single draw, from the same RNG
        // state, because r is drawn from the same range and neither the key array nor its ordinal
        // order changed. This is what lets a book that does not use the feature keep every Set it
        // ever generated reproducible.
        for (int i = 0; i < 2000; i++)
            Assert.Equal(WeightedRoller.Roll(table, oldRng), WeightedRoller.Roll(table, 0, newRng));
    }

    [Fact]
    public void The_absent_weight_makes_the_layer_miss_as_often_as_asked()
    {
        var table = Table();                       // total 10
        double w = WeightedRoller.AbsentWeight(25, 10);
        Assert.Equal(10.0 / 3, w, 9);              // a/(a+10) = 0.25  =>  a = 10/3

        var rng = new SplitMix64Rng(SeedHash.ToUlong("chase"));
        int absent = 0;
        const int Draws = 40_000;
        for (int i = 0; i < Draws; i++)
            if (WeightedRoller.Roll(table, w, rng) is null) absent++;

        Assert.InRange(absent / (double)Draws, 0.24, 0.26);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 10)]        // equal odds against a total of 10
    [InlineData(90, 90)]        // nine times as likely to be absent
    public void The_percent_to_weight_conversion_is_the_one_the_formula_promises(double pct, double expected)
    {
        Assert.Equal(expected, WeightedRoller.AbsentWeight(pct, 10), 9);
    }

    [Fact]
    public void A_hundred_percent_has_no_finite_weight_and_says_so()
    {
        Assert.True(WeightedRoller.AlwaysAbsent(100));
        Assert.False(WeightedRoller.AlwaysAbsent(99.9));

        // The formula divides by zero at 100, so the caller must test AlwaysAbsent first. Throwing
        // here rather than returning infinity is what keeps that from becoming a silent NaN total.
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => WeightedRoller.AbsentWeight(100, 10));
        Assert.Contains("never appears", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void A_percent_outside_the_range_is_refused(double pct) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WeightedRoller.AbsentWeight(pct, 10));

    [Fact]
    public void A_negative_absent_weight_is_refused_rather_than_skewing_the_draw()
    {
        // A negative weight would make `r < absentWeight` unreachable AND shrink the total, so the
        // walk would run off the end of the cumulative array into its defensive return — turning
        // the guard into the production path, which is the failure the NaN check below it exists for.
        Assert.Throws<InvalidOperationException>(() =>
            WeightedRoller.Roll(Table(), -1, new SplitMix64Rng(1)));
    }

    // ---------------------------------------------------------------- the generator

    private static LoadedIngredient Ing(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255))),
    };

    private static LoadedCookBook Book(IReadOnlyDictionary<string, double>? absent) => new()
    {
        Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "cat", new[] { "bg", "hat" },
                    Array.Empty<IncompatibilityRule>(), AbsentPercent: absent),
                Ingredients = new[] { Ing("bg", "a", "b"), Ing("hat", "crown", "cap") },
            },
        },
    };

    private static IReadOnlyList<string> Dnas(IReadOnlyDictionary<string, double>? absent, int n = 4)
    {
        using var book = Book(absent);
        using var set = Generator.Generate(book, new GenerateOptions(n, "seed1"));
        return set.Assets.Select(a => a.Dna).ToList();
    }

    [Fact]
    public void A_recipe_that_does_not_use_the_feature_generates_exactly_what_it_always_did()
    {
        // Three ways of saying "no optional layers" — absent, empty, and explicitly zero — must be
        // one behavior. The third is the one that could have cost a draw, and does not.
        var none = Dnas(null);
        Assert.Equal(none, Dnas(new Dictionary<string, double>()));
        Assert.Equal(none, Dnas(new Dictionary<string, double> { ["hat"] = 0 }));
    }

    [Fact]
    public void A_layer_at_a_hundred_percent_never_appears_and_is_never_rolled()
    {
        using var book = Book(new Dictionary<string, double> { ["hat"] = 100 });
        using var set = Generator.Generate(book, new GenerateOptions(2, "seed1"));

        Assert.All(set.Assets, a =>
        {
            Assert.DoesNotContain(a.Traits, t => t.IngredientId == "hat");
            Assert.Contains(a.Traits, t => t.IngredientId == "bg");   // its neighbour still rolls
        });
    }

    [Fact]
    public void An_absent_layer_publishes_no_trait_and_leaves_the_others_alone()
    {
        using var book = Book(new Dictionary<string, double> { ["hat"] = 50 });
        using var set = Generator.Generate(book, new GenerateOptions(8, "seed1", EnforceUniqueDna: false));

        // Some of each, or the fixture proves nothing about a 50% layer.
        Assert.Contains(set.Assets, a => a.Traits.Any(t => t.IngredientId == "hat"));
        Assert.Contains(set.Assets, a => a.Traits.All(t => t.IngredientId != "hat"));
        Assert.All(set.Assets, a => Assert.Contains(a.Traits, t => t.IngredientId == "bg"));
    }

    /// <summary>
    /// Two assets that both show nothing on a DYNAMIC layer must have the same DNA for it. An absent
    /// layer skips the whole loop body, so it rolls no color — get that wrong and the DNA carries an
    /// (H,S) for a layer nobody can see, and <c>UniqueSpace</c>'s promise of reachable uniques starts
    /// counting distinctions that do not exist on screen.
    /// </summary>
    [Fact]
    public void An_absent_dynamic_layer_rolls_no_color()
    {
        var aura = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 12, 4,
                    new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
                new[] { new Variant("glow", "glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["glow"] = new Image<Rgba32>(2, 2, new Rgba32(120, 120, 120, 255)),
            },
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "cat", new[] { "bg", "aura" },
                        Array.Empty<IncompatibilityRule>(),
                        AbsentPercent: new Dictionary<string, double> { ["aura"] = 100 }),
                    Ingredients = new[] { Ing("bg", "a"), aura },
                },
            },
        };

        using var set = Generator.Generate(book, new GenerateOptions(3, "seed1", EnforceUniqueDna: false));

        // bg has one variant and aura never appears, so every asset is the same asset — which is
        // only true if the invisible layer contributed no rolled color to the hash.
        Assert.Single(set.Assets.Select(a => a.Dna).Distinct());
        Assert.All(set.Assets, a => Assert.DoesNotContain(a.ColorRolls, c => c.LayerId == "aura"));
    }

    // ------------------------------------------------------------- the space it admits

    /// <summary>
    /// The count is a PROMISE: exactly this many unique DNA must be generable, or Generate throws
    /// the self-contradicting "allows exactly N, but N were requested". So these do not check a
    /// formula — they generate the whole space and count what actually comes out.
    /// </summary>
    private static int ActualUniqueDna(LoadedCookBook book, int ask)
    {
        using var set = Generator.Generate(book, new GenerateOptions(ask, "seed1"));
        return set.Assets.Select(a => a.Dna).Distinct().Count();
    }

    [Fact]
    public void An_optional_layer_adds_exactly_one_shape_and_the_generator_can_reach_them_all()
    {
        // bg 2 x hat 2 = 4 with hat mandatory; hat optional adds "no hat", so 2 x 3 = 6.
        using var mandatory = Book(null);
        Assert.Equal(4, UniqueSpace.Count(mandatory).Total);

        using var optional = Book(new Dictionary<string, double> { ["hat"] = 40 });
        var counted = UniqueSpace.Count(optional);
        Assert.Equal(6, counted.Total);
        Assert.True(counted.IsExact);

        // And every one of the six is actually reachable, which is the part a formula cannot claim.
        Assert.Equal(6, ActualUniqueDna(optional, 6));
    }

    [Fact]
    public void A_layer_that_never_appears_contributes_one_shape_not_its_variants()
    {
        using var book = Book(new Dictionary<string, double> { ["hat"] = 100 });
        // hat offers only "absent", so the space is bg's 2 — not 2 x 3.
        Assert.Equal(2, UniqueSpace.Count(book).Total);
        Assert.Equal(2, ActualUniqueDna(book, 2));
    }

    /// <summary>
    /// The reason the counter had to be restructured rather than patched. It used to compute
    /// (legal combinations) x (product of every dynamic layer's color buckets) and multiply the two
    /// — valid only because every legal selection had every layer present, so each carried the same
    /// bucket product. An absent Dynamic layer wears no color and contributes ONE shape, so the
    /// bucket product now varies per selection and the two no longer factorize.
    /// </summary>
    [Fact]
    public void An_optional_dynamic_layer_contributes_one_shape_absent_and_its_colors_present()
    {
        var aura = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "aura", LayerKind.Dynamic,
                // 4 hue buckets x 1 saturation bucket = 4 colors.
                new Colorization(ColorModel.Hsv, 90, 100,
                    new[] { new ColorEntry(1, new ColorRange(0, 360, 0, 100), null) }),
                new[] { new Variant("glow", "glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["glow"] = new Image<Rgba32>(2, 2, new Rgba32(120, 120, 120, 255)),
            },
        };
        LoadedCookBook WithAura(IReadOnlyDictionary<string, double>? absent) => new()
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "cat", new[] { "bg", "aura" },
                        Array.Empty<IncompatibilityRule>(), AbsentPercent: absent),
                    Ingredients = new[] { Ing("bg", "a", "b"), aura },
                },
            },
        };

        using var mandatory = WithAura(null);
        Assert.Equal(8, UniqueSpace.Count(mandatory).Total);          // bg 2 x (1 variant x 4 colors)

        using var optional = WithAura(new Dictionary<string, double> { ["aura"] = 30 });
        // NOT 2 x 2 x 4 = 16. Absent is ONE shape however many colors it could have worn:
        // bg 2 x (1 x 4 + 1) = 10.
        Assert.Equal(10, UniqueSpace.Count(optional).Total);
        Assert.Equal(10, ActualUniqueDna(optional, 10));
    }

    [Fact]
    public void Rules_and_absence_are_counted_together_rather_than_multiplied_apart()
    {
        // bg{a,b} x hat{crown,cap}, hat optional => 6 selections. One rule removes exactly one of
        // them (a+crown), leaving 5 — and the absent selections are untouched by it, which is only
        // right because RulesEngine reads a missing entry as "not present".
        var rule = new IncompatibilityRule(RuleType.Exclude,
            new RuleTarget("bg", "a"), new[] { new RuleTarget("hat", "crown") });

        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "cat", new[] { "bg", "hat" },
                        new[] { rule }, AbsentPercent: new Dictionary<string, double> { ["hat"] = 40 }),
                    Ingredients = new[] { Ing("bg", "a", "b"), Ing("hat", "crown", "cap") },
                },
            },
        };

        var counted = UniqueSpace.Count(book);
        Assert.Equal(5, counted.Total);
        Assert.True(counted.IsExact);
        Assert.Equal(5, ActualUniqueDna(book, 5));
    }

    // -------------------------------------------------------- what the odds actually say

    /// <summary>
    /// A chase item's odds are the odds of GETTING it. Without folding absence in, the variants of a
    /// 90%-absent layer each report the share they hold among themselves — so a one-in-twenty item
    /// prints "50% in recipe", which is the opposite of the number an author is looking for.
    /// </summary>
    [Fact]
    public void An_optional_layer_scales_the_odds_of_every_variant_under_it()
    {
        using var mandatory = Book(null);
        var before = RarityCalculator.Compute(mandatory).Traits
            .Single(t => t.IngredientId == "hat" && t.VariantId == "crown");
        Assert.Equal(50, before.WithinRecipePercent);        // one of two, always present

        using var chase = Book(new Dictionary<string, double> { ["hat"] = 90 });
        var after = RarityCalculator.Compute(chase).Traits
            .Single(t => t.IngredientId == "hat" && t.VariantId == "crown");
        Assert.Equal(5, after.WithinRecipePercent);          // half of the 10% it shows up at

        // Its neighbour on a mandatory layer is untouched.
        Assert.Equal(50, RarityCalculator.Compute(chase).Traits
            .Single(t => t.IngredientId == "bg" && t.VariantId == "a").WithinRecipePercent);
    }

    /// <summary>And the odds are not just arithmetic — a generated collection lands near them.</summary>
    [Fact]
    public void The_generated_collection_lands_near_the_odds_the_report_promises()
    {
        using var book = Book(new Dictionary<string, double> { ["hat"] = 75 });
        using var set = Generator.Generate(book, new GenerateOptions(600, "seed1", EnforceUniqueDna: false));

        double withHat = set.Assets.Count(a => a.Traits.Any(t => t.IngredientId == "hat")) / 600.0;
        Assert.InRange(withHat, 0.21, 0.29);                 // asked for 25% present
    }

    // ------------------------------------------------------------------- validation

    private static IReadOnlyList<string> Problems(
        IReadOnlyDictionary<string, double>? absent, params IncompatibilityRule[] rules)
    {
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "cat", new[] { "bg", "hat" },
                        rules, AbsentPercent: absent),
                    Ingredients = new[] { Ing("bg", "a", "b"), Ing("hat", "crown", "cap") },
                },
            },
        };
        return Validator.Validate(book);
    }

    [Fact]
    public void A_chance_for_a_layer_the_recipe_does_not_stack_is_reported()
    {
        // Not merely useless: it is almost certainly a chance meant for a layer that IS stacked,
        // doing nothing while the author believes their chase item is rare.
        Assert.Contains(Problems(new Dictionary<string, double> { ["wings"] = 50 }),
            p => p.Contains("not one of its layers"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    public void A_chance_outside_zero_to_a_hundred_is_reported(double pct) =>
        Assert.Contains(Problems(new Dictionary<string, double> { ["hat"] = pct }),
            p => p.Contains("absent chance"));

    [Fact]
    public void Leaving_out_every_layer_is_reported_like_an_empty_layer_order()
    {
        Assert.Contains(
            Problems(new Dictionary<string, double> { ["bg"] = 100, ["hat"] = 100 }),
            p => p.Contains("fully-transparent"));
    }

    [Fact]
    public void A_rule_requiring_a_layer_that_never_appears_is_reported()
    {
        // Fatal in a way the author will not see coming: every roll that hits the trigger is
        // rejected, so the trigger's own variant becomes unrollable and the space quietly shrinks.
        var rule = new IncompatibilityRule(RuleType.Require,
            new RuleTarget("bg", "a"), new[] { new RuleTarget("hat", "crown") });

        Assert.Contains(Problems(new Dictionary<string, double> { ["hat"] = 100 }, rule),
            p => p.Contains("can never be rolled at all"));
    }

    [Fact]
    public void A_rule_triggered_by_a_layer_that_never_appears_is_reported()
    {
        var rule = new IncompatibilityRule(RuleType.Exclude,
            new RuleTarget("hat", "crown"), new[] { new RuleTarget("bg", "a") });

        Assert.Contains(Problems(new Dictionary<string, double> { ["hat"] = 100 }, rule),
            p => p.Contains("can never fire"));
    }

    [Fact]
    public void An_ordinary_optional_layer_is_not_reported()
    {
        // The guard against the checks above being written so broadly that a working book trips
        // them: a real chase item, with a rule that names it and still fires sometimes.
        var rule = new IncompatibilityRule(RuleType.Exclude,
            new RuleTarget("bg", "a"), new[] { new RuleTarget("hat", "crown") });

        Assert.Empty(Problems(new Dictionary<string, double> { ["hat"] = 95 }, rule));
    }
}
