using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The predicted share of each color name (<see cref="ColorName.Shares"/>) and its fold into the
/// book-side rarity table: exact for hand-built ranges, then checked against a seeded cook.
/// </summary>
public class ColorSharesTests
{
    private const double Tol = 1e-9;

    private static Colorization Ranged(double h0, double h1, double s0, double s1, ColorModel model = ColorModel.Hsv) =>
        new(model, 10, 10, [new ColorEntry(1, new ColorRange(h0, h1, s0, s1), null)]);

    private static void AssertShares(IReadOnlyDictionary<string, double> actual, params (string Name, double P)[] expected)
    {
        Assert.Equal(expected.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal), actual.Keys);
        foreach (var (name, p) in expected) Assert.Equal(p, actual[name], Tol);
        Assert.Equal(1.0, actual.Values.Sum(), 9);
    }

    [Fact]
    public void A_full_circle_of_full_saturation_is_twelve_equal_names()
    {
        var shares = ColorName.Shares(Ranged(0, 360, 50, 100));
        Assert.Equal(12, shares.Count);
        Assert.All(shares.Values, p => Assert.Equal(1.0 / 12, p, Tol));
        Assert.Equal(ColorName.Hues.OrderBy(h => h, StringComparer.Ordinal), shares.Keys);
    }

    [Fact]
    public void A_range_wider_than_a_turn_counts_every_lap()
    {
        var shares = ColorName.Shares(Ranged(0, 720, 50, 100));
        Assert.All(shares.Values, p => Assert.Equal(1.0 / 12, p, Tol));
        Assert.Equal(12, shares.Count);
    }

    [Fact]
    public void Red_wraps_across_zero()
    {
        // [345, 375) is exactly the Red sector.
        AssertShares(ColorName.Shares(Ranged(345, 375, 40, 80)), ("Red", 1.0));
        // [330, 360): half Magenta [315,345), half Red.
        AssertShares(ColorName.Shares(Ranged(330, 360, 40, 80)), ("Magenta", 0.5), ("Red", 0.5));
        // Negative hues wrap the same way.
        AssertShares(ColorName.Shares(Ranged(-15, 15, 40, 80)), ("Red", 1.0));
    }

    [Fact]
    public void Sector_boundaries_are_half_open()
    {
        // [15, 45) is Orange exactly; the 45 end belongs to Yellow, which is not reached.
        AssertShares(ColorName.Shares(Ranged(15, 45, 40, 80)), ("Orange", 1.0));
    }

    [Fact]
    public void Saturation_splits_into_gray_pale_and_full()
    {
        // Hue [0,30) = Red 15 + Orange 15; saturation [0,50) = gray 12, pale 23, full 15 (of 50).
        AssertShares(ColorName.Shares(Ranged(0, 30, 0, 50)),
            ("Gray", 0.24),
            ("Orange", 0.15), ("Pale Orange", 0.23),
            ("Pale Red", 0.23), ("Red", 0.15));
    }

    [Fact]
    public void Gray_takes_no_hue()
    {
        AssertShares(ColorName.Shares(Ranged(0, 360, 0, 12)), ("Gray", 1.0));
    }

    [Fact]
    public void A_degenerate_axis_is_a_point_mass_named_like_the_roller_names_it()
    {
        AssertShares(ColorName.Shares(Ranged(15, 15, 20, 20)), ("Pale Orange", 1.0));    // 15 starts Orange
        AssertShares(ColorName.Shares(Ranged(15, 15, 35, 35)), ("Orange", 1.0));          // 0.35 is the first full
        AssertShares(ColorName.Shares(Ranged(15, 15, 12, 12)), ("Pale Orange", 1.0));     // 0.12 is the first pale
        AssertShares(ColorName.Shares(Ranged(200, 200, 5, 5)), ("Gray", 1.0));
        // Degenerate hue, ranged saturation: pale 23 + full 15 + gray 12 of [0,50).
        AssertShares(ColorName.Shares(Ranged(120, 120, 0, 50)), ("Gray", 0.24), ("Pale Green", 0.46), ("Green", 0.30));
        // Ranged hue, degenerate saturation.
        AssertShares(ColorName.Shares(Ranged(330, 360, 60, 60)), ("Magenta", 0.5), ("Red", 0.5));
    }

    [Theory]
    [InlineData(ColorModel.Hsv)]
    [InlineData(ColorModel.Hsl)]
    public void A_static_layer_is_a_point_mass_on_its_fixed_color(ColorModel model)
    {
        var red = new Colorization(model, 10, 10, [new ColorEntry(1, null, "hex:ff0000")]);
        AssertShares(ColorName.Shares(red), ("Red", 1.0));
        var gray = new Colorization(model, 10, 10, [new ColorEntry(1, null, "hex:808080")]);
        AssertShares(ColorName.Shares(gray), ("Gray", 1.0));
    }

    [Fact]
    public void Entries_are_chosen_by_weight_and_a_shelved_entry_never()
    {
        var c = new Colorization(ColorModel.Hsv, 10, 10,
        [
            new ColorEntry(3, null, "hex:ff0000"),
            new ColorEntry(1, new ColorRange(105, 135, 50, 100), null),
            new ColorEntry(0, null, "hex:0000ff"),
        ]);
        AssertShares(ColorName.Shares(c), ("Green", 0.25), ("Red", 0.75));
    }

    [Fact]
    public void Two_entries_landing_on_one_name_add_up_and_the_dictionary_is_ordinal()
    {
        var c = new Colorization(ColorModel.Hsv, 10, 10,
        [
            new ColorEntry(1, null, "hex:ff0000"),
            new ColorEntry(1, new ColorRange(345, 375, 40, 80), null),
        ]);
        AssertShares(ColorName.Shares(c), ("Red", 1.0));

        var keys = ColorName.Shares(Ranged(0, 360, 0, 100)).Keys.ToList();
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys);
    }

    [Fact]
    public void No_weight_is_no_shares()
    {
        var c = new Colorization(ColorModel.Hsv, 10, 10, [new ColorEntry(0, null, "hex:ff0000")]);
        Assert.Empty(ColorName.Shares(c));
    }

    // ------------------------------------------------------------------ the rarity table

    private static LoadedIngredient Layer(string id, string name, LayerKind kind, Colorization? colorization, params string[] variants) => new()
    {
        Manifest = new IngredientManifest(id, name, kind, colorization,
            variants.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variants.ToDictionary(v => v,
            _ => new Image<Rgba32>(2, 2, kind == LayerKind.Custom ? new(9, 9, 9, 255) : new(100, 100, 100, 255))),
    };

    private static LoadedCookBook Book(double hatAbsentPct)
    {
        var body = Layer("body", "Body", LayerKind.Dynamic, new Colorization(ColorModel.Hsv, 10, 10,
        [
            new ColorEntry(3, new ColorRange(0, 360, 0, 60), null),
            new ColorEntry(1, null, "hex:00ff00"),
        ]), "a", "b");
        var hat = Layer("hat", "Hat", LayerKind.Dynamic, Ranged(200, 260, 50, 100), "h");
        var trim = Layer("trim", "Trim", LayerKind.Static, new Colorization(ColorModel.Hsv, 10, 10,
            [new ColorEntry(1, null, "hex:ff0000")]), "t");
        var bg = Layer("bg", "Bg", LayerKind.Custom, null, "c");
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "B", new Dimensions(2, 2), new Collection("B", "d", "B"),
                new Dictionary<string, double> { ["r"] = 1 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("r", "R", ["bg", "body", "hat", "trim"], [],
                        AbsentPercent: hatAbsentPct > 0 ? new Dictionary<string, double> { ["hat"] = hatAbsentPct } : null),
                    Ingredients = [bg, body, hat, trim],
                },
            ],
        };
    }

    [Fact]
    public void The_table_has_a_color_trait_per_colorized_layer_scaled_by_presence_and_a_None_row()
    {
        using var book = Book(hatAbsentPct: 40);
        var rows = RarityCalculator.Compute(book).Traits;

        // Order the cook writes: each layer's variants then its color, absent layers last.
        var order = rows.Select(t => (t.IngredientName, t.Kind)).Distinct().ToList();
        Assert.Equal(
        [
            ("Bg", TraitRowKind.Variant),
            ("Body", TraitRowKind.Variant), ("Body Color", TraitRowKind.Color),
            ("Hat", TraitRowKind.Variant), ("Hat Color", TraitRowKind.Color),
            ("Trim", TraitRowKind.Variant), ("Trim Color", TraitRowKind.Color),
            ("Hat", TraitRowKind.Absent),
        ], order);

        // No custom color row; Static is a point mass.
        Assert.DoesNotContain(rows, t => t.IngredientName == "Bg Color");
        Assert.Equal(100, rows.Single(t => t.IngredientName == "Trim Color" && t.VariantName == "Red").WithinRecipePercent, 7);

        // Hat colors: [200,260) at full saturation = Azure 10/60 (195..225 -> 200..225 = 25), Blue 30, Violet 5.
        var hatColors = rows.Where(t => t.IngredientName == "Hat Color").ToDictionary(t => t.VariantName, t => t.WithinRecipePercent);
        Assert.Equal(60.0 * 25 / 60, hatColors["Azure"], 7);
        Assert.Equal(60.0 * 30 / 60, hatColors["Blue"], 7);
        Assert.Equal(60.0 * 5 / 60, hatColors["Violet"], 7);
        Assert.Equal(60.0, hatColors.Values.Sum(), 7);   // 100 - 40 absent

        var none = rows.Single(t => t.Kind == TraitRowKind.Absent);
        Assert.Equal(MetadataTraits.Absent, none.VariantName);
        Assert.Equal("Hat", none.IngredientName);
        Assert.Equal(40, none.WithinRecipePercent, 7);

        // The hat's variant + its None row + nothing else sum to the whole layer.
        Assert.Equal(100.0, rows.Where(t => t.IngredientName == "Hat" ).Sum(t => t.WithinRecipePercent), 7);
    }

    [Fact]
    public void Recipe_weights_fold_into_color_rows_like_variants()
    {
        using var book = Book(0);
        var second = book.Recipes[0];
        var weights = new Dictionary<string, double> { ["r"] = 1, ["r2"] = 3 };
        using var two = new LoadedCookBook
        {
            Manifest = book.Manifest with { RecipeWeights = weights },
            Recipes = [book.Recipes[0], new LoadedRecipe
            {
                Manifest = second.Manifest with { Id = "r2", Name = "R2" },
                Ingredients = second.Ingredients,
            }],
        };
        var rows = RarityCalculator.Compute(two).Traits.Where(t => t.IngredientName == "Trim Color").ToList();
        Assert.Equal(25, rows.Single(t => t.RecipeId == "r").OverallPercent, 7);
        Assert.Equal(75, rows.Single(t => t.RecipeId == "r2").OverallPercent, 7);
        Assert.All(rows, t => Assert.Equal(100, t.WithinRecipePercent, 7));
    }

    [Fact]
    public void Variant_lookups_are_not_confused_by_color_or_None_rows()
    {
        using var book = Book(40);
        // A variant id equal to a color name must still resolve to the variant.
        Assert.Equal(60, VariantReach.Of(book, "r", "hat", "h").Percent, 7);
        Assert.Equal(0, VariantReach.Of(book, "r", "hat", "None").Percent);
        Assert.Equal(0, VariantReach.Of(book, "r", "trim", "Red").Percent);
    }

    // ------------------------------------------------------------------ prediction vs a real cook

    [Fact]
    public void A_seeded_cook_of_4000_assets_matches_the_predicted_color_shares()
    {
        const int n = 4000;
        using var book = Book(hatAbsentPct: 40);
        using var set = Generator.Generate(book, new GenerateOptions(n, "color-shares", EnforceUniqueDna: false));

        var observed = new Dictionary<(string Trait, string Value), int>();
        foreach (var asset in set.Assets)
            foreach (var a in MetadataTraits.Attributes(asset, MetadataTraits.CurrentShape))
            {
                var key = (a.Trait_type, a.Value);
                observed[key] = observed.GetValueOrDefault(key) + 1;
            }

        var predicted = RarityCalculator.Compute(book).Traits
            .ToDictionary(t => (t.IngredientName, t.VariantName), t => t.OverallPercent / 100);

        // Everything that happened was predicted, and every prediction lands within sampling error
        // (5 sigma of the worst case p = 0.5 over 4000 draws is about 0.04).
        foreach (var key in observed.Keys)
            Assert.True(predicted.ContainsKey(key), $"observed but not predicted: {key}");
        foreach (var (key, p) in predicted)
            Assert.InRange(observed.GetValueOrDefault(key) / (double)n, p - 0.04, p + 0.04);

        Assert.Contains(predicted.Keys, k => k.Item1 == "Body Color" && k.Item2.StartsWith("Pale ", StringComparison.Ordinal));
        Assert.Contains(predicted.Keys, k => k == ("Hat", MetadataTraits.Absent));
    }
}
