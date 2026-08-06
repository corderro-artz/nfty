using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

/// <summary>
/// Renders one Variant exactly as generation would: colorized from its value-map for Dynamic and
/// Static layers, passed through untouched for Custom.
///
/// This lived inline in the CLI's <c>preview</c> command, whose own comment warned that the point is
/// "a preview shows exactly what generation would render rather than a second, drifting
/// implementation". Wiring the GUI to its own copy would have created exactly that second
/// implementation, so the rule now lives here and both front-ends call it.
/// </summary>
public static class VariantPreview
{
    /// <summary>The rendered variant. The caller owns the returned image and must dispose it —
    /// including for a Custom layer, where a CLONE is returned rather than the ingredient's own
    /// image. Returning the borrowed one for one kind and an owned one for the others would make
    /// disposal depend on the layer kind, which is exactly the sort of ownership rule that gets a
    /// double-free or a leak eventually.</summary>
    /// <param name="ingredient">The layer the variant belongs to; supplies the kind and the
    /// colorization.</param>
    /// <param name="variantId">Which variant of it to render.</param>
    /// <param name="colorSpec">A prefixed colour spec (<c>hex:</c>, <c>rgb:</c>, <c>hsl:</c>,
    /// <c>hsv:</c>). Required for a colorized layer; ignored for Custom.</param>
    /// <param name="modelOverride">Renders in a different colour model than the ingredient declares,
    /// for comparing the two. Null uses the ingredient's own.</param>
    public static Image<Rgba32> Render(LoadedIngredient ingredient, string variantId,
        string? colorSpec = null, ColorModel? modelOverride = null)
    {
        if (!ingredient.VariantImages.TryGetValue(variantId, out var image))
        {
            string validIds = string.Join(", ", ingredient.Manifest.Variants.Select(v => v.Id));
            throw new InvalidOperationException(
                $"Ingredient '{ingredient.Manifest.Name}' has no variant '{variantId}'. "
                + $"Valid variant ids: {validIds}.");
        }

        // Custom layers are full-colour RGBA composited as-is and are never colorized — their
        // Colorization is always null. A preview that claims to show what generation renders must do
        // the same rather than applying a colour generation never applies.
        if (ingredient.Manifest.Kind == LayerKind.Custom) return image.Clone();

        if (colorSpec is null)
            throw new InvalidOperationException(
                $"A colour is required to preview '{ingredient.Manifest.Name}': it is a "
                + $"{ingredient.Manifest.Kind.ToString().ToLowerInvariant()} layer, colorized from "
                + "a value-map at generation time.");

        var colorization = ingredient.Manifest.Colorization
            ?? throw new InvalidOperationException(
                $"Ingredient '{ingredient.Manifest.Name}' is {ingredient.Manifest.Kind} but carries no "
                + "colorization block, so there is no colour model to render it in.");

        var model = modelOverride ?? colorization.Model;

        // The same spec→(H,S) resolution a Static layer gets at generation time.
        var (h, s) = ColorRoller.FromFixed(colorSpec, model);
        return Colorizer.Apply(image, h, s, model);
    }

    /// <summary>Whether <paramref name="ingredient"/> needs a colour to be previewed. Custom does
    /// not; a UI can use this to decide whether to ask.</summary>
    public static bool NeedsColor(LoadedIngredient ingredient) =>
        ingredient.Manifest.Kind != LayerKind.Custom;
}
