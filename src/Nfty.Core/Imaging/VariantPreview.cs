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
        var image = SourceImage(ingredient, variantId);
        if (!NeedsColor(ingredient)) return image.Clone();   // Custom — as-is, never colorized

        if (colorSpec is null)
            throw new InvalidOperationException(
                $"A colour is required to preview '{ingredient.Manifest.Name}': it is a "
                + $"{ingredient.Manifest.Kind.ToString().ToLowerInvariant()} layer, colorized from "
                + "a value-map at generation time.");

        var model = ModelFor(ingredient, modelOverride);

        // The same spec→(H,S) resolution a Static layer gets at generation time.
        var (h, s) = ColorRoller.FromFixed(colorSpec, model);
        return Colorizer.Apply(image, h, s, model);
    }

    /// <summary>
    /// The rendered variant, colorized from an <b>already-rolled</b> hue and saturation rather than
    /// from a spec string. The caller owns the returned image, exactly as with the spec overload.
    ///
    /// <para>This is the path generation itself takes: <c>ColorRoller.Roll</c> produces a continuous
    /// <see cref="RolledColor"/> and hands it straight to <c>Colorizer.Apply</c>, with no string in
    /// between. The spec overload <i>cannot</i> match that for a rolled colour — every colour spec
    /// resolves back through 8-bit RGB, so a hue that fell between two representable colours comes
    /// back as the nearest one and the preview lands within a rounding step of the asset instead of
    /// on it. Small (about a quarter-degree of hue), but this class's whole promise is "exactly as
    /// generation would", and a value that was never quantized must not be quantized on its way to
    /// being shown.</para>
    ///
    /// <para>A Static layer needs no such overload: its colour is an author-written spec, so
    /// generation resolves the same string through the same parser and the spec overload is already
    /// exact. Only a <i>rolled</i> colour has a value with no exact spelling.</para>
    /// </summary>
    /// <param name="ingredient">The layer the variant belongs to; supplies the kind and the
    /// colour model.</param>
    /// <param name="variantId">Which variant of it to render.</param>
    /// <param name="color">The rolled hue and saturation, unrounded.</param>
    /// <param name="modelOverride">Renders in a different colour model than the ingredient declares.
    /// Null uses the ingredient's own.</param>
    /// <returns>The rendered image; the caller owns it. A Custom layer is cloned as-is and the colour
    /// is ignored, since Custom is never colorized.</returns>
    public static Image<Rgba32> Render(LoadedIngredient ingredient, string variantId,
        RolledColor color, ColorModel? modelOverride = null)
    {
        var image = SourceImage(ingredient, variantId);
        if (!NeedsColor(ingredient)) return image.Clone();   // Custom — as-is, never colorized
        return Colorizer.Apply(image, color.H, color.S, ModelFor(ingredient, modelOverride));
    }

    /// <summary>Resolves the variant's image and settles the one case that needs no colour at all.
    /// Custom layers are full-colour RGBA composited as-is and are never colorized — their
    /// Colorization is always null — so a preview claiming to show what generation renders must pass
    /// them through rather than applying a colour generation never applies.</summary>
    /// <returns>The ingredient's own image for that variant. Borrowed, never owned: both overloads
    /// either clone it or hand it to <c>Colorizer.Apply</c>, which returns a new image.</returns>
    private static Image<Rgba32> SourceImage(LoadedIngredient ingredient, string variantId)
    {
        if (ingredient.VariantImages.TryGetValue(variantId, out var image)) return image;

        string validIds = string.Join(", ", ingredient.Manifest.Variants.Select(v => v.Id));
        throw new InvalidOperationException(
            $"Ingredient '{ingredient.Manifest.Name}' has no variant '{variantId}'. "
            + $"Valid variant ids: {validIds}.");
    }

    /// <summary>The colour model to render in: the caller's override, else the layer's own.
    ///
    /// <para>The missing-colorization check comes FIRST and is deliberately not folded into the
    /// <c>??</c> beside the override. Written as <c>modelOverride ?? (Colorization ?? throw).Model</c>
    /// the right operand — the throw with it — is never evaluated when an override is supplied, so a
    /// colorized layer carrying no colorization block would quietly render whenever a caller happened
    /// to pass <paramref name="modelOverride"/>. That is a broken manifest either way: the block is
    /// where the colour ranges, the entry weights and the DNA quantize steps live, so an override
    /// supplies a model for a layer generation could not colorize at all.</para></summary>
    private static ColorModel ModelFor(LoadedIngredient ingredient, ColorModel? modelOverride)
    {
        var colorization = ingredient.Manifest.Colorization
            ?? throw new InvalidOperationException(
                $"Ingredient '{ingredient.Manifest.Name}' is {ingredient.Manifest.Kind} but carries no "
                + "colorization block, so there is no colour model to render it in.");
        return modelOverride ?? colorization.Model;
    }

    /// <summary>Whether <paramref name="ingredient"/> needs a colour to be previewed. Custom does
    /// not; a UI can use this to decide whether to ask.</summary>
    public static bool NeedsColor(LoadedIngredient ingredient) =>
        ingredient.Manifest.Kind != LayerKind.Custom;
}
