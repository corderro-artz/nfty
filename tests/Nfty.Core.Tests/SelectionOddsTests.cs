using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// THE CHANCE OF ROLLING ONE EXACT ASSET, WITHOUT THE INDEPENDENCE ASSUMPTION.
/// </summary>
/// <remarks>
/// <para>The Set browser could already multiply its rarity table together, and that product is only
/// right for a book with no rules and no optional layers. This is the figure computed from the
/// book's own weights, and the test that matters is the last one in the file: it rolls the real
/// generator four thousand times and counts, rather than checking one formula against a second copy
/// of itself.</para>
/// </remarks>
public class SelectionOddsTests
{
    private static LoadedIngredient Ing(string id, params (string Id, double Weight)[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variants.Select(v => new Variant(v.Id, v.Id, v.Weight)).ToList()),
        VariantImages = variants.ToDictionary(v => v.Id,
            _ => new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255))),
    };

    private static LoadedRecipe Recipe(
        string id,
        IReadOnlyList<IncompatibilityRule>? rules = null,
        IReadOnlyDictionary<string, double>? absent = null,
        params LoadedIngredient[] ings) => new()
    {
        Manifest = new RecipeManifest(id, id, ings.Select(i => i.Manifest.Id).ToList(),
            rules ?? Array.Empty<IncompatibilityRule>(), AbsentPercent: absent),
        Ingredients = ings,
    };

    private static LoadedCookBook Book(IReadOnlyDictionary<string, double> weights, params LoadedRecipe[] recipes) => new()
    {
        Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"), weights),
        Recipes = recipes,
    };

    /// <summary>One even 2x2 recipe: four combinations, a quarter each.</summary>
    private static LoadedCookBook Even() => Book(
        new Dictionary<string, double> { ["cat"] = 1 },
        Recipe("cat", null, null, Ing("bg", ("a", 1), ("b", 1)), Ing("body", ("x", 1), ("y", 1))));

    /// <summary>The same recipe with one combination forbidden: three survive, a third each.</summary>
    private static LoadedCookBook Ruled() => Book(
        new Dictionary<string, double> { ["cat"] = 1 },
        Recipe("cat",
            new[]
            {
                new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "a"),
                    new[] { new RuleTarget("body", "x") }),
            },
            null,
            Ing("bg", ("a", 1), ("b", 1)), Ing("body", ("x", 1), ("y", 1))));

    private static SelectionChance Of(LoadedCookBook book, string recipe,
        Dictionary<string, string> traits, params string[] absent) =>
        SelectionOdds.Of(PeekedCookBook.Of(book), recipe, traits, absent);

    [Fact]
    public void An_even_book_gives_every_combination_the_same_share()
    {
        using var book = Even();
        var chance = Of(book, "cat", new Dictionary<string, string> { ["bg"] = "a", ["body"] = "x" });

        Assert.True(chance.IsExact);
        Assert.Equal(0.25, chance.Probability, 12);
        Assert.Equal(4, chance.OneIn, 9);
    }

    /// <summary>
    /// THE WHOLE POINT. A rule removes one combination and hands its share to the survivors, so the
    /// truth is 1/3 where the product of the two layers' own shares says 1/4. The naive figure is
    /// restated here rather than merely described, so the test fails if the two ever agree.
    /// </summary>
    [Fact]
    public void A_rule_makes_the_surviving_combinations_commoner_than_the_product_says()
    {
        using var book = Ruled();
        var chance = Of(book, "cat", new Dictionary<string, string> { ["bg"] = "b", ["body"] = "x" });

        Assert.True(chance.IsExact);
        Assert.Equal(1.0 / 3.0, chance.Probability, 12);
        Assert.NotEqual(0.5 * 0.5, chance.Probability, 6);
    }

    /// <summary>The forbidden combination is not rare, it is impossible — and a Set cannot contain
    /// one, so reporting nothing is right where reporting "0%" would look like an answer.</summary>
    [Fact]
    public void A_combination_a_rule_forbids_has_no_chance_to_report()
    {
        using var book = Ruled();
        var chance = Of(book, "cat", new Dictionary<string, string> { ["bg"] = "a", ["body"] = "x" });

        Assert.False(chance.IsKnown);
        Assert.Equal(SpaceCertainty.Unknown, chance.Certainty);
        Assert.Equal("", SelectionOdds.Describe(chance, asOdds: true));
    }

    [Fact]
    public void A_recipe_weight_is_its_share_of_the_answer()
    {
        using var book = Book(
            new Dictionary<string, double> { ["cat"] = 80, ["robot"] = 20 },
            Recipe("cat", null, null, Ing("bg", ("a", 1), ("b", 1))),
            Recipe("robot", null, null, Ing("bg", ("a", 1), ("b", 1))));

        var cat = Of(book, "cat", new Dictionary<string, string> { ["bg"] = "a" });
        var robot = Of(book, "robot", new Dictionary<string, string> { ["bg"] = "a" });

        Assert.Equal(0.8 * 0.5, cat.Probability, 12);
        Assert.Equal(0.2 * 0.5, robot.Probability, 12);
    }

    /// <summary>An uneven layer splits what is left of it, not what it started with.</summary>
    [Fact]
    public void Variant_weights_are_shares_of_their_own_layer()
    {
        using var book = Book(
            new Dictionary<string, double> { ["cat"] = 1 },
            Recipe("cat", null, null, Ing("bg", ("a", 3), ("b", 1))));

        Assert.Equal(0.75, Of(book, "cat", new Dictionary<string, string> { ["bg"] = "a" }).Probability, 12);
        Assert.Equal(0.25, Of(book, "cat", new Dictionary<string, string> { ["bg"] = "b" }).Probability, 12);
    }

    /// <summary>
    /// AN OPTIONAL LAYER IS TWO FACTS AT ONCE: the chance of it being missing IS the stored percent,
    /// and every variant of it is that much rarer than its sibling share suggests. Both are asserted
    /// here, because getting only the first right still misprices every asset that HAS the layer.
    /// </summary>
    [Fact]
    public void An_optional_layer_costs_its_variants_exactly_the_absent_percent()
    {
        using var book = Book(
            new Dictionary<string, double> { ["cat"] = 1 },
            Recipe("cat", null, new Dictionary<string, double> { ["hat"] = 40 },
                Ing("hat", ("a", 1), ("b", 1))));

        var absent = Of(book, "cat", new Dictionary<string, string>(), "hat");
        var wearing = Of(book, "cat", new Dictionary<string, string> { ["hat"] = "a" });

        Assert.Equal(0.40, absent.Probability, 12);
        Assert.Equal(0.30, wearing.Probability, 12);
    }

    /// <summary>A layer pinned to 100% absent never appears, so an asset claiming to wear it did not
    /// come from this book.</summary>
    [Fact]
    public void A_shelved_layer_cannot_be_worn()
    {
        using var book = Book(
            new Dictionary<string, double> { ["cat"] = 1 },
            Recipe("cat", null, new Dictionary<string, double> { ["hat"] = 100 },
                Ing("hat", ("a", 1))));

        Assert.Equal(1.0, Of(book, "cat", new Dictionary<string, string>(), "hat").Probability, 12);
        Assert.False(Of(book, "cat", new Dictionary<string, string> { ["hat"] = "a" }).IsKnown);
    }

    /// <summary>A zero-weight recipe is never rolled. Reporting a figure for one would state a
    /// chance the book itself contradicts.</summary>
    [Fact]
    public void A_shelved_recipe_reports_nothing()
    {
        using var book = Book(
            new Dictionary<string, double> { ["cat"] = 1, ["robot"] = 0 },
            Recipe("cat", null, null, Ing("bg", ("a", 1))),
            Recipe("robot", null, null, Ing("bg", ("a", 1))));

        Assert.True(Of(book, "cat", new Dictionary<string, string> { ["bg"] = "a" }).IsKnown);
        Assert.False(Of(book, "robot", new Dictionary<string, string> { ["bg"] = "a" }).IsKnown);
    }

    [Theory]
    [InlineData("nosuch", "a")]
    [InlineData("cat", "nosuch")]
    public void A_selection_this_book_does_not_contain_reports_nothing(string recipe, string variant)
    {
        using var book = Even();
        var chance = Of(book, recipe, new Dictionary<string, string> { ["bg"] = variant, ["body"] = "x" });
        Assert.False(chance.IsKnown);
    }

    /// <summary>
    /// A layer carrying two variants of one NAME is scored as either of them, because a name is all
    /// the metadata records and "the asset shows a Crown" is the event a reader is asking about.
    /// </summary>
    [Fact]
    public void Two_variants_sharing_a_name_are_one_outcome()
    {
        using var hat = new LoadedIngredient
        {
            Manifest = new IngredientManifest("hat", "hat", LayerKind.Custom, null, new[]
            {
                new Variant("crown-a", "Crown", 1),
                new Variant("crown-b", "Crown", 1),
                new Variant("cap", "Cap", 2),
            }),
            VariantImages = new[] { "crown-a", "crown-b", "cap" }.ToDictionary(
                v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255))),
        };
        using var book = Book(new Dictionary<string, double> { ["cat"] = 1 }, Recipe("cat", null, null, hat));

        Assert.Equal(0.5, Of(book, "cat", new Dictionary<string, string> { ["hat"] = "Crown" }).Probability, 12);
        Assert.Equal(0.5, Of(book, "cat", new Dictionary<string, string> { ["hat"] = "Cap" }).Probability, 12);
    }

    /// <summary>
    /// GIVING UP ON THE DENOMINATOR COSTS A DIRECTION, NOT THE ANSWER. The denominator is at most 1,
    /// so the numerator alone can only be too small — a floor on the probability, and therefore a
    /// CEILING on the odds.
    ///
    /// <para>Probed with a budget of TWO, which is the smallest that starves the right half: the
    /// denominator has to walk this recipe's four combinations and gives up, while the numerator —
    /// restricted to the one outcome per layer the metadata names — still walks. A budget of one
    /// would starve both and report nothing, which is a different answer and not this one.</para>
    /// </summary>
    [Fact]
    public void An_unwalkable_book_reports_a_floor_rather_than_a_guess()
    {
        using var book = Ruled();
        var peeked = PeekedCookBook.Of(book);
        var traits = new Dictionary<string, string> { ["bg"] = "b", ["body"] = "x" };

        var exact = SelectionOdds.Of(peeked, "cat", traits, Array.Empty<string>());
        var bounded = SelectionOdds.Of(peeked, "cat", traits, Array.Empty<string>(), enumerationBudget: 2);

        Assert.Equal(SpaceCertainty.AtLeast, bounded.Certainty);
        Assert.Equal(0.25, bounded.Probability, 12);                 // the unnormalised numerator
        Assert.True(bounded.Probability < exact.Probability, "a floor must be below the truth");
        Assert.Equal("at most 1 in 4", SelectionOdds.Describe(bounded, asOdds: true));
        Assert.Equal("at least 25%", SelectionOdds.Describe(bounded, asOdds: false));
    }

    [Theory]
    [InlineData(0.25, true, "1 in 4")]
    [InlineData(0.25, false, "25%")]
    [InlineData(1.0 / 43200, true, "1 in 43,200")]
    [InlineData(1.0 / 43200, false, "0.002315%")]
    [InlineData(1e-15, true, "1 in over a trillion")]
    public void A_figure_reads_the_same_everywhere_it_is_printed(double p, bool asOdds, string expected) =>
        Assert.Equal(expected, SelectionOdds.Describe(new SelectionChance(p, SpaceCertainty.Exact), asOdds));

    [Fact]
    public void A_BOUND_PAST_A_TRILLION_DOES_NOT_TIGHTEN_ITSELF_ON_THE_WAY_OUT()
    {
        // THE ONE BRANCH OF Describe THAT OVERCLAIMED, and the only one no test covered: the
        // trillion case was exercised unbounded and never bounded.
        //
        // `bounded` means the PROBABILITY is a floor, so the odds are a CEILING - all that is known
        // is `odds <= one`, and `one` is itself at least a trillion. Saying "at most 1 in a
        // trillion" substitutes a trillion for `one` and states a TIGHTER bound than the arithmetic
        // supports: true odds of one in three trillion satisfy what was computed and are denied by
        // what was printed. Both sides say "over a trillion" now, and only the "at most"
        // distinguishes them - which is the direction this method exists to get right.
        var bounded = new SelectionChance(1e-15, SpaceCertainty.AtLeast);

        Assert.Equal("at most 1 in over a trillion", SelectionOdds.Describe(bounded, asOdds: true));
        Assert.DoesNotContain("in a trillion", SelectionOdds.Describe(bounded, asOdds: true),
            StringComparison.Ordinal);

        // The unbounded wording is unchanged, so the pair still differs only by its direction.
        Assert.Equal("1 in over a trillion",
            SelectionOdds.Describe(new SelectionChance(1e-15, SpaceCertainty.Exact), asOdds: true));
    }

    [Fact]
    public void A_budget_the_caller_got_wrong_is_refused_rather_than_downgrading_the_whole_book()
    {
        // Left unchecked it does not merely give a wrong answer. The walk is gated on
        // `combos >= budget`, which a zero or negative budget makes true on the first layer, so
        // every recipe reports itself unwalkable and the book silently comes back as a floor -
        // indistinguishable from a book that genuinely could not be counted.
        using var book = Ruled();
        var peeked = PeekedCookBook.Of(book);

        Assert.Throws<ArgumentOutOfRangeException>(() => SelectionOdds.Prepare(peeked, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SelectionOdds.Prepare(peeked, -1));
    }

    /// <summary>
    /// THE ORACLE: four thousand real rolls, counted.
    /// </summary>
    /// <remarks>
    /// Every test above compares one formula against a number worked out by hand, which cannot catch
    /// a shared misreading of how the generator actually rolls. This runs <see cref="Generator"/>
    /// itself — same seed every time, so it is deterministic rather than statistical — and counts how
    /// often the combination came up. The book has a rule AND an optional layer, which is exactly
    /// the shape the independence product gets wrong, and the naive figure is asserted to be outside
    /// the tolerance so the test cannot pass on it.
    /// </remarks>
    [Fact]
    public void The_predicted_chance_matches_what_the_generator_actually_rolls()
    {
        using var book = Book(
            new Dictionary<string, double> { ["cat"] = 70, ["robot"] = 30 },
            Recipe("cat",
                new[]
                {
                    new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "a"),
                        new[] { new RuleTarget("body", "x") }),
                },
                new Dictionary<string, double> { ["hat"] = 40 },
                Ing("bg", ("a", 1), ("b", 3)), Ing("body", ("x", 1), ("y", 1)), Ing("hat", ("cap", 1))),
            Recipe("robot", null, null, Ing("bg", ("a", 1), ("b", 1))));

        var predicted = Of(book, "cat",
            new Dictionary<string, string> { ["bg"] = "b", ["body"] = "x", ["hat"] = "cap" });
        Assert.True(predicted.IsExact);

        const int rolls = 4000;
        // Uniqueness OFF: dedup is a property of a collection being filled, not of a roll, and with
        // it on this book's small space would reject most of these rolls and skew the count.
        using var set = Generator.Generate(book, new GenerateOptions(rolls, "odds-oracle", EnforceUniqueDna: false));

        int hits = set.Assets.Count(a =>
            a.RecipeId == "cat"
            && a.AbsentLayers.Count == 0
            && a.Traits.Any(t => t.IngredientId == "bg" && t.VariantId == "b")
            && a.Traits.Any(t => t.IngredientId == "body" && t.VariantId == "x"));

        double observed = (double)hits / rolls;
        double naive = 0.7 * 0.75 * 0.5 * 0.6;      // the independence product, which is NOT the truth

        Assert.True(Math.Abs(observed - predicted.Probability) < 0.02,
            $"predicted {predicted.Probability:F4}, rolled {observed:F4} over {rolls}");
        Assert.True(Math.Abs(observed - naive) > 0.02,
            $"the naive product {naive:F4} is inside the tolerance, so this test proves nothing");
    }

    /// <summary>
    /// The whole way through: cook a Set, read it back off disk, and price one of its assets from
    /// the book. This is the path the Set browser takes, and it is the only one that exercises
    /// reading a selection back out of the metadata — the Type row skipped, absence read from
    /// <c>absentLayers</c> rather than from the placeholder the rarity table counts it under.
    /// </summary>
    [Fact]
    public void An_asset_read_back_off_disk_is_priced_from_the_book()
    {
        using var book = Book(
            new Dictionary<string, double> { ["cat"] = 1 },
            Recipe("cat", null, new Dictionary<string, double> { ["hat"] = 40 },
                Ing("bg", ("a", 1), ("b", 1)), Ing("hat", ("cap", 1))));

        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using (var set = Generator.Generate(book, new GenerateOptions(8, "disk", EnforceUniqueDna: false)))
                SetWriter.Write(set, dir, pack: false);

            using var loaded = SetReader.Read(dir);
            var peeked = PeekedCookBook.Of(book);

            foreach (var item in loaded.Items)
            {
                var chance = SelectionOdds.Of(peeked, item);
                Assert.True(chance.IsExact, $"asset {item.Number} could not be priced");

                // Two shapes only: wearing the hat (0.5 x 0.6) or not (0.5 x 0.4).
                bool bare = item.AbsentLayers is { Count: > 0 };
                Assert.Equal(bare ? 0.2 : 0.3, chance.Probability, 12);
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
