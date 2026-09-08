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

    // A custom ingredient whose variants carry explicit weights, so a shelved (zero-weight)
    // variant can be told apart from a rollable one.
    private static LoadedIngredient CustomWeighted(string id, params (string Id, double Weight)[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variants.Select(v => new Variant(v.Id, v.Id, v.Weight)).ToList()),
        VariantImages = variants.ToDictionary(
            v => v.Id, _ => new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255))),
    };

    private static LoadedIngredient StaticIng(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Static,
            new Colorization(ColorModel.Hsv, 10, 10, new[] { new ColorEntry(1, null, "hex:d6249f") }),
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(2, 2, 2, 255))),
    };

    private static LoadedIngredient Dynamic(string id, ColorRange range, int hueQ, int satQ, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, hueQ, satQ, new[] { new ColorEntry(1, range, null) }),
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(2, 2, 2, 255))),
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

    private static LoadedCookBook BookWithWeights(
        IReadOnlyDictionary<string, double> weights, params LoadedRecipe[] recipes) => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(2, 2),
            new Collection("B", "d", "B"), weights),
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
    public void Zero_weight_recipe_is_excluded_from_the_cookbook_total()
    {
        // Same two recipes as Recipes_sum_together (2 + 3 = 5), but the second is shelved with a
        // zero weight, so the cookbook can never roll it: the total counts only the rollable one.
        var book = BookWithWeights(
            new Dictionary<string, double> { ["cat"] = 1.0, ["robot"] = 0.0 },
            Recipe("cat", Array.Empty<IncompatibilityRule>(), Custom("bg", "a", "b")),
            Recipe("robot", Array.Empty<IncompatibilityRule>(), Custom("bg", "x", "y", "z")));

        Assert.Equal(2, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void Zero_weight_recipe_still_reports_its_own_space()
    {
        // The total excludes a shelved recipe, but its per-recipe space is still recorded so a
        // caller can see what it would contribute if enabled.
        var book = BookWithWeights(
            new Dictionary<string, double> { ["cat"] = 1.0, ["robot"] = 0.0 },
            Recipe("cat", Array.Empty<IncompatibilityRule>(), Custom("bg", "a", "b")),
            Recipe("robot", Array.Empty<IncompatibilityRule>(), Custom("bg", "x", "y", "z")));

        Assert.Equal(3, UniqueSpace.Count(book)["robot"].Total);
    }

    [Fact]
    public void Static_layer_contributes_one_bucket_not_more()
    {
        // A static layer's color is constant, so it adds no cross-asset uniqueness:
        // 2 bg x 2 skin variants = 4, NOT 4 x (color buckets).
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Custom("bg", "a", "b"), StaticIng("skin", "p", "q")));

        Assert.Equal(4, UniqueSpace.Count(book).Total);
    }

    /// <summary>Every distinct DNA reachable by rolling this book many times.</summary>
    private static int RollDistinctDna(LoadedCookBook book, int rolls)
    {
        var recipe = book.Recipes[0];
        var rng = new SplitMix64Rng(SeedHash.ToUlong("reachability"));
        var seen = new HashSet<string>();

        for (int i = 0; i < rolls; i++)
        {
            var parts = new List<LayerSelection>();
            foreach (var layerId in recipe.Manifest.LayerOrder)
            {
                var ing = recipe.Ingredients.First(x => x.Manifest.Id == layerId);
                var weights = ing.Manifest.Variants.ToDictionary(v => v.Id, v => v.Weight);
                string variantId = WeightedRoller.Roll(weights, rng);
                var col = ing.Manifest.Colorization;
                if (ing.Manifest.Kind == LayerKind.Dynamic)
                {
                    var rolled = ColorRoller.Roll(col!, rng);
                    parts.Add(new LayerSelection(layerId, variantId, rolled.H, rolled.S,
                        col!.HueQuantize, col.SatQuantize));
                }
                else if (ing.Manifest.Kind == LayerKind.Static)
                {
                    var f = ColorRoller.FromFixed(col!.Entries[0].Fixed!, col.Model);
                    parts.Add(new LayerSelection(layerId, variantId, f.H, f.S,
                        col.HueQuantize, col.SatQuantize));
                }
                else
                {
                    parts.Add(new LayerSelection(layerId, variantId, null, null, 1, 1));
                }
            }
            seen.Add(Dna.Compute(recipe.Manifest.Id, parts));
        }

        return seen.Count;
    }

    [Fact]
    public void Dynamic_layer_count_matches_the_dna_actually_reachable_on_a_bucket_boundary()
    {
        // hue 0..90 at quantize 30: the roller samples Min + r*(Max-Min) with r in [0,1),
        // so 90 itself is unreachable => buckets 0,1,2 only. sat 0..0 => 1 bucket.
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Dynamic("aura", new ColorRange(0, 90, 0, 0), hueQ: 30, satQ: 10, "glow")));

        Assert.Equal(3, UniqueSpace.Count(book).Total);
        Assert.Equal(3, RollDistinctDna(book, 200_000));
    }

    [Fact]
    public void Dynamic_layer_count_matches_reachable_dna_when_the_range_misses_the_boundary()
    {
        // hue 0..85 at quantize 30 => buckets 0,1,2 (85 falls inside bucket 2).
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Dynamic("aura", new ColorRange(0, 85, 0, 0), hueQ: 30, satQ: 10, "glow")));

        Assert.Equal(3, UniqueSpace.Count(book).Total);
        Assert.Equal(3, RollDistinctDna(book, 200_000));
    }

    [Fact]
    public void Degenerate_range_still_counts_its_single_bucket()
    {
        // Min == Max: r*(Max-Min) == 0, so the endpoint IS the only reachable value.
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Dynamic("aura", new ColorRange(90, 90, 50, 50), hueQ: 30, satQ: 10, "glow")));

        Assert.Equal(1, UniqueSpace.Count(book).Total);
        Assert.Equal(1, RollDistinctDna(book, 1_000));
    }

    [Fact]
    public void Sat_axis_counts_only_reachable_buckets()
    {
        // sat 0..30 at quantize 10 => buckets 0,1,2 (30 unreachable). hue 0..0 => 1 bucket.
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Dynamic("aura", new ColorRange(0, 0, 0, 30), hueQ: 30, satQ: 10, "glow")));

        Assert.Equal(3, UniqueSpace.Count(book).Total);
        Assert.Equal(3, RollDistinctDna(book, 200_000));
    }

    [Fact]
    public void Generating_exactly_the_counted_space_succeeds_and_one_more_fails()
    {
        // The count is a promise: N unique DNA must be generable, N+1 must not.
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Dynamic("aura", new ColorRange(0, 90, 0, 0), hueQ: 30, satQ: 10, "glow")));

        long total = UniqueSpace.Count(book).Total;

        using (var set = Generator.Generate(book, new GenerateOptions((int)total, "seed")))
            Assert.Equal(total, set.Assets.Select(a => a.Dna).Distinct().Count());

        Assert.Throws<UniqueSpaceExhaustedException>(
            () => Generator.Generate(book, new GenerateOptions((int)total + 1, "seed")));
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
    public void Zero_buckets_are_not_reported_as_zero_legal_combinations()
    {
        // A dynamic layer with no color entries has no reachable buckets, so the total is 0.
        // The recipe has NO rules, so its legal combinations must still count 1 — conflating the
        // two zeroes makes Generator blame rules that do not exist.
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 30, 10, Array.Empty<ColorEntry>()),
                new[] { new Variant("glow", "glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["glow"] = new Image<Rgba32>(2, 2, new Rgba32(2, 2, 2, 255)),
            },
        };
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(), ing));

        var count = UniqueSpace.Count(book);
        Assert.Equal(0, count.Total);
        Assert.Equal(1, count["cat"].Combos);
    }

    [Fact]
    public void Inverted_range_does_not_collapse_the_space_to_zero()
    {
        // An inverted range is author error that Validator rejects outright. UniqueSpace must
        // still not report a self-contradicting empty space for it, since Generator reads a
        // zero total on a rules-free recipe as "rules exclude everything".
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Dynamic("aura", new ColorRange(350, 10, 0, 0), hueQ: 30, satQ: 10, "glow")));

        Assert.NotEqual(0, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void Zero_legal_combinations_are_surfaced_as_zero_combos()
    {
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("body", "fox"),
                new[] { new RuleTarget("hat", "cap") }),
        };
        var book = Book(Recipe("cat", rules, Custom("body", "fox"), Custom("hat", "cap")));

        Assert.Equal(0, UniqueSpace.Count(book)["cat"].Combos);
    }

    [Fact]
    public void A_recipe_whose_combinations_saturated_is_never_reported_exact()
    {
        // Combinations saturate (inexact), but a dynamic layer with no entries has zero buckets,
        // so the product falls back to 0 - UNDER the limit. Re-deriving exactness as "total <
        // limit" then claims the count is exact when the count itself already gave up.
        //
        // Driven by a low reportingCeiling rather than a low cap: combinations are a product, and
        // products are what the ceiling governs now. The invariant is unchanged - it was never
        // about the number, only about not losing the signal.
        var many = Enumerable.Range(0, 40).Select(i => $"v{i}").ToArray();
        var empty = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 30, 10, Array.Empty<ColorEntry>()),
                new[] { new Variant("glow", "glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["glow"] = new Image<Rgba32>(2, 2, new Rgba32(2, 2, 2, 255)),
            },
        };
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Custom("bg", many), Custom("body", many), empty));

        var count = UniqueSpace.Count(book, reportingCeiling: 500);

        Assert.Equal(0, count["cat"].Total);
        Assert.False(count["cat"].IsExact);
    }

    // --- weights the rollers can never land on ---

    [Fact]
    public void Zero_weight_variant_is_not_counted()
    {
        // WeightedRoller returns the first variant whose running weight total passes the sample,
        // so a zero-weight variant never advances that total and can never be rolled. Counting it
        // promises DNA that does not exist.
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            CustomWeighted("bg", ("a", 1), ("b", 1), ("c", 0))));

        Assert.Equal(2, UniqueSpace.Count(book).Total);
        Assert.Equal(2, RollDistinctDna(book, 10_000));
    }

    [Fact]
    public void Generating_the_counted_space_of_a_book_with_a_shelved_variant_succeeds()
    {
        // The count is a promise, and a shelved variant must not inflate it: 2 must generate,
        // and asking for the third must name 2 as the true maximum rather than contradict itself.
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            CustomWeighted("bg", ("a", 1), ("b", 1), ("c", 0))));

        using (var set = Generator.Generate(book, new GenerateOptions(2, "seed")))
            Assert.Equal(2, set.Assets.Select(a => a.Dna).Distinct().Count());

        var ex = Assert.Throws<UniqueSpaceExhaustedException>(
            () => Generator.Generate(book, new GenerateOptions(3, "seed")));

        Assert.Equal(2, ex.Available);
        Assert.True(ex.IsExact);
    }

    [Fact]
    public void Zero_weight_color_entry_contributes_no_buckets()
    {
        // ColorRoller.PickEntry accumulates weights the same way, so a zero-weight entry is
        // unreachable and its buckets belong to no asset. Only the hue 0..90 entry can be rolled
        // (3 buckets at quantize 30); the shelved 180..270 entry would have added 3 more.
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 30, 10, new[]
                {
                    new ColorEntry(1, new ColorRange(0, 90, 0, 0), null),
                    new ColorEntry(0, new ColorRange(180, 270, 0, 0), null),
                }),
                new[] { new Variant("glow", "glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["glow"] = new Image<Rgba32>(2, 2, new Rgba32(2, 2, 2, 255)),
            },
        };
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(), ing));

        Assert.Equal(3, UniqueSpace.Count(book).Total);
        Assert.Equal(3, RollDistinctDna(book, 200_000));
    }

    [Fact]
    public void A_variant_list_that_is_entirely_shelved_is_still_a_validation_problem()
    {
        // Zero-weight variants are legal individually, so nothing may quietly reduce an
        // ingredient to no rollable variants at all — that is a broken book, and it is the
        // zero-total-weight check that must catch it rather than the space collapsing to 0.
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            CustomWeighted("bg", ("a", 0), ("b", 0))));

        Assert.Contains(Validator.Validate(book),
            p => p.Contains("zero total variant weight", StringComparison.OrdinalIgnoreCase));
    }

    // --- Count must never throw on a mid-edit, transiently invalid book (finding 1) ---

    [Fact]
    public void Duplicate_ingredient_id_does_not_throw_and_resolves_last_one_wins()
    {
        // Two ingredients sharing id "bg". Count must not throw ArgumentException the way a bare
        // ToDictionary would; it resolves duplicate-tolerantly (last wins), same house style as
        // Validator.CheckRecipe's own ingById.
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Custom("bg", "a"), Custom("bg", "b") },
        };
        var book = Book(recipe);

        Assert.Equal(1, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void Dangling_layerOrder_reference_does_not_throw_and_reports_uncountable()
    {
        // layerOrder names an ingredient the recipe does not carry. Count must not throw
        // KeyNotFoundException the way ingById[id] would; it reports the recipe as uncountable
        // (zero total, zero combos, not exact) rather than crashing or claiming an honest zero.
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "cat", new[] { "missing" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = Array.Empty<LoadedIngredient>(),
        };
        var book = Book(recipe);

        var count = UniqueSpace.Count(book);

        Assert.Equal(0, count.Total);
        Assert.False(count.IsExact);
        Assert.Equal(0, count["cat"].Combos);
        Assert.False(count["cat"].IsExact);
    }

    [Fact]
    public void Duplicate_variant_id_is_counted_once_not_once_per_entry()
    {
        // Two variant entries sharing id "a" roll to the same DNA regardless of which entry the
        // roller lands on (Dna records the variant id, not the entry). Counting both promises
        // one more distinct DNA than the id space actually holds.
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                new[] { new Variant("a", "A1", 1), new Variant("a", "A2", 1), new Variant("b", "B", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
                ["b"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
            },
        };
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(), ing));

        Assert.Equal(2, UniqueSpace.Count(book).Total);
    }

    [Fact]
    public void Huge_space_is_capped_and_reported_inexact()
    {
        // Two dynamic layers over the whole colour wheel at quantize 1 reach 36,000 buckets each.
        // Filling that set is real enumeration, so it is the BUDGET that stops it, and the count
        // reports itself inexact.
        var many = Enumerable.Range(0, 40).Select(i => $"v{i}").ToArray();
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Dynamic("a", new ColorRange(0, 360, 0, 100), hueQ: 1, satQ: 1, many),
            Dynamic("b", new ColorRange(0, 360, 0, 100), hueQ: 1, satQ: 1, many)));

        var count = UniqueSpace.Count(book, enumerationBudget: 1000);

        Assert.False(count.IsExact);

        // And the total is NOT clamped to the budget. It is a lower bound built from an under-count
        // of buckets, which is what "more than Total" has always meant - the old code threw that
        // away and reported the cap itself, so a book with billions of assets and a book with a
        // million read identically.
        Assert.True(count.Total > 1000,
            $"the total should be a lower bound, not the budget; it is {count.Total}");
    }

    // ---- the budget and the ceiling are two limits, and only one of them is expensive -----------

    [Fact]
    public void A_space_far_past_a_million_still_counts_exactly_when_nothing_is_walked()
    {
        // THE POINT OF THE SPLIT. One cap used to govern both the walking and the answer, so this
        // book - which factorizes, and whose whole count is a handful of multiplies - reported
        // "more than 1000000". Every layer added to the built-in demo therefore cost a re-tune of
        // its quantize steps to stay under a ceiling that was defending nothing.
        var many = Enumerable.Range(0, 40).Select(i => $"v{i}").ToArray();
        var book = Book(Recipe("cat", Array.Empty<IncompatibilityRule>(),
            Custom("a", many), Custom("b", many), Custom("c", many), Custom("d", many)));

        var count = UniqueSpace.Count(book);

        Assert.True(count.IsExact, "nothing here needs walking, so nothing should give up");
        Assert.Equal(40L * 40 * 40 * 40, count.Total);          // 2,560,000
        Assert.True(count.Total > UniqueSpace.DefaultEnumerationBudget);
    }

    private static LoadedCookBook RulesTooDenseToWalk()
    {
        var many = Enumerable.Range(0, 40).Select(i => $"v{i}").ToArray();
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("a", "v0"), new[] { new RuleTarget("b", "v0") }),
        };
        return Book(Recipe("cat", rules,
            Custom("a", many), Custom("b", many), Custom("c", many), Custom("d", many)));
    }

    [Fact]
    public void The_budget_still_stops_a_walk_that_would_be_expensive()
    {
        // The other half of the split: the budget is not decorative. With rules present the count
        // enumerates one selection at a time, and THAT cost is real however small the resulting
        // number is - so a book with more combinations than the budget still gives up.
        var count = UniqueSpace.Count(RulesTooDenseToWalk());

        Assert.False(count.IsExact);
    }

    [Fact]
    public void A_walk_that_was_skipped_reports_a_CEILING_and_says_so()
    {
        // THE BUG THIS MODEL EXISTS FOR. This used to report the BUDGET as the total with
        // IsExact false, which every surface rendered as "more than 1,000,000" - a floor. Rules can
        // only REMOVE selections, so that was the one inexact result whose truth lies in the other
        // direction: a book like this one might admit four hundred assets, and the report claimed
        // more than a million.
        var count = UniqueSpace.Count(RulesTooDenseToWalk());

        Assert.Equal(SpaceCertainty.AtMost, count.Certainty);

        // And the ceiling is the UNCONSTRAINED product - a real fact about the book, computed in
        // four multiplies - rather than the budget, which is a fact about this counter.
        Assert.Equal(40L * 40 * 40 * 40, count.Total);
        Assert.NotEqual(UniqueSpace.DefaultEnumerationBudget, count.Total);
        Assert.StartsWith("at most ", SpaceText.Describe(count), StringComparison.Ordinal);
    }

    [Fact]
    public void An_upper_bound_is_only_reported_when_the_number_it_bounds_was_itself_counted()
    {
        // The ceiling is the product of combinations and colour buckets, so it is a bound on the
        // truth only if those buckets were counted. Squeeze the budget until the bucket sets give up
        // too and there is nothing left to say: an under-count multiplied out bounds the answer in
        // NEITHER direction, which is not a smaller claim than "at most" - it is a different one.
        var many = Enumerable.Range(0, 40).Select(i => $"v{i}").ToArray();
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("a", "v0"), new[] { new RuleTarget("b", "v0") }),
        };
        var book = Book(Recipe("cat", rules,
            Dynamic("a", new ColorRange(0, 360, 0, 100), hueQ: 1, satQ: 1, many),
            Custom("b", many), Custom("c", many), Custom("d", many)));

        var count = UniqueSpace.Count(book, enumerationBudget: 100);

        Assert.Equal(SpaceCertainty.Unknown, count.Certainty);
        Assert.False(count.IsCountable);
    }

    // ---- the lattice: what a SUM of two spaces is ------------------------------------------------

    [Theory]
    [InlineData(SpaceCertainty.Exact, SpaceCertainty.Exact, SpaceCertainty.Exact)]
    [InlineData(SpaceCertainty.Exact, SpaceCertainty.AtLeast, SpaceCertainty.AtLeast)]
    [InlineData(SpaceCertainty.AtMost, SpaceCertainty.Exact, SpaceCertainty.AtMost)]
    [InlineData(SpaceCertainty.AtLeast, SpaceCertainty.AtLeast, SpaceCertainty.AtLeast)]
    [InlineData(SpaceCertainty.AtMost, SpaceCertainty.AtMost, SpaceCertainty.AtMost)]
    [InlineData(SpaceCertainty.Unknown, SpaceCertainty.Exact, SpaceCertainty.Unknown)]
    public void Summing_two_spaces_keeps_whichever_direction_they_agree_on(
        SpaceCertainty a, SpaceCertainty b, SpaceCertainty expected)
    {
        Assert.Equal(expected, UniqueSpace.Combine(a, b));
        Assert.Equal(expected, UniqueSpace.Combine(b, a));       // addition is commutative
    }

    [Fact]
    public void A_floor_summed_with_a_ceiling_is_nothing_at_all()
    {
        // The case an &= over a bool could not express, and the reason Combine is a function. One
        // recipe admits AT LEAST a million and another AT MOST a million; their sum is bounded in
        // neither direction, and picking either would be inventing a fact.
        Assert.Equal(SpaceCertainty.Unknown,
            UniqueSpace.Combine(SpaceCertainty.AtLeast, SpaceCertainty.AtMost));
        Assert.Equal(SpaceCertainty.Unknown,
            UniqueSpace.Combine(SpaceCertainty.AtMost, SpaceCertainty.AtLeast));
    }

    [Fact]
    public void Over_sums_only_the_recipes_it_is_given()
    {
        // What Generator asks when a run fails: a shelved recipe is never rolled, so its space must
        // not inflate a maximum the run could not have reached.
        var count = UniqueSpace.Count(BookWithWeights(
            new Dictionary<string, double> { ["one"] = 1, ["two"] = 1 },
            Recipe("one", Array.Empty<IncompatibilityRule>(), Custom("a", "x", "y")),
            Recipe("two", Array.Empty<IncompatibilityRule>(), Custom("b", "x", "y", "z"))));

        Assert.Equal(5, count.Over(new[] { "one", "two" }).Total);
        Assert.Equal(2, count.Over(new[] { "one" }).Total);
        Assert.Equal(0, count.Over(Array.Empty<string>()).Total);
        Assert.Equal(SpaceCertainty.Exact, count.Over(new[] { "one" }).Certainty);
    }

    [Fact]
    public void A_book_whose_recipes_each_hold_a_vast_space_saturates_rather_than_overflowing()
    {
        // The ceiling is long.MaxValue now, so the old "add first, clamp afterwards" would wrap and
        // report a NEGATIVE space. Two recipes each near the top of the range is the case that used
        // to be impossible to reach and now is not.
        var many = Enumerable.Range(0, 60).Select(i => $"v{i}").ToArray();
        LoadedRecipe Huge(string id) => Recipe(id, Array.Empty<IncompatibilityRule>(),
            Dynamic(id + "x", new ColorRange(0, 360, 0, 100), hueQ: 1, satQ: 1, many),
            Dynamic(id + "y", new ColorRange(0, 360, 0, 100), hueQ: 1, satQ: 1, many),
            Dynamic(id + "z", new ColorRange(0, 360, 0, 100), hueQ: 1, satQ: 1, many));

        var count = UniqueSpace.Count(BookWithWeights(
            new Dictionary<string, double> { ["one"] = 1, ["two"] = 1 }, Huge("one"), Huge("two")));

        Assert.True(count.Total > 0, $"the total wrapped to {count.Total}");
        Assert.False(count.IsExact);
    }

    // ---- CountColors: the one figure allowed to answer "how many colors?" -----------------------
    //
    // The Ingredient editor used to print HueQuantize * SatQuantize, the product of the two STEP
    // sizes. That is not a count of anything: it ignores the ranges, and it grows as the steps
    // coarsen, which can only ever remove colors. These pin the count to buckets.

    private static Colorization Ranged(double hMin, double hMax, double sMin, double sMax,
                                       int hueQ, int satQ) =>
        new(ColorModel.Hsv, hueQ, satQ,
            new[] { new ColorEntry(1, new ColorRange(hMin, hMax, sMin, sMax), null) });

    [Fact]
    public void Color_count_is_buckets_not_the_product_of_the_two_steps()
    {
        // The demo book's Background layer: the whole hue circle in 30-degree steps is 12 buckets,
        // and 25..70 saturation in steps of 20 is 3 - so 36, where step-multiplication said 600.
        var (count, exact) = UniqueSpace.CountColors(Ranged(0, 360, 25, 70, 30, 20));

        Assert.True(exact);
        Assert.Equal(36, count);
        Assert.NotEqual(30 * 20, count);
    }

    [Fact]
    public void Coarser_quantize_never_admits_more_colors()
    {
        var fine = UniqueSpace.CountColors(Ranged(0, 360, 0, 100, 10, 10)).Count;
        var coarse = UniqueSpace.CountColors(Ranged(0, 360, 0, 100, 30, 20)).Count;

        Assert.True(coarse < fine,
            $"a coarser step must not admit more colors: fine={fine}, coarse={coarse}");
    }

    [Fact]
    public void Color_count_reads_the_range_not_only_the_step()
    {
        // Same steps, a quarter of the hue circle: the count has to fall with the range. The old
        // formula could not see this at all - both of these read 600.
        int whole = (int)UniqueSpace.CountColors(Ranged(0, 360, 25, 70, 30, 20)).Count;
        int quarter = (int)UniqueSpace.CountColors(Ranged(0, 90, 25, 70, 30, 20)).Count;

        Assert.Equal(36, whole);
        Assert.Equal(9, quarter);
    }

    [Fact]
    public void A_fixed_color_admits_exactly_one()
    {
        var fixedSpec = new Colorization(ColorModel.Hsv, 30, 20,
            new[] { new ColorEntry(1, null, "hex:d6249f") });

        Assert.Equal((1L, true), UniqueSpace.CountColors(fixedSpec));
    }

    [Fact]
    public void Count_colors_agrees_with_the_space_a_book_reports()
    {
        // CountColors is the same counter Count multiplies per dynamic layer, and the editor now
        // prints it - so the editor's figure and the CookBook panel's cannot drift apart.
        var colorization = Ranged(0, 360, 25, 70, 30, 20);
        var dynamic = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Dynamic, colorization,
                new[] { new Variant("only", "only", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["only"] = new Image<Rgba32>(2, 2, new Rgba32(9, 9, 9, 255)) },
        };
        using var book = Book(Recipe("r", Array.Empty<IncompatibilityRule>(), dynamic));

        // One variant, so the whole space IS the color count.
        Assert.Equal(UniqueSpace.CountColors(colorization).Count, UniqueSpace.Count(book).Total);
    }
}
