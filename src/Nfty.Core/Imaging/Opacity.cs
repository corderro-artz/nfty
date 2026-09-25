using Nfty.Core.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

/// <summary>
/// Whether a pixel — or a whole image, layer or book — carries partial alpha: a level of
/// transparency that is neither fully there nor fully absent. This is the single definition of
/// "partial" the rest of the product reads, including <see cref="Stats.VoxelReport"/>'s per-pixel
/// count, and it is also what the derived "2D only" flag (Rise) is built from.
/// </summary>
public static class Opacity
{
    /// <summary>Whether a pixel is fully opaque.</summary>
    /// <param name="p">The pixel to test.</param>
    /// <returns><c>true</c> when the pixel's alpha is 255.</returns>
    public static bool IsSolid(Rgba32 p) => p.A == 255;

    /// <summary>Whether a pixel is partly transparent — neither fully opaque nor fully erased.</summary>
    /// <param name="p">The pixel to test.</param>
    /// <returns><c>true</c> when the pixel's alpha is neither 0 nor 255.</returns>
    public static bool IsPartial(Rgba32 p) => p.A != 0 && p.A != 255;

    /// <summary>Whether an image contains any partially transparent pixel. Stops at the first one it
    /// finds.</summary>
    /// <param name="img">The image to scan.</param>
    /// <returns><c>true</c> as soon as a partial pixel is found.</returns>
    public static bool HasPartialAlpha(Image<Rgba32> img)
    {
        bool found = false;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !found; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (IsPartial(row[x])) { found = true; break; }
                }
            }
        });
        return found;
    }

    /// <summary>Whether any image in a sequence contains a partially transparent pixel. Stops at the
    /// first one it finds.</summary>
    /// <param name="images">The images to scan, in order.</param>
    /// <returns><c>true</c> as soon as a partial pixel is found in any image.</returns>
    public static bool HasPartialAlpha(IEnumerable<Image<Rgba32>> images)
    {
        foreach (var img in images)
            if (HasPartialAlpha(img))
                return true;
        return false;
    }

    /// <summary>Whether any variant image of a layer carries partial alpha.</summary>
    /// <param name="ing">The layer to scan.</param>
    /// <returns><c>true</c> as soon as a partial pixel is found in any of its variants.</returns>
    public static bool HasPartialAlpha(LoadedIngredient ing) => HasPartialAlpha(ing.VariantImages.Values);

    /// <summary>Whether any layer's variant image in a recipe carries partial alpha.</summary>
    /// <param name="recipe">The recipe to scan.</param>
    /// <returns><c>true</c> as soon as a partial pixel is found in any of its layers.</returns>
    public static bool HasPartialAlpha(LoadedRecipe recipe)
    {
        foreach (var ing in recipe.Ingredients)
            if (HasPartialAlpha(ing))
                return true;
        return false;
    }

    /// <summary>Whether any variant image anywhere in a book carries partial alpha.</summary>
    /// <param name="book">The book to scan.</param>
    /// <returns><c>true</c> as soon as a partial pixel is found anywhere in it.</returns>
    public static bool HasPartialAlpha(LoadedCookBook book)
    {
        foreach (var recipe in book.Recipes)
            if (HasPartialAlpha(recipe))
                return true;
        return false;
    }
}
