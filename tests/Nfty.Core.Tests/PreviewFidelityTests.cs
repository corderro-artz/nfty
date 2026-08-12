using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The promise <c>VariantPreview</c> makes in its own summary — the render a Variant gets <b>exactly
/// as generation would</b> — held to the word "exactly".
///
/// <para>It was not true for a rolled colour. A <see cref="PreviewLayer"/> named its colour as a spec
/// string, and every spec resolves through 8-bit RGB, so a hue that fell between two representable
/// colours came back as the nearest one and the preview landed a rounding step off the asset. About a
/// quarter-degree of hue — invisible, and exactly the kind of drift that survives forever because
/// nobody can see it. The layer now carries the unrounded <see cref="RolledColor"/> as well and draws
/// from that; the spec stayed, because a person still has to be able to read and re-run it.</para>
/// </summary>
public class PreviewFidelityTests
{
    private static readonly Dimensions Canvas = new(2, 2);

    /// <summary>A value-map layer whose colour is rolled from a wide range, so the rolled hue is a
    /// continuous value rather than one of the few a spec can spell exactly.</summary>
    private static LoadedIngredient Dynamic(string id, ColorModel model = ColorModel.Hsv) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic,
            new Colorization(model, 12, 5,
                new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
            new[] { new Variant("v", "v", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal)
        {
            ["v"] = new Image<Rgba32>(2, 2, new Rgba32(200, 200, 200, 255)),
        },
    };

    // Draws are scripted rather than seeded (see ScriptedRng) so the rolled colour is stated by the
    // test rather than discovered. The budget matters here specifically: a colour roll costs THREE
    // draws — weighted entry, hue, saturation — and StackRoll.ForIngredient spends one more before
    // that on the variant. Scripting them is what lets the preview and the direct roll be handed the
    // same hue and saturation draws, which is the only thing that makes comparing their pixels mean
    // anything.

    /// <summary>Every pixel, so a one-channel drift of one step in a corner cannot pass.</summary>
    private static void AssertPixelIdentical(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.Equal(expected[x, y], actual[x, y]);
    }

    /// <summary>
    /// The property the whole change exists for: a stack preview of a rolled layer is <b>the same
    /// pixels</b> generation would produce, not pixels near them. Generation's path is
    /// <c>ColorRoller.Roll</c> straight into <c>Colorizer.Apply</c>, so that is what this compares
    /// against — the real path, not a re-derivation of it.
    ///
    /// <para><b>Only the first of these draws actually catches the defect.</b> Mutation-probed: with
    /// the rolled colour ignored and the spec rendered instead, this theory fails at
    /// <c>0.123456789</c> and passes at the other three, because most rolled colours happen to
    /// survive the 8-bit round trip unchanged. That is precisely why the drift went unnoticed — a
    /// spot check would almost always have agreed. The other cases stay because they cost nothing and
    /// pin the axis ends; the first one is the test.</para>
    /// </summary>
    [Theory]
    [InlineData(0.123456789)]
    [InlineData(0.3)]
    [InlineData(0.777777)]
    [InlineData(0.9999999)]
    public void A_rolled_layer_previews_as_the_exact_pixels_generation_would_draw(double draw)
    {
        using var ing = Dynamic("body");
        var colorization = ing.Manifest.Colorization!;

        // What generation does, in generation's own order: entry, hue, sat.
        var rolled = ColorRoller.Roll(colorization, new ScriptedRng(0, draw, draw));
        using var expected = Colorizer.Apply(ing.VariantImages["v"], rolled.H, rolled.S, colorization.Model);

        // What a preview does, through the whole public path. The leading 0 is the variant draw
        // ForIngredient spends first; the three after it are the same colour draws as above, so any
        // difference in the pixels is the colour's TRIP through PreviewLayer and nothing else.
        var layer = StackRoll.ForIngredient(ing, new ScriptedRng(0, 0, draw, draw));
        using var actual = StackPreview.Render(Canvas, new[] { layer });

        AssertPixelIdentical(expected, actual);
    }

    /// <summary>
    /// The non-vacuity guard, and the reason the extra field had to exist at all: the spec printed
    /// beside the rolled colour does <b>not</b> resolve back to it. Asserted on the values rather than
    /// on pixels — a hue drift of a quarter-degree may or may not move an 8-bit channel for any
    /// particular grey, so a pixel-level version of this could pass by luck on a kinder test image
    /// while the defect was still there.
    /// </summary>
    [Fact]
    public void The_printed_spec_cannot_spell_the_rolled_colour_exactly()
    {
        using var ing = Dynamic("body");
        var layer = StackRoll.ForIngredient(ing, new ScriptedRng(0, 0, 0.123456789, 0.123456789));

        Assert.NotNull(layer.Rolled);
        var rolled = layer.Rolled!.Value;
        var (specH, specS) = ColorRoller.FromFixed(layer.ColorSpec!, ing.Manifest.Colorization!.Model);

        Assert.NotEqual(rolled.H, specH);

        // ...and it is a rounding step, not a blunder. If this ever widens, the spelling regressed
        // (pinning value/lightness at the wrong end of its axis is how — hsl l=100 is pure white and
        // throws saturation away entirely), and the printed spec stopped being a usable stand-in.
        Assert.True(Math.Abs(rolled.H - specH) < 1.0,
            $"the spec should land within a degree of the rolled hue, but {specH} is not near {rolled.H}");
        Assert.True(Math.Abs(rolled.S - specS) < 0.01,
            $"the spec should land within a point of the rolled saturation, but {specS} is not near {rolled.S}");
    }

    /// <summary>
    /// The field earns its place: rendering the same layer with the rolled colour and without it
    /// produces <b>different pixels</b>. Stated directly rather than left implicit in the theory
    /// above, because that theory compares a preview against generation and would still pass if
    /// <c>Rolled</c> were quietly dropped and both paths drifted together.
    /// </summary>
    [Fact]
    public void Dropping_the_rolled_colour_changes_what_is_drawn()
    {
        using var ing = Dynamic("body");
        var withRolled = StackRoll.ForIngredient(ing, new ScriptedRng(0, 0, 0.123456789, 0.123456789));
        var specOnly = withRolled with { Rolled = null };

        Assert.NotNull(withRolled.Rolled);
        using var exact = StackPreview.Render(Canvas, new[] { withRolled });
        using var rounded = StackPreview.Render(Canvas, new[] { specOnly });

        Assert.NotEqual(exact[0, 0], rounded[0, 0]);
    }

    /// <summary>
    /// A Static layer needs none of this and must not acquire it. Its colour is a spec the author
    /// wrote, and generation resolves that same string through that same parser — so the string IS the
    /// exact value and there is nothing to carry alongside it. A rolled value appearing here would
    /// mean the layer had consumed RNG, which a Static layer must never do.
    /// </summary>
    [Fact]
    public void A_static_layer_carries_no_rolled_colour_because_its_spec_is_already_exact()
    {
        using var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("eyes", "eyes", LayerKind.Static,
                new Colorization(ColorModel.Hsv, 12, 5,
                    new[] { new ColorEntry(1, null, "hex:d6249f") }),
                new[] { new Variant("v", "v", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal)
            {
                ["v"] = new Image<Rgba32>(2, 2, new Rgba32(160, 160, 160, 255)),
            },
        };

        // Exactly one draw is offered — the variant's. A colour draw would overrun the script and
        // throw, so "Static consumes no RNG for its colour" is enforced by the budget, not just
        // asserted after the fact.
        var rng = new ScriptedRng(0);
        var layer = StackRoll.ForIngredient(ing, rng);

        Assert.Equal(1, rng.Calls);
        Assert.Null(layer.Rolled);
        Assert.Equal("hex:d6249f", layer.ColorSpec);

        var (h, s) = ColorRoller.FromFixed(layer.ColorSpec!, ColorModel.Hsv);
        using var expected = Colorizer.Apply(ing.VariantImages["v"], h, s, ColorModel.Hsv);
        using var actual = StackPreview.Render(Canvas, new[] { layer });
        AssertPixelIdentical(expected, actual);
    }

    /// <summary>
    /// A Custom layer is composited as-is and is never colorized, so neither colour field means
    /// anything for it — and a rolled colour arriving on one must not change a single pixel. This is
    /// the kind-boundary the rest of the engine already holds; it holds here too.
    /// </summary>
    [Fact]
    public void A_custom_layer_ignores_a_rolled_colour_entirely()
    {
        using var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("hat", "hat", LayerKind.Custom, null,
                new[] { new Variant("v", "v", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal)
            {
                ["v"] = new Image<Rgba32>(2, 2, new Rgba32(40, 200, 120, 255)),
            },
        };

        var forced = new PreviewLayer(ing, "v", "hex:ff0000", new RolledColor(210, 0.9));
        using var actual = StackPreview.Render(Canvas, new[] { forced });

        AssertPixelIdentical(ing.VariantImages["v"], actual);
    }
}
