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
        // A dynamic layer with no colour entries has no reachable buckets, so the total is 0.
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
        // Combinations saturate the cap (inexact), but a dynamic layer with no entries has zero
        // buckets, so the product falls back to 0 — under the cap. Re-deriving exactness as
        // "total < cap" then claims the count is exact when the count itself already gave up.
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

        var count = UniqueSpace.Count(book, cap: 500);

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
    public void Zero_weight_colour_entry_contributes_no_buckets()
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
