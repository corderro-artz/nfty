using System.Linq;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class IngredientDraftExporterTests
{
    private static IngredientDraft TwoVariantDraft()
    {
        var draft = new IngredientDraft("body", "Body", LayerKind.Dynamic, null,
            new Dimensions(2, 2), System.Array.Empty<VariantDraft>());
        var a = draft.AddVariant("slime", "Slime", 40);
        a.Map.Set(0, 0, 128, 255);
        draft.AddVariant("fluff", "Fluff", 25);
        return draft;
    }

    [Fact]
    public void Export_maps_variants_and_grayscale_images()
    {
        var (manifest, images) = IngredientDraftExporter.Export(TwoVariantDraft());
        Assert.Equal("body", manifest.Id);
        Assert.Equal(LayerKind.Dynamic, manifest.Kind);
        Assert.Equal(2, manifest.Variants.Count);
        Assert.Equal(new Rgba32(128, 128, 128, 255), images["slime"][0, 0]);
        foreach (var img in images.Values) img.Dispose();
    }

    [Fact]
    public void Export_round_trips_through_the_igt_archive()
    {
        var (manifest, images) = IngredientDraftExporter.Export(TwoVariantDraft());
        var dir = System.IO.Directory.CreateTempSubdirectory();
        var path = System.IO.Path.Combine(dir.FullName, "body.igt");
        IngredientArchive.Write(path, manifest, images);
        foreach (var img in images.Values) img.Dispose();

        using var loaded = IngredientArchive.Read(path);
        Assert.Equal("body", loaded.Manifest.Id);
        Assert.Equal(2, loaded.Manifest.Variants.Count);
        Assert.Equal(new Rgba32(128, 128, 128, 255), loaded.VariantImages["slime"][0, 0]);
    }

    [Fact]
    public void Duplicate_variant_ids_throw()
    {
        var draft = new IngredientDraft("body", "Body", LayerKind.Dynamic, null,
            new Dimensions(2, 2), System.Array.Empty<VariantDraft>());
        draft.AddVariant("dup", "One", 1);
        draft.AddVariant("dup", "Two", 1);
        Assert.Throws<System.InvalidOperationException>(() => IngredientDraftExporter.Export(draft));
    }
}
