using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// One deterministic roll of a whole Recipe into the layers a stack preview draws — what
/// <c>nfty preview cat.rcp --seed alpha</c> is.
///
/// <para>The properties worth pinning are all about <b>what the roll costs</b> rather than what it
/// returns. The draw order is the determinism contract: one weighted draw per layer walking
/// <c>layerOrder</c> bottom-first, then that layer's colour draws — and a Static layer takes none,
/// because generation's does not. Every one of those is asserted against a <see cref="ScriptedRng"/>
/// with a counted, fixed sequence rather than against a seed, so the assertions say exactly which
/// draw reached which layer instead of merely that two runs agreed.</para>
/// </summary>
public class StackRollTests
{
    private static readonly Dimensions Canvas = new(2, 2);

    // Two equally-weighted variants "a" and "b": WeightedRoller walks keys in ordinal order over a
    // total of 2, so a draw below 0.5 picks "a" and one at or above it picks "b". That makes a
    // scripted sequence readable as "this layer got 'a', that one got 'b'".
    private static LoadedIngredient Custom(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(
            v => v, v => new Image<Rgba32>(2, 2, new Rgba32((byte)v[0], 0, 0, 255)), StringComparer.Ordinal),
    };

    private static LoadedIngredient Static(string id, string fixedSpec, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Static,
            new Colorization(ColorModel.Hsv, 12, 5, new[] { new ColorEntry(1, null, fixedSpec) }),
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(
            v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(160, 160, 160, 255)), StringComparer.Ordinal),
    };

    private static LoadedIngredient Dynamic(
        string id, ColorModel model = ColorModel.Hsv, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic,
            new Colorization(model, 12, 5,
                new[] { new ColorEntry(1, new ColorRange(0, 360, 80, 100), null) }),
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(
            v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(200, 200, 200, 255)), StringComparer.Ordinal),
    };

    private static LoadedRecipe Recipe(IReadOnlyList<IncompatibilityRule> rules, params LoadedIngredient[] ings) =>
        new()
        {
            Manifest = new RecipeManifest("cat", "Cat", ings.Select(i => i.Manifest.Id).ToList(), rules),
            Ingredients = ings,
        };

    private static LoadedRecipe Recipe(params LoadedIngredient[] ings) =>
        Recipe(Array.Empty<IncompatibilityRule>(), ings);

    // ---- the draw order, which is the determinism contract ---------------------------------------

    /// <summary>
    /// The one the spec's Correction rests on, stated at preview level: <b>moving a layer moves which
    /// draw reaches it.</b> Same script, same two layers, same two variants each — only the order
    /// differs, and that alone swaps which layer gets which variant. A seed-based version of this
    /// would only show that the two runs differed; the script shows why.
    /// </summary>
    [Fact]
    public void Reordering_the_stack_moves_which_draw_reaches_which_layer()
    {
        using var one = Custom("one", "a", "b");
        using var two = Custom("two", "a", "b");

        var bottomFirst = Recipe(one, two);
        var rolled = StackRoll.ForRecipe(bottomFirst, new ScriptedRng(0.1, 0.9));
        Assert.Equal(new[] { "a", "b" }, rolled.Select(l => l.VariantId));

        // The same two layers, the same two draws — only "two" now sits at depth 1.
        var topFirst = new LoadedRecipe
        {
            Manifest = LayerDepth.MoveTo(bottomFirst.Manifest, "two", 1),
            Ingredients = bottomFirst.Ingredients,
        };
        var reRolled = StackRoll.ForRecipe(topFirst, new ScriptedRng(0.1, 0.9));

        Assert.Equal(new[] { "two", "one" }, reRolled.Select(l => l.Ingredient.Manifest.Id));
        Assert.Equal(new[] { "a", "b" }, reRolled.Select(l => l.VariantId));

        // Which means "one" — the layer that did not move — came out differently anyway.
        Assert.Equal("a", rolled.Single(l => l.Ingredient.Manifest.Id == "one").VariantId);
        Assert.Equal("b", reRolled.Single(l => l.Ingredient.Manifest.Id == "one").VariantId);
    }

    /// <summary>
    /// The exact RNG cost per layer kind, which is what makes a seed reproduce a stack however the
    /// kinds are mixed: one variant draw each, plus a Dynamic layer's entry pick and its hue and
    /// saturation samples. A <b>Static layer takes none</b> — it resolves one fixed colour, exactly as
    /// generation does — and a Custom layer takes none either, having no colour at all.
    /// </summary>
    [Fact]
    public void Only_a_dynamic_layer_costs_colour_draws()
    {
        using var stat = Static("shades", "hex:d6249f", "v");
        using var custom = Custom("hat", "v");
        using var dyn = Dynamic("body", ColorModel.Hsv, "v");

        var rng = new ScriptedRng(0.5, 0.5, 0.5, 0.5, 0.25, 0.75);
        StackRoll.ForRecipe(Recipe(stat, custom, dyn), rng);

        // 1 variant draw each for static and custom (no colour), then the dynamic layer's variant
        // draw plus its entry pick, hue sample and saturation sample.
        Assert.Equal(1 + 1 + 4, rng.Calls);
    }

    [Fact]
    public void The_stack_comes_back_one_layer_per_layer_order_entry_bottom_first()
    {
        using var a = Custom("a", "v");
        using var b = Custom("b", "v");
        using var c = Custom("c", "v");
        var recipe = Recipe(a, b, c);

        var rolled = StackRoll.ForRecipe(recipe, StackRoll.RngFor("alpha"));

        Assert.Equal(recipe.Manifest.LayerOrder, rolled.Select(l => l.Ingredient.Manifest.Id));
        Assert.Equal(
            LayerDepth.Ordered(recipe.Manifest).Select(l => l.IngredientId),
            rolled.Select(l => l.Ingredient.Manifest.Id));
    }

    [Fact]
    public void The_same_seed_rolls_the_same_stack()
    {
        using var body = Dynamic("body", ColorModel.Hsv, "round", "slim");
        using var hat = Custom("hat", "cap", "top");
        var recipe = Recipe(body, hat);

        var first = StackRoll.ForRecipe(recipe, StackRoll.RngFor("alpha"));
        var again = StackRoll.ForRecipe(recipe, StackRoll.RngFor("alpha"));

        Assert.Equal(first.Select(l => (l.VariantId, l.ColorSpec)), again.Select(l => (l.VariantId, l.ColorSpec)));
    }

    // ---- colour, and the one place the path is lossy ---------------------------------------------

    /// <summary>
    /// A named variant costs no draw. That is what lets a reference layer — the CLI's <c>--with</c>,
    /// whose variant is PICKED rather than rolled so it holds still while the author varies the seed
    /// — sit in the same RNG stream as a rolled stack without shifting every colour after it.
    /// </summary>
    [Fact]
    public void Naming_a_variant_takes_the_variant_draw_off_the_rng()
    {
        using var hat = Custom("hat", "cap", "top");

        var rolledRng = new ScriptedRng(0.1);
        var named = new ScriptedRng();      // no draws available at all

        Assert.Equal("cap", StackRoll.ForIngredient(hat, rolledRng).VariantId);
        Assert.Equal(1, rolledRng.Calls);

        Assert.Equal("top", StackRoll.ForIngredient(hat, named, "top").VariantId);
        Assert.Equal(0, named.Calls);
    }

    [Fact]
    public void A_custom_layer_is_rolled_without_a_colour()
    {
        using var hat = Custom("hat", "cap");

        var layer = StackRoll.ForIngredient(hat, StackRoll.RngFor("alpha"));

        Assert.Null(layer.ColorSpec);
    }

    /// <summary>
    /// A Static layer's colour is its own spec string, passed through character for character rather
    /// than resolved to (H,S) and written back out. That is what keeps this kind exact where a rolled
    /// colour cannot be — the round trip through 8-bit RGB never happens for it.
    /// </summary>
    [Fact]
    public void A_static_layer_carries_its_own_fixed_spec_verbatim()
    {
        using var shades = Static("shades", "hex:d6249f", "wide");

        var layer = StackRoll.ForIngredient(shades, StackRoll.RngFor("alpha"));

        Assert.Equal("hex:d6249f", layer.ColorSpec);
    }

    /// <summary>
    /// The lossy step, bounded. A rolled (H,S) is written as a spec, and every spec resolves back
    /// through 8-bit RGB — so what the preview draws is the nearest representable colour, not the
    /// rolled one. This pins how near: a quarter-degree of hue and half a percentage point of
    /// saturation, both far below what a viewer or the DNA's own quantize buckets can resolve, and
    /// both a direct consequence of one 8-bit rounding step per channel. HSL is the looser of the two
    /// because its saturation is read off the DIFFERENCE of two rounded channels rather than off one.
    /// </summary>
    [Theory]
    [InlineData(ColorModel.Hsv)]
    [InlineData(ColorModel.Hsl)]
    public void A_rolled_colour_survives_the_spec_round_trip_to_within_an_8_bit_step(ColorModel model)
    {
        using var body = Dynamic("body", model, "v");
        var colorization = body.Manifest.Colorization!;

        // The colour draws the roll will take, taken here on an identically seeded RNG — after
        // stepping past the VARIANT draw the roll takes first, which is the draw order this whole
        // file exists to pin. Without that step the two RNGs are offset by one and the "loss" being
        // measured is a different colour entirely.
        var rng = StackRoll.RngFor("alpha");
        rng.NextDouble();
        var expected = ColorRoller.Roll(colorization, rng);

        var layer = StackRoll.ForIngredient(body, StackRoll.RngFor("alpha"));
        var actual = ColorRoller.FromFixed(layer.ColorSpec!, model);

        Assert.InRange(Math.Abs(actual.H - expected.H), 0, 0.25);
        Assert.InRange(Math.Abs(actual.S - expected.S), 0, 0.005);
    }

    /// <summary>
    /// The trap the spec format sets: an HSL spec has to pin lightness at <b>50</b>, because
    /// <c>hsl:h,s,100</c> is pure white and throws the saturation away entirely — a preview that used
    /// it would render every HSL layer grey and look like a colorizer bug.
    /// </summary>
    [Fact]
    public void An_hsl_layer_pins_lightness_at_fifty_so_its_saturation_survives()
    {
        using var body = Dynamic("body", ColorModel.Hsl, "v");

        string spec = StackRoll.ForIngredient(body, StackRoll.RngFor("alpha")).ColorSpec!;

        Assert.StartsWith("hsl:", spec, StringComparison.Ordinal);
        Assert.EndsWith(",50", spec, StringComparison.Ordinal);

        // The range rolls saturation in 80..100, so anything near zero means it was flattened.
        Assert.InRange(ColorRoller.FromFixed(spec, ColorModel.Hsl).S, 0.79, 1.0);
    }

    /// <summary>
    /// No third rendering path: the layers this rolls go through <c>VariantPreview</c> like every
    /// other preview, so a rolled stack of one is pixel-identical to that one variant rendered alone.
    /// Asserted by running both, not by reading the call chain.
    /// </summary>
    [Fact]
    public void A_rolled_stack_renders_exactly_what_VariantPreview_renders()
    {
        using var shades = Static("shades", "hex:d6249f", "wide");

        var rolled = StackRoll.ForRecipe(Recipe(shades), StackRoll.RngFor("alpha"));
        using var stacked = StackPreview.Render(Canvas, rolled);
        using var alone = VariantPreview.Render(shades, "wide", "hex:d6249f");

        for (int y = 0; y < Canvas.Height; y++)
            for (int x = 0; x < Canvas.Width; x++)
                Assert.Equal(alone[x, y], stacked[x, y]);
    }

    // ---- rules, which a preview must honour or it shows a collection that cannot exist ------------

    /// <summary>
    /// An illegal roll is discarded and re-rolled, exactly as generation discards one. A preview that
    /// skipped this would happily show a combination the collection can never contain.
    /// </summary>
    [Fact]
    public void An_illegal_roll_is_discarded_and_rolled_again()
    {
        var rule = new IncompatibilityRule(RuleType.Exclude,
            new RuleTarget("body", "round"), new[] { new RuleTarget("hat", "cap") });

        for (int i = 0; i < 25; i++)
        {
            using var body = Custom("body", "round", "slim");
            using var hat = Custom("hat", "cap", "top");

            var rolled = StackRoll.ForRecipe(
                Recipe(new[] { rule }, body, hat), StackRoll.RngFor($"seed-{i}"));

            var picked = rolled.ToDictionary(l => l.Ingredient.Manifest.Id, l => l.VariantId);
            Assert.False(picked["body"] == "round" && picked["hat"] == "cap");
        }
    }

    [Fact]
    public void A_recipe_whose_rules_exclude_every_roll_says_so_rather_than_spinning()
    {
        using var body = Custom("body", "round");
        using var hat = Custom("hat", "cap");
        var rule = new IncompatibilityRule(RuleType.Exclude,
            new RuleTarget("body", "round"), new[] { new RuleTarget("hat", "cap") });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StackRoll.ForRecipe(Recipe(new[] { rule }, body, hat), StackRoll.RngFor("alpha"), maxAttempts: 5));

        Assert.Contains("cat", ex.Message, StringComparison.Ordinal);
        Assert.Contains("rules", ex.Message, StringComparison.Ordinal);
    }

    // ---- malformed input, since a preview is what you reach for when a recipe looks wrong ---------

    [Fact]
    public void A_layer_order_entry_the_recipe_does_not_carry_is_named()
    {
        using var body = Custom("body", "v");
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", ["body", "ghost"], []),
            Ingredients = [body],
        };

        var ex = Assert.Throws<KeyNotFoundException>(() =>
            StackRoll.ForRecipe(recipe, StackRoll.RngFor("alpha")));

        Assert.Contains("ghost", ex.Message, StringComparison.Ordinal);
        Assert.Contains("cat", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_colorized_layer_with_no_colorization_block_says_which_layer()
    {
        using var broken = new LoadedIngredient
        {
            Manifest = new IngredientManifest("body", "Body", LayerKind.Dynamic, null,
                [new Variant("v", "V", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = new(2, 2) },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StackRoll.ForIngredient(broken, StackRoll.RngFor("alpha")));

        Assert.Contains("Body", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_positive_attempt_budget_is_rejected_rather_than_returning_nothing()
    {
        using var body = Custom("body", "v");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StackRoll.ForRecipe(Recipe(body), StackRoll.RngFor("alpha"), maxAttempts: 0));
    }
}
