using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Turns an <see cref="IngredientDraft"/> into the pair the existing
/// <see cref="Formats.IngredientArchive"/> writes: a manifest plus one live image per variant id.
/// Callers own the returned images.
/// </summary>
/// <remarks>
/// <see cref="IngredientDraft.Kind"/> alone decides which raster a variant exports — its
/// <see cref="VariantDraft.Color"/> for Custom, its <see cref="VariantDraft.Map"/> for Dynamic and
/// Static. A variant painted in color still holds both, so reading "whichever is non-null" would
/// quietly export color art from a value-map layer and destroy the layer's color space.
/// </remarks>
public static class IngredientDraftExporter
{
    /// <summary>Turns a draft into the manifest and images an <c>.igt</c> is written from.</summary>
    /// <param name="draft">The draft to export.</param>
    /// <returns>The manifest and one image per variant; the caller owns the images.</returns>
    /// <exception cref="InvalidOperationException">Two variants share an id, or a Custom variant has
    /// no color raster to write.</exception>
    public static (IngredientManifest Manifest, IReadOnlyDictionary<string, Image<Rgba32>> Images) Export(
        IngredientDraft draft)
    {
        bool custom = draft.Kind == LayerKind.Custom;

        // Validate every variant BEFORE materialising a single image: rendering as we go would leak
        // each image already built when a later variant turned out to be unexportable.
        var ids = new HashSet<string>();
        foreach (var v in draft.Variants)
        {
            if (!ids.Add(v.Id))
                throw new InvalidOperationException($"Duplicate variant id '{v.Id}' in ingredient '{draft.Id}'.");
            if (custom && v.Color is null)
                throw new InvalidOperationException(
                    $"Variant '{v.Name}' in custom ingredient '{draft.Id}' has no image.");
        }

        var variants = draft.Variants.Select(v => new Variant(v.Id, v.Name, v.Weight)).ToList();
        var manifest = new IngredientManifest(draft.Id, draft.Name, draft.Kind, draft.Colorization, variants);
        var images = draft.Variants.ToDictionary(
            v => v.Id,
            v => custom ? v.Color!.ToImage() : v.Map.ToImage(),
            StringComparer.Ordinal);
        return (manifest, images);
    }
}
