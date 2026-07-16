using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Formats;

/// <summary>
/// An ingredient manifest paired with its eagerly-decoded variant images. **Owns those images**:
/// dispose it (or the recipe/cookbook above it) to free them.
/// </summary>
public class LoadedIngredient : IDisposable
{
    public required IngredientManifest Manifest { get; init; }
    public required IReadOnlyDictionary<string, Image<Rgba32>> VariantImages { get; init; }

    public void Dispose()
    {
        foreach (var img in VariantImages.Values) img.Dispose();
    }
}

/// <summary>Owns its ingredients; disposing it disposes them and their images.</summary>
public class LoadedRecipe : IDisposable
{
    public required RecipeManifest Manifest { get; init; }
    public required IReadOnlyList<LoadedIngredient> Ingredients { get; init; }

    public void Dispose()
    {
        foreach (var ing in Ingredients) ing.Dispose();
    }
}

/// <summary>
/// Owns its recipes, which own their ingredients, which own the variant images — so
/// <c>using var book = CookBookArchive.Read(path)</c> frees the whole tree. Reading an archive
/// pulls every variant PNG into memory, so a caller that opens books repeatedly (a GUI) must
/// dispose each one.
/// </summary>
public class LoadedCookBook : IDisposable
{
    public required CookBookManifest Manifest { get; init; }
    public required IReadOnlyList<LoadedRecipe> Recipes { get; init; }

    /// <summary>
    /// SHA-256 of the <c>.cbk</c> this was read from, recorded into a generated Set so an
    /// output can be traced back to the exact archive that produced it. Null for a book that
    /// never came from a file (built in memory, or still unsaved in an editor).
    /// </summary>
    public string? SourceSha256 { get; init; }

    public void Dispose()
    {
        foreach (var recipe in Recipes) recipe.Dispose();
    }
}
