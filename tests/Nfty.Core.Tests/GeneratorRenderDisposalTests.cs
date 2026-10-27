using System.Reflection;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Generator.Render accumulates one Image&lt;Rgba32&gt; per layer into a local list as it walks a
/// ticket's layers (cloning a custom layer's source, or colorizing a dynamic/static one). Those
/// images have no owner but Render itself until compositing succeeds. If a later layer's
/// Colorizer.Apply or Compositor.Composite throws, every image already added for an earlier layer
/// must be disposed before the exception propagates, or it leaks on every reroll.
///
/// Rolling and rendering are separate: <c>Ticketer.TryRoll</c> decides the asset and computes its
/// DNA without touching a pixel, so a duplicate is discarded before anything is rendered, and
/// <c>Generator.Render</c> is the only place a pixel is touched. This test drives Render directly
/// with a hand-built <see cref="AssetTicket"/> — the same shape Ticketer would hand it — because
/// Render is private, and going through Generator.Generate/GenerateStreaming would run
/// Validator.Validate first, which would reject the pre-disposed-source-image setup below before
/// either ever ran.
///
/// Disposal is asserted via ImageSharp's process-wide leak counter,
/// <see cref="MemoryDiagnostics.TotalUndisposedAllocationCount"/> (see
/// <see cref="ArchiveReadDisposalTests"/> for why this counter and not a GC-based check).
/// </summary>
public class GeneratorRenderDisposalTests
{
    private static readonly MethodInfo RenderMethod =
        typeof(Generator).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Generator.Render not found by reflection; has its signature changed?");

    private static GeneratedAsset? Render(
        Dimensions canvas,
        LoadedRecipe recipe,
        IReadOnlyDictionary<string, LoadedIngredient> ingById,
        AssetTicket ticket,
        int number)
    {
        try
        {
            return (GeneratedAsset?)RenderMethod.Invoke(
                null, new object?[] { canvas, recipe, ingById, ticket, number, Parallelism.Sequential.ImageConfiguration(false) });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Reflection wraps the real exception; unwrap so this behaves like a direct call.
            throw ex.InnerException;
        }
    }

    [Fact]
    public void Render_disposes_an_earlier_layers_image_when_a_later_layers_colorizer_throws()
    {
        // Layer "bg": custom, decodes/clones fine — Render clones its source image into the
        // local accumulator before moving to the next layer.
        var bg = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                new[] { new Variant("v", "V", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["v"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)) },
        };

        // Layer "fg": dynamic, but its source value-map is pre-disposed. Colorizer.Apply's
        // internal Clone() then throws ObjectDisposedException partway through the layer stack,
        // after "bg"'s clone is already sitting in Render's accumulator.
        var brokenSource = new Image<Rgba32>(2, 2, new Rgba32(128, 128, 128, 255));
        brokenSource.Dispose();
        var fg = new LoadedIngredient
        {
            Manifest = new IngredientManifest("fg", "fg", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 5, 5,
                    new[] { new ColorEntry(1, new ColorRange(0, 360, 0, 100), null) }),
                new[] { new Variant("v", "V", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = brokenSource },
        };

        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "bg", "fg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { bg, fg },
        };
        var ingById = new Dictionary<string, LoadedIngredient> { ["bg"] = bg, ["fg"] = fg };

        var layers = new[]
        {
            new TicketLayer("bg", "v", null, null, null, LayerKind.Custom, 0, 0),
            new TicketLayer("fg", "v", 180, 0.5, ColorModel.Hsv, LayerKind.Dynamic, 5, 5),
        };
        var ticket = new AssetTicket(AssetTicket.ComputeDna("r", layers), "r", layers);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        Assert.Throws<ObjectDisposedException>(
            () => Render(new Dimensions(2, 2), recipe, ingById, ticket, 1));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    [Fact]
    public void Render_disposes_every_image_when_a_late_step_throws()
    {
        var bg = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                new[] { new Variant("v", "V", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["v"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)) },
        };
        // layerOrder names "ghost": absent from the ticket AND from ingById, so resolving the
        // absent layers' names throws. That used to happen AFTER compositing, leaking the canvas.
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", new[] { "bg", "ghost" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { bg },
        };
        var ingById = new Dictionary<string, LoadedIngredient> { ["bg"] = bg };
        var layers = new[] { new TicketLayer("bg", "v", null, null, null, LayerKind.Custom, 0, 0) };
        var ticket = new AssetTicket(AssetTicket.ComputeDna("r", layers), "r", layers);

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        Assert.Throws<KeyNotFoundException>(
            () => Render(new Dimensions(2, 2), recipe, ingById, ticket, 1));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }
}
