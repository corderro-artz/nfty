using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>One variant that making a book voxel-ready would change, and by how much.</summary>
/// <param name="RecipeId">The recipe holding the layer.</param>
/// <param name="IngredientId">The layer's id.</param>
/// <param name="IngredientName">The layer's display name.</param>
/// <param name="VariantId">The variant's id.</param>
/// <param name="VariantName">The variant's display name.</param>
/// <param name="ToSolid">Partial pixels that become fully opaque (alpha at or above the threshold).</param>
/// <param name="ToClear">Partial pixels that become fully clear.</param>
public sealed record VoxelReadyChange(string RecipeId, string IngredientId, string IngredientName,
    string VariantId, string VariantName, int ToSolid, int ToClear);

/// <summary>
/// What <see cref="VoxelReady.Apply"/> did: the snapped book plus enough to undo it. Owns the
/// pre-snap images it holds; dispose it when the undo is no longer wanted.
/// </summary>
public sealed class VoxelReadyResult : IDisposable
{
    internal VoxelReadyResult(LoadedCookBook book, IReadOnlyList<VoxelReadyChange> changes,
        Dictionary<(string, string, string), Image<Rgba32>> originals,
        Dictionary<(string, string, string), Rgba32[]> snapped)
    {
        Book = book;
        Changes = changes;
        Originals = originals;
        Snapped = snapped;
    }

    /// <summary>The snapped graph. Not owned by this result.</summary>
    public LoadedCookBook Book { get; }

    /// <summary>Every variant changed, ordered by recipe id, ingredient id, variant id (ordinal).</summary>
    public IReadOnlyList<VoxelReadyChange> Changes { get; }

    /// <summary>Clones of each changed variant's pre-snap image, keyed (recipeId, ingredientId, variantId).</summary>
    internal Dictionary<(string recipeId, string ingredientId, string variantId), Image<Rgba32>> Originals { get; }

    /// <summary>The pixels the snap produced, to tell whether anything was edited since.</summary>
    internal Dictionary<(string recipeId, string ingredientId, string variantId), Rgba32[]> Snapped { get; }

    /// <summary>Frees the pre-snap images.</summary>
    public void Dispose()
    {
        foreach (var img in Originals.Values) img.Dispose();
    }
}

/// <summary>
/// Makes a book's art voxel-ready: every partially transparent pixel snaps at 50% to solid or clear,
/// through <see cref="AlphaSnap{TPixel}"/>, so there is one snap rule. Depth maps are untouched — a
/// pixel turning solid has no stored depth, so it rises at paper; one turning clear never had any.
/// </summary>
public static class VoxelReady
{
    /// <summary>Alpha at or above this snaps solid; below it snaps clear.</summary>
    public const byte Threshold = 128;

    private static readonly StringComparer Ord = StringComparer.Ordinal;

    private static IEnumerable<(LoadedRecipe r, LoadedIngredient i)> Layers(LoadedCookBook book) =>
        book.Recipes.OrderBy(r => r.Manifest.Id, Ord)
            .SelectMany(r => r.Ingredients.OrderBy(i => i.Manifest.Id, Ord).Select(i => (r, i)));

    /// <summary>Lists what <see cref="Apply"/> would change. Writes nothing.</summary>
    /// <param name="book">The book to inspect.</param>
    /// <returns>One row per variant with partial alpha, ordinal by recipe, layer, variant.</returns>
    public static IReadOnlyList<VoxelReadyChange> Plan(LoadedCookBook book)
    {
        var rows = new List<VoxelReadyChange>();
        foreach (var (r, i) in Layers(book))
            foreach (var v in i.Manifest.Variants.OrderBy(v => v.Id, Ord))
            {
                if (!i.VariantImages.TryGetValue(v.Id, out var img)) continue;
                var (solid, clear) = Count(img);
                if (solid + clear == 0) continue;
                rows.Add(new VoxelReadyChange(r.Manifest.Id, i.Manifest.Id, i.Manifest.Name,
                    v.Id, v.Name, solid, clear));
            }
        return rows;
    }

