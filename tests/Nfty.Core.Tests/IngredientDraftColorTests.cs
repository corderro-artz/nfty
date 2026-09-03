using Nfty.Core.Editing;
using Nfty.Core.Model;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The draft's two rasters, and which one an export reads. A variant painted in colour carries both
/// — that is what lets a colour save leave the original value-map layer untouched on disk — so
/// "whichever is non-null" is never the rule; the ingredient's KIND is.
/// </summary>
public class IngredientDraftColorTests
{
    private static Dimensions Canvas => new(4, 4);

    private static IngredientDraft Draft(LayerKind kind, params VariantDraft[] variants) =>
        new("ing", "Ing", kind, kind == LayerKind.Custom ? null : Colorization(), Canvas, variants);

    private static Colorization Colorization() =>
        new(ColorModel.Hsv, 12, 4, new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });

    private static VariantDraft Gray(string id)
    {
        var map = ValueMap.ForCanvas(Canvas);
        map.Set(1, 1, 200, 255);
        return new VariantDraft(id, id, 1, map);
    }

    [Fact]
    public void Entering_colour_widens_the_value_map_rather_than_starting_blank()
    {
        var v = Gray("v1");
        var color = v.EnsureColor();

        // The drawing that is already there, in grey — no hue, no saturation, no colorization.
        var lifted = color.Get(1, 1);
        Assert.Equal(200, lifted.R);
        Assert.Equal(200, lifted.G);
        Assert.Equal(200, lifted.B);
        Assert.Equal(255, lifted.A);
        Assert.Equal(0, color.Get(0, 0).A);   // untouched pixels stay erased
    }

    [Fact]
    public void Entering_colour_twice_never_discards_paint()
    {
        var v = Gray("v1");
        v.EnsureColor().Set(1, 1, new Rgba32(10, 20, 30, 255));

        var again = v.EnsureColor();

        Assert.Equal(new Rgba32(10, 20, 30, 255), again.Get(1, 1));
    }

    [Fact]
    public void A_value_map_layer_exports_its_value_map_even_when_it_also_has_colour()
    {
        var v = Gray("v1");
        v.EnsureColor().Set(1, 1, new Rgba32(255, 0, 0, 255));   // painted in colour, not saved as Custom

        var (manifest, images) = IngredientDraftExporter.Export(Draft(LayerKind.Dynamic, v));
        using var img = images["v1"];

        Assert.Equal(LayerKind.Dynamic, manifest.Kind);
        var px = img[1, 1];
        Assert.Equal(200, px.R);
        Assert.Equal(px.R, px.G);   // still a grey — the colour raster was not read
        Assert.Equal(px.G, px.B);
    }

    [Fact]
    public void A_custom_layer_exports_its_colour_map()
    {
        var v = Gray("v1");
        v.EnsureColor().Set(1, 1, new Rgba32(255, 0, 0, 255));

        var (_, images) = IngredientDraftExporter.Export(Draft(LayerKind.Custom, v));
        using var img = images["v1"];

        Assert.Equal(new Rgba32(255, 0, 0, 255), img[1, 1]);
    }

    /// <summary>The GUI cannot reach this — a Custom draft's variants all carry a raster — but the
    /// exporter is the contract point, and a CLI or a future caller building a draft by hand can.</summary>
    [Fact]
    public void A_custom_layer_whose_variant_has_no_colour_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => IngredientDraftExporter.Export(Draft(LayerKind.Custom, Gray("v1"))));

        Assert.Contains("v1", ex.Message);
        Assert.Contains("no image", ex.Message);
    }

    [Fact]
    public void A_custom_variant_added_to_a_draft_is_born_with_a_raster()
    {
        var draft = Draft(LayerKind.Custom, new VariantDraft("v1", "V1", 1, ValueMap.ForCanvas(Canvas),
            ColorMap.ForCanvas(Canvas)));

        var added = draft.AddVariant("v2", "V2", 1);

        Assert.NotNull(added.Color);
        Assert.Equal(0, added.Color!.Get(0, 0).A);   // blank, not a ghost of anything
    }

    [Fact]
    public void A_value_map_variant_added_to_a_draft_has_no_colour_raster_yet()
    {
        var draft = Draft(LayerKind.Dynamic, Gray("v1"));

        Assert.Null(draft.AddVariant("v2", "V2", 1).Color);
    }

    [Fact]
    public void Duplicating_carries_both_rasters_and_shares_neither()
    {
        var draft = Draft(LayerKind.Custom, new VariantDraft("v1", "V1", 1, ValueMap.ForCanvas(Canvas),
            ColorMap.ForCanvas(Canvas)));
        draft.Variants[0].Map.Set(1, 1, 200, 255);
        draft.Variants[0].Color!.Set(1, 1, new Rgba32(255, 0, 0, 255));

        var copy = draft.DuplicateVariant("v1", "v2", "Copy");
        copy.Map.Set(1, 1, 10, 255);
        copy.Color!.Set(1, 1, new Rgba32(0, 0, 255, 255));

        Assert.Equal(200, draft.Variants[0].Map.GetValue(1, 1));                       // source untouched
        Assert.Equal(new Rgba32(255, 0, 0, 255), draft.Variants[0].Color!.Get(1, 1));
    }

    /// <summary>Export validates every variant before materialising any image, so a draft that turns
    /// out to be unexportable halfway through leaks nothing.</summary>
    [Fact]
    public void A_duplicate_id_is_refused_before_any_image_is_built()
    {
        var a = Gray("dup");
        var b = Gray("dup");

        Assert.Throws<InvalidOperationException>(
            () => IngredientDraftExporter.Export(Draft(LayerKind.Dynamic, a, b)));
    }
}
