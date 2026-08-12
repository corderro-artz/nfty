using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>Rendering one Variant exactly as generation would.
///
/// This rule used to live inline in the CLI's preview command, whose own comment said the point was
/// "a preview shows exactly what generation would render rather than a second, drifting
/// implementation". Wiring the GUI separately would have created that second implementation, so it
/// moved here — and the assertion that matters most is the one comparing this against the real
/// colorization path rather than against a hand-computed expectation.</summary>
public class VariantPreviewTests
{
    private static LoadedIngredient Ing(LayerKind kind, Colorization? colorization, Rgba32 fill)
    {
        var img = new Image<Rgba32>(4, 4, fill);
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", kind, colorization,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = img },
        };
    }

    private static Colorization Hsv() => new(ColorModel.Hsv, 24, 6,
        new[] { new ColorEntry(1, new ColorRange(0, 360, 0, 100), null) });

    // ---- the contract that makes it a preview and not a picture -----------------------------------

    /// <summary>The one that matters: identical to what the generation-time colorizer produces, and
    /// asserted by running that colorizer rather than by a number typed into the test.</summary>
    [Fact]
    public void A_colorized_render_is_pixel_identical_to_the_generation_path()
    {
        using var ing = Ing(LayerKind.Dynamic, Hsv(), new Rgba32(128, 128, 128, 255));

        using var preview = VariantPreview.Render(ing, "a", "hex:d6249f");

        var (h, s) = ColorRoller.FromFixed("hex:d6249f", ColorModel.Hsv);
        using var expected = Colorizer.Apply(ing.VariantImages["a"], h, s, ColorModel.Hsv);

        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.Equal(expected[x, y], preview[x, y]);
    }

    /// <summary>A Custom layer is composited as-is and never colorized, so its preview must be the
    /// art untouched — applying a colour generation never applies would make the preview a lie.</summary>
    [Fact]
    public void A_custom_layer_renders_untouched()
    {
        var fill = new Rgba32(10, 200, 30, 255);
        using var ing = Ing(LayerKind.Custom, null, fill);

        using var preview = VariantPreview.Render(ing, "a");

        Assert.Equal(fill, preview[0, 0]);
        Assert.Equal(fill, preview[3, 3]);
    }

    [Fact]
    public void A_custom_layer_ignores_a_colour_rather_than_honouring_it()
    {
        var fill = new Rgba32(10, 200, 30, 255);
        using var ing = Ing(LayerKind.Custom, null, fill);

        // Passing one is not an error - it is simply not what this kind does.
        using var preview = VariantPreview.Render(ing, "a", "hex:ff0000");

        Assert.Equal(fill, preview[0, 0]);
    }

    /// <summary>Ownership must not depend on the layer kind. Custom returns a CLONE, so the caller
    /// always disposes and the ingredient's own image is never handed out — otherwise disposing a
    /// preview would corrupt the ingredient for one kind and be correct for the others.</summary>
    [Fact]
    public void The_returned_image_is_always_the_callers_even_for_custom()
    {
        using var ing = Ing(LayerKind.Custom, null, new Rgba32(1, 2, 3, 255));
        var source = ing.VariantImages["a"];

        var preview = VariantPreview.Render(ing, "a");
        Assert.NotSame(source, preview);

        preview.Dispose();

        // The ingredient's own image survives its preview being disposed.
        Assert.Equal(new Rgba32(1, 2, 3, 255), source[0, 0]);
    }

    // ---- what it refuses to guess ------------------------------------------------------------------

    [Fact]
    public void A_colorized_layer_without_a_colour_is_an_error_naming_the_kind()
    {
        using var ing = Ing(LayerKind.Static, Hsv(), new Rgba32(128, 128, 128, 255));

        var ex = Assert.Throws<InvalidOperationException>(() => VariantPreview.Render(ing, "a"));

        Assert.Contains("colour is required", ex.Message);
        Assert.Contains("static", ex.Message);      // says WHY, not just that it failed
        Assert.Contains("Aura", ex.Message);
    }

    [Fact]
    public void An_unknown_variant_lists_the_valid_ids()
    {
        using var ing = Ing(LayerKind.Custom, null, new Rgba32(1, 1, 1, 255));

        var ex = Assert.Throws<InvalidOperationException>(() => VariantPreview.Render(ing, "nope"));

        Assert.Contains("nope", ex.Message);
        Assert.Contains("a", ex.Message);           // the id that IS valid
    }

    /// <summary>A hand-edited manifest can claim a colorized kind with no colorization block. The
    /// same shape that makes UniqueSpace throw - so say what is wrong rather than dereferencing null.</summary>
    [Fact]
    public void A_colorized_kind_with_no_colorization_block_is_explained_not_dereferenced()
    {
        using var ing = Ing(LayerKind.Dynamic, colorization: null, new Rgba32(128, 128, 128, 255));

        var ex = Assert.Throws<InvalidOperationException>(
            () => VariantPreview.Render(ing, "a", "hex:d6249f"));

        Assert.Contains("no colorization", ex.Message);
    }

    /// <summary>
    /// The same refusal <b>with a model override supplied</b>, on both overloads — the one case a
    /// refactor can silently drop.
    ///
    /// <para>Written as <c>modelOverride ?? (Colorization ?? throw).Model</c> the check reads
    /// correctly and is dead: <c>??</c> never evaluates its right operand when the left is non-null,
    /// so passing a model turned a refusal into a successful render of a manifest generation could
    /// not cook at all. Reachable straight from the command line —
    /// <c>preview x.igt --variant v --color hex:aabbcc --model hsv</c> — and invisible to every test
    /// that omitted the override, which was all of them.</para>
    /// </summary>
    [Fact]
    public void A_model_override_does_not_excuse_a_missing_colorization_block()
    {
        using var ing = Ing(LayerKind.Dynamic, colorization: null, new Rgba32(128, 128, 128, 255));

        var fromSpec = Assert.Throws<InvalidOperationException>(
            () => VariantPreview.Render(ing, "a", "hex:d6249f", ColorModel.Hsl));
        var fromRolled = Assert.Throws<InvalidOperationException>(
            () => VariantPreview.Render(ing, "a", new RolledColor(210, 0.5), ColorModel.Hsl));

        Assert.Contains("no colorization", fromSpec.Message);
        Assert.Contains("no colorization", fromRolled.Message);
    }

    /// <summary>The rolled overload refuses it without an override too — it has no colour-spec check
    /// ahead of it, so this is the only thing standing between a broken manifest and a render.</summary>
    [Fact]
    public void The_rolled_overload_refuses_a_colorized_kind_with_no_colorization_block()
    {
        using var ing = Ing(LayerKind.Static, colorization: null, new Rgba32(128, 128, 128, 255));

        var ex = Assert.Throws<InvalidOperationException>(
            () => VariantPreview.Render(ing, "a", new RolledColor(210, 0.5)));

        Assert.Contains("no colorization", ex.Message);
    }

    // ---- the rolled overload -----------------------------------------------------------------------

    /// <summary>Ownership does not depend on which overload was called either: Custom still comes back
    /// as a clone the caller owns, and the colour is still ignored rather than applied.</summary>
    [Fact]
    public void The_rolled_overload_clones_a_custom_layer_and_ignores_the_colour()
    {
        var fill = new Rgba32(10, 200, 30, 255);
        using var ing = Ing(LayerKind.Custom, null, fill);
        var source = ing.VariantImages["a"];

        var preview = VariantPreview.Render(ing, "a", new RolledColor(210, 0.5));

        Assert.NotSame(source, preview);
        Assert.Equal(fill, preview[0, 0]);

        preview.Dispose();
        Assert.Equal(fill, source[0, 0]);   // the ingredient's own image survived
    }

    // ---- model override ----------------------------------------------------------------------------

    [Fact]
    public void The_model_override_actually_changes_the_render()
    {
        using var ing = Ing(LayerKind.Dynamic, Hsv(), new Rgba32(128, 128, 128, 255));

        using var asDeclared = VariantPreview.Render(ing, "a", "hex:d6249f");
        using var overridden = VariantPreview.Render(ing, "a", "hex:d6249f", ColorModel.Hsl);

        // HSL and HSV resolve the same spec to different (H,S), so the two renders must differ -
        // otherwise the override is silently doing nothing.
        Assert.NotEqual(asDeclared[0, 0], overridden[0, 0]);
    }

    [Fact]
    public void No_override_uses_the_ingredients_own_model()
    {
        using var ing = Ing(LayerKind.Dynamic, Hsv(), new Rgba32(128, 128, 128, 255));

        using var implicitModel = VariantPreview.Render(ing, "a", "hex:d6249f");
        using var explicitModel = VariantPreview.Render(ing, "a", "hex:d6249f", ColorModel.Hsv);

        Assert.Equal(explicitModel[0, 0], implicitModel[0, 0]);
    }

    // ---- the question a UI asks --------------------------------------------------------------------

    [Theory]
    [InlineData(LayerKind.Dynamic, true)]
    [InlineData(LayerKind.Static, true)]
    [InlineData(LayerKind.Custom, false)]
    public void NeedsColor_says_whether_to_ask(LayerKind kind, bool expected)
    {
        using var ing = Ing(kind, kind == LayerKind.Custom ? null : Hsv(), new Rgba32(1, 1, 1, 255));
        Assert.Equal(expected, VariantPreview.NeedsColor(ing));
    }

    /// <summary>Value comes from the grayscale map, hue and saturation from the colour — so a
    /// value-map's light and dark pixels must stay light and dark after colorization.</summary>
    [Fact]
    public void Colorizing_preserves_the_value_maps_light_and_dark()
    {
        var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(20, 20, 20, 255);      // dark
        img[1, 0] = new Rgba32(230, 230, 230, 255);   // light
        using var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, Hsv(),
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = img },
        };

        using var preview = VariantPreview.Render(ing, "a", "hex:d6249f");

        static int Luma(Rgba32 p) => (int)(0.2126 * p.R + 0.7152 * p.G + 0.0722 * p.B);
        Assert.True(Luma(preview[0, 0]) < Luma(preview[1, 0]));
    }
}
