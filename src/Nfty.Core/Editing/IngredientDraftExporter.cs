using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Turns an <see cref="IngredientDraft"/> into the pair the existing
/// <see cref="Formats.IngredientArchive"/> writes: a manifest plus one live image per variant id.
/// Callers own the returned images.
/// </summary>
public static class IngredientDraftExporter
{
    /// <summary>Turns a draft into the manifest and images an <c>.igt</c> is written from.</summary>
    /// <param name="draft">The draft to export.</param>
    /// <returns>The manifest and one image per variant; the caller owns the images.</returns>
    /// <exception cref="InvalidOperationException">Two variants share an id.</exception>
    public static (IngredientManifest Manifest, IReadOnlyDictionary<string, Image<Rgba32>> Images) Export(
        IngredientDraft draft)
    {
        var ids = new HashSet<string>();
        foreach (var v in draft.Variants)
            if (!ids.Add(v.Id))
                throw new InvalidOperationException($"Duplicate variant id '{v.Id}' in ingredient '{draft.Id}'.");

        var variants = draft.Variants.Select(v => new Variant(v.Id, v.Name, v.Weight)).ToList();
        var manifest = new IngredientManifest(draft.Id, draft.Name, draft.Kind, draft.Colorization, variants);
        var images = draft.Variants.ToDictionary(v => v.Id, v => v.Map.ToImage());
        return (manifest, images);
    }
}
