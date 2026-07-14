using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Semantics of the three layer kinds:
/// Dynamic (rolled color), Static (single fixed color, no RNG), Custom (as-is full color).
/// </summary>
public class KindSemanticsTests
{
    private static readonly Rgba32 Gray = new(128, 128, 128, 255);

    private static Colorization SingleFixed(string spec, ColorModel model = ColorModel.Hsv) =>
        new(model, 5, 5, new[] { new ColorEntry(1, null, spec) });

    private static Colorization DynRange(ColorModel model = ColorModel.Hsv) =>
        new(model, 5, 5, new[] { new ColorEntry(1, new ColorRange(175, 195, 60, 90), null) });

    private static LoadedIngredient Ing(string id, LayerKind kind, Colorization? col, Rgba32 fill) => new()
    {
        Manifest = new IngredientManifest(id, id, kind, col, new[] { new Variant("v", "v", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = new Image<Rgba32>(2, 2, fill) },
    };

    private static LoadedIngredient StaticIng(string id, string spec) =>
        Ing(id, LayerKind.Static, SingleFixed(spec), Gray);

    private static LoadedIngredient CustomIng(string id, Rgba32 fill) =>
        Ing(id, LayerKind.Custom, null, fill);

    private static LoadedIngredient DynIng(string id) =>
        Ing(id, LayerKind.Dynamic, DynRange(), Gray);

    private static LoadedCookBook Book(params LoadedIngredient[] ings) => new()
    {
        Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["r"] = 1 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("r", "R", ings.Select(i => i.Manifest.Id).ToList(),
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = ings,
            },
        },
    };

    private static GeneratedAsset One(LoadedCookBook book, string seed = "seed") =>
        Generator.Generate(book, new GenerateOptions(1, seed)).Assets.Single();

    [Fact]
    public void Static_layer_resolves_to_its_single_fixed_color()
    {
        var roll = One(Book(StaticIng("s", "hsv:200,50,80"))).ColorRolls.Single(c => c.LayerId == "s");
        Assert.Equal(LayerKind.Static, roll.Kind);
        Assert.InRange(roll.H!.Value, 199.0, 201.0);
        Assert.InRange(roll.S!.Value, 0.49, 0.51);
    }

    [Fact]
    public void Static_color_is_seed_independent()
    {
        var a = One(Book(StaticIng("s", "hsv:200,50,80")), "seed-A").ColorRolls.Single();
        var b = One(Book(StaticIng("s", "hsv:200,50,80")), "seed-B").ColorRolls.Single();
        Assert.Equal(a.H, b.H);
        Assert.Equal(a.S, b.S);
    }

    [Fact]
    public void Static_consumes_no_rng()
    {
        // A Static first layer and a Custom first layer both consume ZERO color RNG,
        // so the downstream Dynamic layer must roll an identical color in both books.
        var withStatic = One(Book(StaticIng("l1", "hsv:200,50,80"), DynIng("l2")))
            .ColorRolls.Single(c => c.LayerId == "l2");
        var withCustom = One(Book(CustomIng("l1", new Rgba32(9, 9, 9, 255)), DynIng("l2")))
            .ColorRolls.Single(c => c.LayerId == "l2");
        Assert.Equal(withStatic.H, withCustom.H);
        Assert.Equal(withStatic.S, withCustom.S);
    }

    [Fact]
    public void Custom_layer_composites_as_is()
    {
        var color = new Rgba32(10, 200, 30, 255);
        var asset = One(Book(CustomIng("c", color)));
        Assert.Equal(color, asset.Image[0, 0]);
        var roll = asset.ColorRolls.Single();
        Assert.Equal(LayerKind.Custom, roll.Kind);
        Assert.Null(roll.H);
        Assert.Null(roll.S);
        Assert.Null(roll.Model);
    }

    [Fact]
    public void Static_fixed_color_contributes_to_dna()
    {
        var pink = One(Book(StaticIng("s", "hsv:320,80,80"))).Dna;
        var teal = One(Book(StaticIng("s", "hsv:180,80,80"))).Dna;
        Assert.NotEqual(pink, teal);
    }

    [Fact]
    public void Custom_and_static_same_variant_have_different_dna()
    {
        // Custom records no color; Static records its fixed color, so DNA must differ
        // even though ingredient id and variant id are identical.
        var custom = One(Book(CustomIng("s", new Rgba32(1, 2, 3, 255)))).Dna;
        var stat = One(Book(StaticIng("s", "hsv:200,80,80"))).Dna;
        Assert.NotEqual(custom, stat);
    }

    [Fact]
    public void All_layers_are_represented_in_color_rolls_with_kind()
    {
        var asset = One(Book(CustomIng("c", new Rgba32(1, 2, 3, 255)), StaticIng("s", "hsv:200,50,80"), DynIng("d")));
        Assert.Equal(3, asset.ColorRolls.Count);
        Assert.Equal(LayerKind.Custom, asset.ColorRolls.Single(c => c.LayerId == "c").Kind);
        Assert.Equal(LayerKind.Static, asset.ColorRolls.Single(c => c.LayerId == "s").Kind);
        Assert.Equal(LayerKind.Dynamic, asset.ColorRolls.Single(c => c.LayerId == "d").Kind);
    }
}
