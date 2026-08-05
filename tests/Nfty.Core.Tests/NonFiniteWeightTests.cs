using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// A weight of NaN or ±Infinity used to pass every check and then quietly ruin a collection.
///
/// <para><c>Validator</c> tested <c>weight &lt; 0</c> and <c>Sum() &lt;= 0</c>, and both comparisons
/// are <em>false</em> for NaN — so the book validated clean. <see cref="WeightedRoller"/> then had
/// <c>total = NaN</c>, every <c>r &lt; cumulative[i]</c> false, and fell through to its
/// <c>Keys[^1]</c> guard on every single draw. The whole collection collapsed onto one variant —
/// the ordinal-last one, so renaming an unrelated variant silently changed the output — with no
/// error raised anywhere. Archives can't carry NaN (JSON has no literal for it), but Core's
/// in-memory API can, and that is the GUI's documented path.</para>
/// </summary>
public class NonFiniteWeightTests
{
    private static LoadedIngredient Ing(string id, params (string Id, double Weight)[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variants.Select(v => new Variant(v.Id, v.Id, v.Weight)).ToList()),
        VariantImages = variants.ToDictionary(
            v => v.Id, _ => new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255))),
    };

    private static LoadedCookBook Book(LoadedIngredient ing, double recipeWeight = 1.0)
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "r", new[] { ing.Manifest.Id },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(2, 2),
                new Collection("B", "d", "B"),
                new Dictionary<string, double> { ["r"] = recipeWeight }),
            Recipes = new[] { recipe },
        };
    }

    public static TheoryData<double> NonFinite() => new() { double.NaN, double.PositiveInfinity, double.NegativeInfinity };

    [Theory]
    [MemberData(nameof(NonFinite))]
    public void A_non_finite_variant_weight_is_reported(double weight)
    {
        using var book = Book(Ing("l", ("aaa", 1), ("zzz", weight)));

        var problems = Validator.Validate(book);

        Assert.Contains(problems, p => p.Contains("zzz") && p.Contains("non-finite"));
    }

    [Theory]
    [MemberData(nameof(NonFinite))]
    public void A_non_finite_recipe_weight_is_reported(double weight)
    {
        using var book = Book(Ing("l", ("aaa", 1)), recipeWeight: weight);

        var problems = Validator.Validate(book);

        Assert.Contains(problems, p => p.Contains("'r'") && p.Contains("non-finite"));
    }

    [Theory]
    [MemberData(nameof(NonFinite))]
    public void A_non_finite_colorization_entry_weight_is_reported(double weight)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("l", "l", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 10, 10, new[]
                {
                    new ColorEntry(1, new ColorRange(0, 60, 40, 80), null),
                    new ColorEntry(weight, new ColorRange(100, 160, 40, 80), null),
                }),
                new[] { new Variant("v", "v", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["v"] = new Image<Rgba32>(2, 2, new Rgba32(128, 128, 128, 255)),
            },
        };
        using var book = Book(ing);

        Assert.Contains(Validator.Validate(book), p => p.Contains("non-finite"));
    }

    /// <summary>Belt and braces: even if a non-finite weight reaches the roller some other way, the
    /// draw must fail loudly rather than silently return its last key forever.</summary>
    [Theory]
    [MemberData(nameof(NonFinite))]
    public void The_roller_refuses_a_non_finite_total_instead_of_falling_through(double weight)
    {
        var table = WeightedRoller.Prepare(new Dictionary<string, double>
        {
            ["aaa"] = 1,
            ["zzz"] = weight,
        });

        var ex = Assert.Throws<InvalidOperationException>(
            () => WeightedRoller.Roll(table, new SplitMix64Rng(1)));
        Assert.Contains("finite", ex.Message);
    }

    /// <summary>The symptom as it actually presented: not an exception, but a collection with no
    /// variety at all, silently pinned to whichever id sorts last.</summary>
    [Fact]
    public void A_finite_table_still_spreads_across_its_keys()
    {
        var table = WeightedRoller.Prepare(new Dictionary<string, double>
        {
            ["aaa"] = 1,
            ["mmm"] = 1,
            ["zzz"] = 1,
        });
        var rng = new SplitMix64Rng(SeedHash.ToUlong("spread"));

        var hits = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 300; i++) hits.Add(WeightedRoller.Roll(table, rng));

        Assert.Equal(3, hits.Count);
    }
}
