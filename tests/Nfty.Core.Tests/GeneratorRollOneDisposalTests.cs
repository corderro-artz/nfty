using System.Reflection;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Generator.Render accumulates one Image&lt;Rgba32&gt; per layer into a local list as it walks a
/// rolled asset's plan (cloning a custom layer's source, or colorizing a dynamic/static one). Those
/// images have no owner but Render itself until compositing succeeds. If a later layer's
/// Colorizer.Apply or Compositor.Composite throws, every image already added for an earlier layer
/// must be disposed before the exception propagates, or it leaks on every reroll.
///
/// Rolling and rendering are separate methods — the roll decides the asset and computes its DNA
/// without touching a pixel, so a duplicate is discarded before anything is rendered — and this
/// drives both, because the images only exist in the second.
///
/// Both are private, so this reaches them via reflection to test the unit directly rather than
/// through Generator.Generate/GenerateStreaming — which run Validator.Validate first, and would
/// reject the pre-disposed-source-image setup below before either ever ran.
///
/// Disposal is asserted via ImageSharp's process-wide leak counter,
/// <see cref="MemoryDiagnostics.TotalUndisposedAllocationCount"/> (see
/// <see cref="ArchiveReadDisposalTests"/> for why this counter and not a GC-based check).
/// </summary>
public class GeneratorRollOneDisposalTests
{
    private static readonly MethodInfo RollOneMethod =
        typeof(Generator).GetMethod("RollOne", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Generator.RollOne not found by reflection; has its signature changed?");

    private static readonly MethodInfo RenderMethod =
        typeof(Generator).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Generator.Render not found by reflection; has its signature changed?");

    // RollOne takes its layers pre-resolved (ingredient + prepared weight table + variants by id),
    // built once per run instead of per roll attempt. That plan is a private type, so it is built
    // by calling the same private helper generation itself calls rather than reconstructed here —
    // which also keeps this test bound to the real production shape of a layer plan.
    private static readonly MethodInfo PlanLayersMethod =
        typeof(Generator).GetMethod("PlanLayers", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Generator.PlanLayers not found by reflection; has its signature changed?");

    private static GeneratedAsset? RollOne(Dimensions canvas, LoadedRecipe recipe, IRng rng, int number)
    {
        try
        {
            object? layers = PlanLayersMethod.Invoke(null, new object?[] { recipe });
            object? rolled = RollOneMethod.Invoke(null, new object?[] { recipe, layers, rng });
            if (rolled is null) return null;                 // rules rejected the selection
            return (GeneratedAsset?)RenderMethod.Invoke(null, new object?[] { canvas, rolled, number });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Reflection wraps the real exception; unwrap so this behaves like a direct call.
            throw ex.InnerException;
        }
    }

    [Fact]
    public void RollOne_disposes_an_earlier_layers_image_when_a_later_layers_colorizer_throws()
    {
        // Layer "bg": custom, decodes/clones fine — RollOne clones its source image into the
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
        // after "bg"'s clone is already sitting in RollOne's accumulator.
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

        int before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        Assert.Throws<ObjectDisposedException>(
            () => RollOne(new Dimensions(2, 2), recipe, new SplitMix64Rng(1), 1));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }
}
