using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Compositing several layers at their real depths, so art authored one layer at a time can be lined
/// up against what it will sit between.
///
/// <para>Three properties carry the feature. First, the <b>above/below split</b>: an opaque reference
/// above the edited layer must hide it and the same reference below must not — sunglasses sit above a
/// face and below a hat, and a flat underlay would show neither correctly. Second, <b>one rendering
/// rule</b>: a colorized layer is asserted pixel-identical to what <c>VariantPreview.Render</c>
/// produces for the same inputs, by running it, so "the CLI's preview and the GUI's preview are the
/// same image" is pinned rather than assumed. Third, <b>no leak</b>, asserted through ImageSharp's
/// process-wide <see cref="MemoryDiagnostics.TotalUndisposedAllocationCount"/> — a disposed
/// <c>Image</c> still answers <c>Width</c> without throwing, so a property read proves nothing and
/// that counter is the only honest check. (It is process-wide, which is why this assembly disables
/// cross-class parallelization; see <c>AssemblyInfo.cs</c>.)</para>
/// </summary>
public class StackPreviewTests
{
    private static readonly Dimensions Canvas = new(2, 2);
    private static readonly Rgba32 Red = new(255, 0, 0, 255);
    private static readonly Rgba32 Green = new(0, 255, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 255, 255);
    private static readonly Rgba32 Clear = new(0, 0, 0, 0);

    // Every pixel of the 2x2, never just one: a layer that failed to cover would show at a corner.
    private static readonly (int X, int Y)[] Corners = { (0, 0), (1, 0), (0, 1), (1, 1) };

