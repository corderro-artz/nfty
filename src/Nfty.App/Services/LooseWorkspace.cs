using System;
using System.Collections.Generic;
using System.Linq;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.Services;

/// <summary>Wraps a standalone (loose) ingredient in a throwaway single-recipe cookbook so the
/// Ingredient Editor — which needs a canvas + recipe context — can open it. The wrapper is a view/edit
/// scaffold only; it is never persisted as a cookbook (loose Save writes the .igt directly). The
/// returned book owns the ingredient, so disposing the book disposes the ingredient's images.</summary>
public static class LooseWorkspace
{
    public static LoadedCookBook WrapIngredient(LoadedIngredient ing)
    {
        var img = ing.VariantImages.Values.FirstOrDefault()
            ?? throw new InvalidOperationException("A loose ingredient needs at least one variant to edit.");
        var canvas = new Dimensions(img.Width, img.Height);
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("loose", ing.Manifest.Name,
                new[] { ing.Manifest.Id }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("loose", ing.Manifest.Name, canvas,
                new Collection(ing.Manifest.Name, "", "L"),
                new Dictionary<string, double> { ["loose"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    /// <summary>Wraps a standalone (loose) recipe in a throwaway single-recipe cookbook so the Explorer
    /// can browse it. The recipe is kept as-is (its own id, layer order, rules, ingredients);
    /// <c>RecipeWeights</c> keys the recipe's real id so the synthetic book is internally consistent. The
    /// returned book owns the recipe → its ingredients → their images.</summary>
    public static LoadedCookBook WrapRecipe(LoadedRecipe recipe)
    {
        var img = recipe.Ingredients.SelectMany(i => i.VariantImages.Values).FirstOrDefault();
        var canvas = img is null ? new Dimensions(512, 512) : new Dimensions(img.Width, img.Height);
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("loose", recipe.Manifest.Name, canvas,
                new Collection(recipe.Manifest.Name, "", "L"),
                new Dictionary<string, double> { [recipe.Manifest.Id] = 100 }),
            Recipes = new[] { recipe },
        };
    }
}
