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
}
