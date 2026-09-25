using System.Collections.Generic;
using System.Linq;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// Depth maps must survive every edit path that keeps a variant alive, and drop only the variant
/// that is actually removed. <see cref="CookBookEdits.RemoveVariant"/> is the Core-level carry-through;
/// the Ingredient editor save path and the CLI's `add variant` are covered beside their own tests.
/// </summary>
public class DepthCarryThroughTests
{
    private static LoadedIngredient MultiVariant(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1.0)).ToArray()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(1, 1)),
        Depths = variantIds.ToDictionary(v => v, v => DepthMap.Flat(1, 1).With(0, 0, front: 7, back: 8)),
    };

    private static LoadedCookBook BookWith(LoadedIngredient ing)
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("aurora", "Aurora", new List<string> { ing.Manifest.Id },
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient> { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("vp", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["aurora"] = 1.0 }),
            Recipes = new List<LoadedRecipe> { recipe },
        };
    }

    [Fact]
    public void Removing_a_variant_drops_only_its_own_depth_map()
    {
        var book = BookWith(MultiVariant("body", "a", "b", "c"));
        var next = CookBookEdits.RemoveVariant(book, "aurora", "body", "b");

        var trimmed = next.Recipes.Single().Ingredients.Single();
        Assert.False(trimmed.Depths.ContainsKey("b"));
        Assert.True(trimmed.Depths.ContainsKey("a"));
        Assert.True(trimmed.Depths.ContainsKey("c"));
        Assert.Equal(7, trimmed.Depths["a"].Front(0, 0));
        Assert.Equal(8, trimmed.Depths["a"].Back(0, 0));
    }
}
