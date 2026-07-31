using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Tests;

/// <summary>Minimal 1-recipe, 2-variant custom cookbook (custom = no colorization) with an 8x8
/// canvas, for App tests that need a real LoadedCookBook to cook and read a Set from.
/// Mirrors Nfty.Core.Tests' SetReaderTests.TinyBook() since App tests can't see Core test internals.</summary>
internal static class CoreTestBook
{
    public static LoadedCookBook Tiny()
    {
        LoadedIngredient Ing() => new()
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1), new Variant("b", "B", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["a"] = new(8, 8), ["b"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing() },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("VaporCats", "desc", "VC"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
    }
}
