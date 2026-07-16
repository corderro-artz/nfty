using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Formats;

public class LoadedIngredient
{
    public required IngredientManifest Manifest { get; init; }
    public required IReadOnlyDictionary<string, Image<Rgba32>> VariantImages { get; init; }
}

public class LoadedRecipe
{
    public required RecipeManifest Manifest { get; init; }
    public required IReadOnlyList<LoadedIngredient> Ingredients { get; init; }
}

public class LoadedCookBook
{
    public required CookBookManifest Manifest { get; init; }
    public required IReadOnlyList<LoadedRecipe> Recipes { get; init; }

    /// <summary>
    /// SHA-256 of the <c>.cbk</c> this was read from, recorded into a generated Set so an
    /// output can be traced back to the exact archive that produced it. Null for a book that
    /// never came from a file (built in memory, or still unsaved in an editor).
    /// </summary>
    public string? SourceSha256 { get; init; }
}