    private static (int solid, int clear) Count(Image<Rgba32> img)
    {
        int solid = 0, clear = 0;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                var p = img[x, y];
                if (!Opacity.IsPartial(p)) continue;
                if (p.A >= Threshold) solid++; else clear++;
            }
        return (solid, clear);
    }

    /// <summary>Snaps an image. The command's rule, applied to a copy.</summary>
    /// <param name="art">The image to snap; left untouched.</param>
    /// <returns>A new image the caller owns.</returns>
    public static Image<Rgba32> Snap(Image<Rgba32> art)
    {
        var map = ColorMap.FromImage(art);
        new AlphaSnap<Rgba32>().Apply(map);
        return map.ToImage();
    }

    private static Rgba32[] Pixels(Image<Rgba32> img)
    {
        var buf = new Rgba32[img.Width * img.Height];
        img.CopyPixelDataTo(buf);
        return buf;
    }

    /// <summary>Snaps every partial variant, returning a new graph. Clean variants and depth maps are
    /// shared with <paramref name="book"/>, which is not disposed or modified.</summary>
    /// <param name="book">The book to make voxel-ready.</param>
    /// <returns>The snapped book plus what is needed to undo.</returns>
    public static VoxelReadyResult Apply(LoadedCookBook book)
    {
        var changes = Plan(book);
        var originals = new Dictionary<(string, string, string), Image<Rgba32>>();
        var snapped = new Dictionary<(string, string, string), Rgba32[]>();
        var result = book;
        foreach (var group in changes.GroupBy(c => (c.RecipeId, c.IngredientId)))
        {
            var ing = book.Recipes.Single(r => r.Manifest.Id == group.Key.RecipeId)
                .Ingredients.Single(i => i.Manifest.Id == group.Key.IngredientId);
            var images = new Dictionary<string, Image<Rgba32>>(ing.VariantImages);
            foreach (var c in group)
            {
                var key = (c.RecipeId, c.IngredientId, c.VariantId);
                var before = ing.VariantImages[c.VariantId];
                originals[key] = before.Clone();
                var after = Snap(before);
                snapped[key] = Pixels(after);
                images[c.VariantId] = after;
            }
            result = CookBookEdits.UpsertIngredient(result, group.Key.RecipeId, Rebuilt(ing, images));
        }
        return new VoxelReadyResult(result, changes, originals, snapped);
    }

    private static LoadedIngredient Rebuilt(LoadedIngredient ing, Dictionary<string, Image<Rgba32>> images) => new()
    {
        Manifest = ing.Manifest with { PartialAlpha = Opacity.HasPartialAlpha(images.Values) },
        VariantImages = images,
        Depths = ing.Depths,
    };

    /// <summary>Puts the pre-snap pixels back.</summary>
    /// <param name="current">The book as it stands now.</param>
    /// <param name="applied">What <see cref="Apply"/> returned.</param>
    /// <returns>A new graph with the original pixels restored (as clones; <paramref name="applied"/> stays disposable).</returns>
    /// <exception cref="InvalidOperationException">A snapped variant's pixels have changed since
    /// <see cref="Apply"/>; the message names each, and nothing is restored.</exception>
    public static LoadedCookBook Undo(LoadedCookBook current, VoxelReadyResult applied)
    {
        var edited = new List<string>();
        foreach (var c in applied.Changes)
        {
            var key = (c.RecipeId, c.IngredientId, c.VariantId);
            var ing = Find(current, c.RecipeId, c.IngredientId);
            if (ing is null || !ing.VariantImages.TryGetValue(c.VariantId, out var img)
                || !Pixels(img).AsSpan().SequenceEqual(applied.Snapped[key]))
                edited.Add($"'{c.VariantName}' ({c.IngredientName}, recipe '{c.RecipeId}')");
        }
        if (edited.Count > 0)
            throw new InvalidOperationException(
                "Cannot undo making the book voxel-ready; these variants changed since: "
                + string.Join(", ", edited) + ".");

        var result = current;
        foreach (var group in applied.Changes.GroupBy(c => (c.RecipeId, c.IngredientId)))
        {
            var ing = Find(result, group.Key.RecipeId, group.Key.IngredientId)!;
            var images = new Dictionary<string, Image<Rgba32>>(ing.VariantImages);
            foreach (var c in group)
                images[c.VariantId] = applied.Originals[(c.RecipeId, c.IngredientId, c.VariantId)].Clone();
            result = CookBookEdits.UpsertIngredient(result, group.Key.RecipeId, Rebuilt(ing, images));
        }
        return result;
    }

    private static LoadedIngredient? Find(LoadedCookBook book, string recipeId, string ingredientId) =>
        book.Recipes.FirstOrDefault(r => r.Manifest.Id == recipeId)
            ?.Ingredients.FirstOrDefault(i => i.Manifest.Id == ingredientId);
}