    // A custom layer with a single variant "v": full-colour art, composited as-is, never colorized.
    private static LoadedIngredient Custom(string id, Rgba32 fill, int width = 2, int height = 2) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            new[] { new Variant("v", "V", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
            { ["v"] = new Image<Rgba32>(width, height, fill) },
    };

    // A colorized layer with a single variant "v": an opaque grayscale value-map.
    private static LoadedIngredient Gray(string id, LayerKind kind, byte value) => new()
    {
        Manifest = new IngredientManifest(id, id, kind, Hsv(), new[] { new Variant("v", "V", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
            { ["v"] = new Image<Rgba32>(2, 2, new Rgba32(value, value, value, 255)) },
    };

    // A custom layer whose variants differ in weight (and in fill, so which one rendered is visible).
    private static LoadedIngredient Weighted(params (string Id, double Weight)[] variants) => new()
    {
        Manifest = new IngredientManifest("shades", "Sunglasses", LayerKind.Custom, null,
            variants.Select(v => new Variant(v.Id, v.Id, v.Weight)).ToList()),
        VariantImages = variants.ToDictionary(
            v => v.Id,
            v => new Image<Rgba32>(2, 2, new Rgba32((byte)v.Id[0], 0, 0, 255))),
    };

    private static Colorization Hsv() => new(ColorModel.Hsv, 24, 6,
        new[] { new ColorEntry(1, new ColorRange(0, 360, 0, 100), null) });

    private static RecipeManifest Stack(params string[] ids) =>
        new("cat", "Cat", ids, Array.Empty<IncompatibilityRule>());

    /// <summary>The stack a caller actually renders: the split, with the edited layer pinned back in
    /// at its own depth.</summary>
    private static IReadOnlyList<PreviewLayer> Pin(
        IReadOnlyDictionary<string, LoadedIngredient> byId, StackSplit split, string editedId) =>
        split.Below.Append(editedId).Concat(split.Above)
            .Select(id => new PreviewLayer(byId[id], "v", null))
            .ToList();

    // ---- the one the whole feature rests on -------------------------------------------------------

    /// <summary>
    /// Two renders of the same two layers, in the same colours, on the same canvas. The only
    /// difference is which side of the edited layer the recipe stacks the reference on — and that
    /// alone must decide whether the edited layer is visible. Without this split the feature is
    /// useless: a flat underlay shows a hat as if it were behind the sunglasses.
    /// </summary>
    [Fact]
    public void A_reference_above_the_edited_layer_hides_it_and_the_same_reference_below_does_not()
    {
        using var shades = Custom("shades", Green);
        using var reference = Custom("ref", Blue);
        var byId = new Dictionary<string, LoadedIngredient>
            { ["shades"] = shades, ["ref"] = reference };

        var above = StackPreview.Split(Stack("shades", "ref"), "shades", new[] { "ref" });
        Assert.Empty(above.Below);
        Assert.Equal(new[] { "ref" }, above.Above);

        var below = StackPreview.Split(Stack("ref", "shades"), "shades", new[] { "ref" });
        Assert.Equal(new[] { "ref" }, below.Below);
        Assert.Empty(below.Above);

        using var covered = StackPreview.Render(Canvas, Pin(byId, above, "shades"));
        using var uncovered = StackPreview.Render(Canvas, Pin(byId, below, "shades"));

        foreach (var (x, y) in Corners)
        {
            Assert.Equal(Blue, covered[x, y]);      // the reference above hid the layer being edited
            Assert.Equal(Green, uncovered[x, y]);   // the same reference below did not
        }
    }

    // ---- the split ---------------------------------------------------------------------------------

    [Fact]
    public void Split_orders_both_sides_bottom_to_top_by_depth()
    {
        var recipe = Stack("bg", "body", "face", "shades", "hair", "hat");

        // Enabled deliberately out of order: the answer's ordering comes from the recipe's depths,
        // never from the order the caller happened to tick the boxes in.
        var split = StackPreview.Split(recipe, "shades",
            new[] { "hat", "bg", "hair", "face", "body" });

        Assert.Equal(new[] { "bg", "body", "face" }, split.Below);
        Assert.Equal(new[] { "hair", "hat" }, split.Above);
    }

    [Fact]
    public void Split_omits_the_layers_that_are_switched_off()
    {
        var split = StackPreview.Split(
            Stack("bg", "body", "shades", "hair", "hat"), "shades", new[] { "bg", "hat" });

        Assert.Equal(new[] { "bg" }, split.Below);
        Assert.Equal(new[] { "hat" }, split.Above);
    }

    /// <summary>Zero references on is the default state, not an error.</summary>
    [Fact]
    public void Split_with_nothing_enabled_is_empty_on_both_sides()
    {
        var split = StackPreview.Split(Stack("bg", "shades", "hat"), "shades", Array.Empty<string>());

        Assert.Empty(split.Below);
        Assert.Empty(split.Above);
    }

    /// <summary>The edited layer is pinned at its own depth: it is the subject of the preview, not a
    /// reference in it, so it must never come back in either list — even when the caller's enabled set
    /// names it.</summary>
    [Fact]
    public void Split_never_returns_the_edited_layer_as_one_of_its_own_references()
    {
        var split = StackPreview.Split(
            Stack("bg", "shades", "hat"), "shades", new[] { "bg", "shades", "hat" });

        Assert.Equal(new[] { "bg" }, split.Below);
        Assert.Equal(new[] { "hat" }, split.Above);
    }

    [Fact]
    public void Editing_the_bottom_or_the_top_layer_leaves_one_side_empty()
    {
        var recipe = Stack("bg", "shades", "hat");
        var enabled = new[] { "bg", "shades", "hat" };

        var bottom = StackPreview.Split(recipe, "bg", enabled);
        Assert.Empty(bottom.Below);
        Assert.Equal(new[] { "shades", "hat" }, bottom.Above);

        var top = StackPreview.Split(recipe, "hat", enabled);
        Assert.Equal(new[] { "bg", "shades" }, top.Below);
        Assert.Empty(top.Above);
    }

    /// <summary>An enabled id the recipe does not stack has no depth here, so there is no position to
    /// put it at — it is ignored rather than rejected. That is the Kitchen case (a loose <c>.igt</c>
    /// the caller places within the preview session), and it also keeps a stale checkbox for a
    /// just-removed layer from throwing in the middle of a repaint.</summary>
    [Fact]
    public void Split_ignores_an_enabled_id_the_recipe_does_not_stack()
    {
        var split = StackPreview.Split(
            Stack("bg", "shades"), "shades", new[] { "bg", "loose-hat-from-the-kitchen" });

        Assert.Equal(new[] { "bg" }, split.Below);
        Assert.Empty(split.Above);
    }

    /// <summary>The edited layer is the one id that cannot be shrugged off: without it there is no
    /// depth to split around.</summary>
    [Fact]
    public void Split_of_a_layer_the_recipe_does_not_stack_is_a_key_not_found()
    {
        var ex = Assert.Throws<KeyNotFoundException>(
            () => StackPreview.Split(Stack("bg", "hat"), "shades", Array.Empty<string>()));

        Assert.Contains("shades", ex.Message);
        Assert.Contains("cat", ex.Message);
    }

    // ---- rendering ---------------------------------------------------------------------------------

    /// <summary>"Zero references on" is the feature's default state, so an empty stack is legal and
    /// renders the canvas it was asked for, fully transparent.</summary>
    [Fact]
    public void An_empty_stack_is_a_fully_transparent_canvas()
    {
        using var image = StackPreview.Render(Canvas, Array.Empty<PreviewLayer>());

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        foreach (var (x, y) in Corners) Assert.Equal(Clear, image[x, y]);
    }

    /// <summary>Custom layers are composited as-is and never colorized, so the art's own colour must
    /// survive the stack exactly — the colour spec here would be violently visible if it were
    /// applied.</summary>
    [Fact]
    public void A_custom_layer_passes_through_uncolorized()
    {
        var fill = new Rgba32(10, 200, 30, 255);
        using var art = Custom("art", fill);

        using var image = StackPreview.Render(Canvas, new[] { new PreviewLayer(art, "v", "hex:ff0000") });

        foreach (var (x, y) in Corners) Assert.Equal(fill, image[x, y]);
    }

    /// <summary>
    /// The "one rendering rule" property, pinned by running the other implementation rather than by a
    /// number typed into the test: a colorized layer in a stack must be byte-for-byte what
    /// <c>VariantPreview.Render</c> produces alone. A second colorizer living here is exactly how the
    /// CLI's preview and the GUI's preview stop being the same image.
    /// </summary>
    [Theory]
    [InlineData(LayerKind.Dynamic)]
    [InlineData(LayerKind.Static)]
    public void A_colorized_layer_is_pixel_identical_to_what_VariantPreview_renders(LayerKind kind)
    {
        using var ing = Gray("aura", kind, 128);

        using var stacked = StackPreview.Render(Canvas, new[] { new PreviewLayer(ing, "v", "hex:d6249f") });
        using var alone = VariantPreview.Render(ing, "v", "hex:d6249f");

        for (int y = 0; y < Canvas.Height; y++)
            for (int x = 0; x < Canvas.Width; x++)
                Assert.Equal(alone[x, y], stacked[x, y]);

        // And not vacuously: the value-map went in gray and came out coloured.
        Assert.NotEqual(new Rgba32(128, 128, 128, 255), stacked[0, 0]);
    }

    /// <summary>A colorized layer still refuses to guess a colour inside a stack — the rule has one
    /// owner and the stack does not re-implement or soften it.</summary>
    [Fact]
    public void A_colorized_layer_with_no_colour_is_still_refused_inside_a_stack()
    {
        using var ing = Gray("aura", LayerKind.Static, 128);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StackPreview.Render(Canvas, new[] { new PreviewLayer(ing, "v", null) }));

        Assert.Contains("colour is required", ex.Message);
    }

    /// <summary>Never scaled: a preview that silently resizes is lying about alignment, which is the
    /// one thing this feature exists to show. The message has to say enough to fix it — which layer,
    /// which variant, and both sizes.
    ///
    /// <para>The check is load-bearing rather than belt-and-braces: deleting it was tried, and
    /// nothing downstream objected. <c>Compositor.Composite</c> draws at the origin and clips, so a
    /// 4x4 layer on a 2x2 canvas renders its top-left quarter and reports success — art that looks
    /// merely mispositioned, in the one tool whose job is showing where art lands.</para></summary>
    [Fact]
    public void A_layer_that_does_not_match_the_canvas_is_rejected_naming_both_sizes()
    {
        using var floor = Custom("bg", Red);
        using var oversized = new LoadedIngredient
        {
            Manifest = new IngredientManifest("shades", "Sunglasses", LayerKind.Custom, null,
                new[] { new Variant("wide", "Wide", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["wide"] = new Image<Rgba32>(4, 4, Blue) },
        };

        var ex = Assert.Throws<InvalidOperationException>(() => StackPreview.Render(Canvas, new[]
        {
            new PreviewLayer(floor, "v", null),
            new PreviewLayer(oversized, "wide", null),
        }));

        Assert.Contains("4x4", ex.Message);
        Assert.Contains("2x2", ex.Message);
        Assert.Contains("Sunglasses", ex.Message);
        Assert.Contains("wide", ex.Message);
    }

    /// <summary>A struct cannot enforce a non-null field, so <c>default(PreviewLayer)</c> is
    /// constructible and must be named rather than dereferenced — and the layers rendered before it
    /// must still be freed.</summary>
    [Fact]
    public void A_layer_with_no_ingredient_is_named_by_index_rather_than_dereferenced()
    {
        using var floor = Custom("bg", Red);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var ex = Assert.Throws<ArgumentException>(() => StackPreview.Render(
            Canvas, new[] { new PreviewLayer(floor, "v", null), default }));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);

        Assert.Contains("1", ex.Message);
    }

    // ---- ownership ----------------------------------------------------------------------------------

    /// <summary>Render disposes every intermediate it creates — and an ingredient's own image is not
    /// one of them. Proven by rendering the same ingredient again afterwards, which a disposed source
    /// image could not survive.</summary>
    [Fact]
    public void Rendering_never_disposes_an_ingredients_own_images()
    {
        var fill = new Rgba32(7, 8, 9, 255);
        using var art = Custom("art", fill);
        var source = art.VariantImages["v"];

        using (var first = StackPreview.Render(Canvas, new[] { new PreviewLayer(art, "v", null) }))
            Assert.Equal(fill, first[0, 0]);

        Assert.Equal(fill, source[0, 0]);

        using var second = StackPreview.Render(Canvas, new[] { new PreviewLayer(art, "v", null) });
        Assert.Equal(fill, second[0, 0]);
    }

    [Fact]
    public void Render_leaks_nothing_it_created()
    {
        using var bottom = Custom("bg", Red);
        using var middle = Gray("aura", LayerKind.Dynamic, 128);
        using var top = Custom("hat", Blue);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        using (var image = StackPreview.Render(Canvas, new[]
        {
            new PreviewLayer(bottom, "v", null),
            new PreviewLayer(middle, "v", "hex:d6249f"),
            new PreviewLayer(top, "v", null),
        }))
        {
            Assert.Equal(Blue, image[0, 0]);

            // The counter must actually MOVE for images this small, or every leak assertion in this
            // file — including the two failure-path ones below, which cannot check mid-flight —
            // would be passing vacuously against a counter that never budges.
            Assert.True(MemoryDiagnostics.TotalUndisposedAllocationCount > before);
        }

        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    /// <summary>A throw partway through the stack must not strand the layers already rendered: they
    /// have no owner but Render itself until compositing succeeds, so a leak here would be one per
    /// repaint — and repaints are what this feature does.</summary>
    [Fact]
    public void A_canvas_mismatch_partway_through_the_stack_disposes_what_was_already_rendered()
    {
        using var floor = Custom("bg", Red);                 // renders fine, into the accumulator
        using var oversized = Custom("hat", Blue, 4, 4);     // rejected, with 'bg' already alive

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        Assert.Throws<InvalidOperationException>(() => StackPreview.Render(Canvas, new[]
        {
            new PreviewLayer(floor, "v", null),
            new PreviewLayer(oversized, "v", null),
        }));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    /// <summary>The same guarantee for a failure inside the renderer rather than a check around it: a
    /// pre-disposed value-map makes <c>Colorizer.Apply</c>'s internal <c>Clone()</c> throw halfway up
    /// the stack — the shape <c>GeneratorRollOneDisposalTests</c> drives <c>Generator.Render</c> with.</summary>
    [Fact]
    public void A_layer_that_throws_while_rendering_disposes_the_layers_below_it()
    {
        using var floor = Custom("bg", Red);

        var brokenSource = new Image<Rgba32>(2, 2, new Rgba32(128, 128, 128, 255));
        brokenSource.Dispose();
        var broken = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "aura", LayerKind.Dynamic, Hsv(),
                new[] { new Variant("v", "V", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = brokenSource },
        };

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        Assert.Throws<ObjectDisposedException>(() => StackPreview.Render(Canvas, new[]
        {
            new PreviewLayer(floor, "v", null),
            new PreviewLayer(broken, "v", "hex:d6249f"),
        }));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    // ---- PickVariant --------------------------------------------------------------------------------

    [Fact]
    public void PickVariant_takes_the_highest_weight()
    {
        using var ing = Weighted(("a", 1), ("b", 9), ("c", 3));

        Assert.Equal("b", StackPreview.PickVariant(ing));
    }

    /// <summary>Ties break ordinal-first. 'B' (0x42) sorts before 'a' (0x61) ordinally and AFTER it
    /// under a culture-aware comparison — so a default <c>OrderBy</c> would answer "a" here, and could
    /// answer differently on another machine's locale.</summary>
    [Fact]
    public void PickVariant_breaks_a_tie_ordinal_first()
    {
        using var ing = Weighted(("a", 5), ("B", 5));

        Assert.Equal("B", StackPreview.PickVariant(ing));
    }

    /// <summary>No RNG anywhere: a reference layer that changed appearance between repaints would make
    /// the alignment this feature exists to show unreadable.</summary>
    [Fact]
    public void PickVariant_is_stable_across_repeated_calls()
    {
        using var ing = Weighted(("a", 5), ("b", 5), ("c", 5));

        var picks = Enumerable.Range(0, 25)
            .Select(_ => StackPreview.PickVariant(ing))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "a" }, picks);
    }

    /// <summary>A zero weight shelves a variant from generation but does not hide it from a preview —
    /// an ingredient whose variants are all shelved still has art worth looking at.</summary>
    [Fact]
    public void PickVariant_still_answers_when_every_variant_is_shelved()
    {
        using var ing = Weighted(("b", 0), ("a", 0));

        Assert.Equal("a", StackPreview.PickVariant(ing));
    }

    [Fact]
    public void PickVariant_of_an_ingredient_with_no_variants_says_so()
    {
        using var ing = Weighted();

        var ex = Assert.Throws<InvalidOperationException>(() => StackPreview.PickVariant(ing));

        Assert.Contains("no variants", ex.Message);
        Assert.Contains("Sunglasses", ex.Message);
    }

    /// <summary>The two halves meet: the variant picked is the one that renders.</summary>
    [Fact]
    public void The_picked_variant_is_the_one_that_renders()
    {
        using var ing = Weighted(("a", 1), ("b", 9));

        string picked = StackPreview.PickVariant(ing);
        using var image = StackPreview.Render(Canvas, new[] { new PreviewLayer(ing, picked, null) });

        // Weighted fills each variant's red channel with its id's first byte, so the pixel says
        // which variant was drawn.
        Assert.Equal(new Rgba32((byte)'b', 0, 0, 255), image[0, 0]);
    }
}
