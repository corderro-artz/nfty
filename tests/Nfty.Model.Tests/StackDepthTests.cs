using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class StackDepthTests
{
    private static readonly Rgba32 Red = new(200, 0, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 200, 255);
    private static readonly Rgba32 Green = new(0, 200, 0, 255);
    private static readonly Rgba32 Clear = new(0, 0, 0, 0);

    private static Image<Rgba32> Row(params Rgba32[] pixels)
    {
        var img = new Image<Rgba32>(pixels.Length, 1);
        for (int x = 0; x < pixels.Length; x++) img[x, 0] = pixels[x];
        return img;
    }

    private static DepthMap Map(params byte[] frontBackPairs) =>
        DepthMap.FromBytes(frontBackPairs.Length / 2, 1, frontBackPairs);

    private static StackRise Rise(params (Image<Rgba32>, DepthMap?)[] layers) => StackDepth.Combine(layers);

    [Fact]
    public void A_pixel_no_layer_covers_carries_no_depth()
    {
        using var only = Row(Red, Clear);
        using var rise = Rise((only, Map(0, 0, 4, 4)));
        Assert.Equal((0, 0), (rise.Depth.Front(1, 0), rise.Depth.Back(1, 0)));
        Assert.Equal(Clear, rise.Art[1, 0]);
        Assert.Equal(Clear, rise.Faces.Front[1, 0]);
        Assert.Equal(0, rise.Faces.BackColored(1, 0));
    }

    [Fact]
    public void An_overlay_with_back_zero_over_a_body_with_back_nine_makes_back_nine()
    {
        using var body = Row(Red, Red);
        using var strap = Row(Clear, Blue);
        using var rise = Rise((body, Map(2, 9, 2, 9)), (strap, Map(0, 0, 4, 0)));
        Assert.Equal((4, 9), (rise.Depth.Front(1, 0), rise.Depth.Back(1, 0)));
        Assert.Equal((2, 9), (rise.Depth.Front(0, 0), rise.Depth.Back(0, 0)));
    }

    [Fact]
    public void The_back_face_samples_the_bodys_color()
    {
        using var body = Row(Red, Red);
        using var strap = Row(Clear, Blue);
        using var rise = Rise((body, Map(2, 9, 2, 9)), (strap, Map(0, 0, 4, 0)));
        Assert.Equal(Red, rise.Faces.Back[1, 0]);
        Assert.Equal(Blue, rise.Faces.Front[1, 0]);
    }

    [Fact]
    public void The_front_view_equals_the_art_when_the_topmost_layer_reaches_the_max_front()
    {
        using var body = Row(Red, Red);
        using var strap = Row(Clear, Blue);
        using var rise = Rise((body, Map(2, 9, 2, 9)), (strap, Map(0, 0, 4, 0)));
        for (int x = 0; x < 2; x++)
            Assert.Equal(rise.Art[x, 0], rise.Faces.Front[x, 0]);
    }

    [Fact]
    public void A_lower_protruding_layer_owns_the_front()
    {
        using var body = Row(Red, Red);
        using var strap = Row(Clear, Blue);
        using var rise = Rise((body, Map(5, 9, 5, 9)), (strap, Map(0, 0, 1, 0)));
        Assert.Equal(5, rise.Depth.Front(1, 0));
        Assert.Equal(Red, rise.Faces.Front[1, 0]);
        Assert.Equal(Blue, rise.Art[1, 0]);
    }

    [Fact]
    public void A_tie_goes_to_the_upper_layer()
    {
        using var body = Row(Red);
        using var strap = Row(Blue);
        using var rise = Rise((body, Map(3, 3)), (strap, Map(3, 3)));
        Assert.Equal(Blue, rise.Faces.Front[0, 0]);
        Assert.Equal(Blue, rise.Faces.Back[0, 0]);
    }

    [Fact]
    public void An_all_paper_stack_has_faces_equal_to_the_art()
    {
        using var a = Row(Red, Red, Clear);
        using var b = Row(Blue, Clear, Clear);
        using var c = Row(Green, Clear, Clear);
        using var rise = Rise((a, null), (b, null), (c, null));
        for (int x = 0; x < 3; x++)
        {
            Assert.Equal((0, 0), (rise.Depth.Front(x, 0), rise.Depth.Back(x, 0)));
            Assert.Equal(rise.Art[x, 0], rise.Faces.Front[x, 0]);
            Assert.Equal(rise.Art[x, 0], rise.Faces.Back[x, 0]);
            Assert.Equal(0, rise.Faces.BackColored(x, 0));
        }
    }

    [Fact]
    public void When_the_front_owner_is_above_the_back_owner_the_seam_is_behind_the_front_owners_back()
    {
        using var body = Row(Red, Red);
        using var strap = Row(Clear, Blue);
        using var rise = Rise((body, Map(2, 9, 2, 9)), (strap, Map(0, 0, 4, 3)));
        Assert.Equal(9 - 3, rise.Faces.BackColored(1, 0));
    }

    [Fact]
    public void When_the_back_owner_is_above_the_front_owner_the_seam_is_the_back_owners_interval()
    {
        using var body = Row(Red, Red);
        using var strap = Row(Clear, Blue);
        using var rise = Rise((body, Map(5, 2, 5, 2)), (strap, Map(0, 0, 1, 7)));
        Assert.Equal(Red, rise.Faces.Front[1, 0]);
        Assert.Equal(Blue, rise.Faces.Back[1, 0]);
        Assert.Equal(7 + 1 + 1, rise.Faces.BackColored(1, 0));
    }

    [Fact]
    public void A_middle_layer_never_owns_a_face()
    {
        using var body = Row(Red);
        using var band = Row(Green);
        using var strap = Row(Blue);
        using var rise = Rise((body, Map(2, 9)), (band, Map(3, 4)), (strap, Map(5, 0)));
        Assert.Equal(Blue, rise.Faces.Front[0, 0]);
        Assert.Equal(Red, rise.Faces.Back[0, 0]);
    }

    [Fact]
    public void A_finished_picture_supplies_the_topmost_owners_colors()
    {
        using var body = Row(Red, Red);
        using var strap = Row(Clear, Blue);
        var nearBlue = new Rgba32(1, 0, 200, 255);
        using var finished = Row(Red, nearBlue);
        using var rise = StackDepth.Combine([(body, Map(2, 9, 2, 9)), (strap, Map(0, 0, 4, 0))], "x", finished);
        Assert.Equal(nearBlue, rise.Faces.Front[1, 0]);
        Assert.Equal(nearBlue, rise.Art[1, 0]);
        Assert.Equal(Red, rise.Faces.Back[1, 0]);
    }

    [Fact]
    public void A_finished_picture_whose_solid_mask_disagrees_is_refused()
    {
        using var body = Row(Red, Red);
        using var finished = Row(Red, Clear);
        var ex = Assert.Throws<ArgumentException>(() => StackDepth.Combine([(body, null)], "x", finished));
        Assert.Contains("(1,0)", ex.Message, StringComparison.Ordinal);
    }

    private static LoadedIngredient Dyn(string id, string variant, Image<Rgba32> img, DepthMap? depth) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 1, 1, [new ColorEntry(1, new ColorRange(100, 300, 0.5, 1), null)]),
            new[] { new Variant(variant, variant, 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { [variant] = img },
        Depths = depth is null ? DepthMaps.None : new Dictionary<string, DepthMap> { [variant] = depth },
    };

    private static LoadedRecipe DynamicBodyUnderCustomStrap(Image<Rgba32> body, Image<Rgba32> strap) => new()
    {
        Manifest = new RecipeManifest("r", "R", new[] { "body", "strap" }, Array.Empty<IncompatibilityRule>()),
        Ingredients = new[]
        {
            Dyn("body", "b", body, Art.Depth(body, (_, _) => (2, 9))),
            Layer("strap", "s", strap, Art.Depth(strap, (_, _) => (4, 0))),
        },
    };

    [Fact]
    public void A_selection_colorizes_each_layer_with_its_rolled_color()
    {
        using var body = Art.FromAscii("##");
        using var strap = Art.FromAscii(".#");
        var recipe = DynamicBodyUnderCustomStrap(body, strap);
        var color = new RolledColor(200, 0.8);

        using var rise = StackDepth.ForSelection(recipe, [new StackPick("body", "b", color), new StackPick("strap", "s")]);

        using var expected = VariantPreview.Render(recipe.Ingredients[0], "b", color);
        Assert.Equal(expected[1, 0], rise.Faces.Back[1, 0]);
        Assert.NotEqual(body[1, 0], rise.Faces.Back[1, 0]);
        Assert.Equal(strap[1, 0], rise.Faces.Front[1, 0]);
    }

    [Fact]
    public void An_asset_rises_with_the_colors_it_rolled()
    {
        using var body = Row(new Rgba32(90, 90, 90, 255), new Rgba32(160, 160, 160, 255));   // a value-map: gray
        using var strap = Art.FromAscii(".#");
        var recipe = DynamicBodyUnderCustomStrap(body, strap);
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "CB", new Dimensions(2, 1), new Collection("CB", "d", "CB"),
                new Dictionary<string, double> { ["r"] = 100 }),
            Recipes = [recipe],
        };
        using var set = Generator.Generate(book, new GenerateOptions(1, "seed", EnforceUniqueDna: false));
        var asset = set.Assets[0];
        var roll = asset.ColorRolls.Single(r => r.LayerId == "body");

        using var rise = StackDepth.ForAsset(recipe, asset)!;

        using var tinted = VariantPreview.Render(recipe.Ingredients[0], "b", new RolledColor(roll.H!.Value, roll.S!.Value));
        Assert.Equal(tinted[1, 0], rise.Faces.Back[1, 0]);
        for (int x = 0; x < 2; x++)
            Assert.Equal(asset.Image[x, 0], rise.Art[x, 0]);
    }

    [Fact]
    public void A_partial_pixel_in_any_layer_is_refused_before_compositing()
    {
        // Combine enforces the same one-solid-test rule Riser enforces on a single picture, extended
        // to every layer of the stack: a partial pixel throws, it is never silently skipped.
        using var half = Row(new Rgba32(200, 0, 0, 128));
        Assert.Throws<PartialAlphaException>(() => StackDepth.Combine([(half, Map(2, 2))]));
    }

    [Fact]
    public void A_partial_pixel_over_an_opaque_layer_is_refused_not_silently_composited()
    {
        // Source-over of a partial pixel over an opaque one composites to alpha 255, so the composite
        // alone can't catch this — Combine must scan each layer's own pixels, not just the result.
        using var bottom = Row(Red);
        using var top = Row(new Rgba32(0, 0, 200, 128));

        var ex = Assert.Throws<PartialAlphaException>(
            () => StackDepth.Combine([(bottom, null), (top, null)]));
        Assert.Contains("layer 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_pixel_in_a_non_topmost_layer_is_still_caught()
    {
        using var bottom = Row(Red);
        using var middle = Row(new Rgba32(0, 200, 0, 128));
        using var top = Row(Clear);

        var ex = Assert.Throws<PartialAlphaException>(
            () => StackDepth.Combine([(bottom, null), (middle, null), (top, null)]));
        Assert.Contains("layer 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_art_is_exactly_what_the_compositor_draws()
    {
        using var bottom = Row(Red, Red, Clear);
        using var top = Row(Blue, Clear, Clear);
        using var expected = Compositor.Composite(new Dimensions(3, 1), [bottom, top]);

        using var rise = StackDepth.Combine([(bottom, null), (top, null)]);
        for (int x = 0; x < 3; x++)
            Assert.Equal(expected[x, 0], rise.Art[x, 0]);
    }

    [Fact]
    public void An_empty_stack_is_refused()
    {
        Assert.Throws<ArgumentException>(() => StackDepth.Combine([]));
    }

    [Fact]
    public void Layers_of_different_sizes_are_refused()
    {
        using var two = Row(Red, Red);
        using var three = Row(Red, Red, Red);
        Assert.Throws<ArgumentException>(() => StackDepth.Combine([(two, null), (three, null)]));
    }

    [Fact]
    public void A_depth_map_of_another_size_is_refused()
    {
        using var two = Row(Red, Red);
        Assert.Throws<ArgumentException>(() => StackDepth.Combine([(two, Map(1, 1))]));
    }

    [Fact]
    public void A_selection_unions_the_layers_depth_and_ignores_color_for_alpha()
    {
        // body: 4x1 solid, front 1 everywhere. lock: only x=2 solid, front 3. Value-maps, Dynamic-style.
        using var body = Art.FromAscii("####");
        using var lockArt = Art.FromAscii("..#.");
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("chest", "Chest", new[] { "body", "lock" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[]
            {
                Layer("body", "oak", body, Art.Depth(body, (_, _) => (1, 0))),
                Layer("lock", "gold", lockArt, Art.Depth(lockArt, (_, _) => (3, 0))),
            },
        };

        using var rise = StackDepth.ForSelection(recipe, [("body", "oak"), ("lock", "gold")]);

        Assert.Equal(new byte[] { 1, 0, 1, 0, 3, 0, 1, 0 }, rise.Depth.ToBytes());
        // Colorizing changes no alpha, so the stack rule over colorized layers gives the same bytes.
        using var tinted = Colorizer.Apply(lockArt, 200, 0.5, ColorModel.Hsv);
        using var viaCombine = StackDepth.Combine([
            (body, Art.Depth(body, (_, _) => (1, 0))), (tinted, Art.Depth(lockArt, (_, _) => (3, 0)))]);
        Assert.Equal(viaCombine.Depth.ToBytes(), rise.Depth.ToBytes());
    }

    [Fact]
    public void A_selection_naming_a_missing_variant_is_refused_by_name()
    {
        using var body = Art.FromAscii("#");
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("chest", "Chest", new[] { "body" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Layer("body", "oak", body, null) },
        };
        var ex = Assert.Throws<KeyNotFoundException>(() => StackDepth.ForSelection(recipe, [("body", "walnut")]));
        Assert.Contains("walnut", ex.Message);
    }

    private static LoadedIngredient Layer(string id, string variant, Image<Rgba32> img, DepthMap? depth) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, new[] { new Variant(variant, variant, 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { [variant] = img },
        Depths = depth is null ? DepthMaps.None : new Dictionary<string, DepthMap> { [variant] = depth },
    };
}
