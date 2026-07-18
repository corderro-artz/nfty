using System.Collections.Generic;
using System.Linq;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class CookBookEditsTests
{
    private static LoadedIngredient Ing(string id) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic, null,
            new[] { new Variant(id + "-v", "V", 1.0) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            [id + "-v"] = new Image<Rgba32>(1, 1)
        }
    };

    private static LoadedCookBook OneRecipeBook()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("aurora", "Aurora", new List<string> { "body" },
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient> { Ing("body") }
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("vp", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["aurora"] = 1.0 }),
            Recipes = new List<LoadedRecipe> { recipe }
        };
    }

    [Fact]
    public void Adding_a_new_ingredient_appends_it_and_updates_layer_order()
    {
        var book = CookBookEdits.UpsertIngredient(OneRecipeBook(), "aurora", Ing("ears"));
        var recipe = book.Recipes.Single();
        Assert.Equal(2, recipe.Ingredients.Count);
        Assert.Equal(new[] { "body", "ears" }, recipe.Manifest.LayerOrder);
    }

    [Fact]
    public void Replacing_an_existing_ingredient_keeps_layer_order()
    {
        var book = CookBookEdits.UpsertIngredient(OneRecipeBook(), "aurora", Ing("body"));
        var recipe = book.Recipes.Single();
        Assert.Single(recipe.Ingredients);
        Assert.Equal(new[] { "body" }, recipe.Manifest.LayerOrder);
    }

    [Fact]
    public void Unknown_recipe_id_throws()
    {
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
            () => CookBookEdits.UpsertIngredient(OneRecipeBook(), "nope", Ing("ears")));
    }
}
