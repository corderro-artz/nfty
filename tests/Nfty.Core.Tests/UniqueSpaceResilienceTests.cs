using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// <see cref="UniqueSpace.Count"/> is documented never to throw, because a GUI calls it live while a
/// CookBook is mid-edit and a transiently invalid book is a normal thing to see there. It threw
/// anyway on four shapes — which is why <see cref="Nfty.Core.Stats.CollectionReport"/> had to wrap
/// the call in a try/catch. The contract was asserted in one file and worked around in another;
/// these pin the contract itself, so that workaround is belt-and-braces rather than load-bearing.
///
/// <para>"Uncountable" is reported as <c>IsExact == false</c>, not as an honest zero: the space is
/// undefined until the book is fixed, and <c>RecipeSpace</c> already models exactly that.</para>
/// </summary>
public class UniqueSpaceResilienceTests
{
    private static LoadedCookBook BookWith(IngredientManifest manifest)
    {
        var ing = new LoadedIngredient
        {
            Manifest = manifest,
            VariantImages = manifest.Variants.ToDictionary(
                v => v.Id, _ => new Image<Rgba32>(2, 2, new Rgba32(128, 128, 128, 255))),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "r", new[] { manifest.Id },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(2, 2),
                new Collection("B", "d", "B"), new Dictionary<string, double> { ["r"] = 1.0 }),
            Recipes = new[] { recipe },
        };
    }

    private static Variant[] OneVariant => new[] { new Variant("v", "v", 1) };

    [Fact]
    public void A_dynamic_layer_with_no_colorization_is_uncountable_not_a_crash()
    {
        using var book = BookWith(new IngredientManifest("l", "l", LayerKind.Dynamic, null, OneVariant));

        var count = UniqueSpace.Count(book);

        Assert.False(count.IsExact);
        Assert.NotEmpty(Validator.Validate(book));   // and Validator still says why
    }

    [Fact]
    public void A_colour_entry_with_neither_fixed_nor_range_is_uncountable_not_a_crash()
    {
        using var book = BookWith(new IngredientManifest("l", "l", LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 10, 10, new[] { new ColorEntry(1, null, null) }),
            OneVariant));

        Assert.False(UniqueSpace.Count(book).IsExact);
    }

    [Fact]
    public void An_unparseable_fixed_colour_spec_is_uncountable_not_a_crash()
    {
        using var book = BookWith(new IngredientManifest("l", "l", LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 10, 10, new[] { new ColorEntry(1, null, "notacolor") }),
            OneVariant));

        Assert.False(UniqueSpace.Count(book).IsExact);
    }

    [Fact]
    public void A_non_finite_entry_weight_is_uncountable_not_a_crash()
    {
        using var book = BookWith(new IngredientManifest("l", "l", LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 10, 10, new[]
            {
                new ColorEntry(double.NaN, new ColorRange(0, 60, 40, 80), null),
            }),
            OneVariant));

        Assert.False(UniqueSpace.Count(book).IsExact);
    }

    /// <summary>
    /// Hue 360 and hue 0 render identical pixels — <c>ColorConvert.Wrap</c> maps 360 to 0 — but they
    /// hash to different DNA, because <see cref="ColorBuckets.Hue"/> buckets 360 one past the top.
    /// Two assets can therefore be "unique" and look the same.
    ///
    /// <para>This is pinned rather than fixed <b>on purpose</b>. Wrapping the hue inside the bucket
    /// would change the DNA of every asset whose hue landed at 360, invalidating collections already
    /// minted; tightening Validator to reject <c>HueMax == 360</c> would reject CookBooks that are
    /// legal today. Both cures are worse than the disease, which needs a degenerate range sitting
    /// exactly on 360 to bite at all. The count stays self-consistent either way — this weakens what
    /// "unique" means, it does not make the count wrong.</para>
    /// </summary>
    [Fact]
    public void Hue_360_is_visually_hue_0_but_deliberately_keeps_its_own_dna()
    {
        var atZero = Dna.Compute("r", new[] { new LayerSelection("l", "v", 0, 0.5, 30, 10) });
        var at360 = Dna.Compute("r", new[] { new LayerSelection("l", "v", 360, 0.5, 30, 10) });

        Assert.Equal(
            Nfty.Core.Imaging.ColorConvert.HsvToRgb(0, 1, 1),
            Nfty.Core.Imaging.ColorConvert.HsvToRgb(360, 1, 1));
        Assert.NotEqual(atZero, at360);
    }
}
