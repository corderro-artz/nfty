using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>Validates then atomically writes a loose ingredient to its <c>.igt</c>: a sibling temp
    /// plus <see cref="File.Move(string,string,bool)"/>, so an existing file is replaced rather than
    /// rejected (IngredientArchive.Write alone opens CreateNew and throws on an existing path). Returns
    /// the validation problems and writes nothing when there are any; throws on an IO failure. Shared by
    /// every create-loose entry point so they all apply the same standard.</summary>
    public static IReadOnlyList<string> WriteIngredient(string path, LoadedIngredient ing)
    {
        var problems = Validator.ValidateIngredient(ing);
        if (problems.Count > 0) return problems;

        var tmp = path + ".tmp";
        try
        {
            IngredientArchive.Write(tmp, ing.Manifest, ing.VariantImages);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
        }
        return Array.Empty<string>();
    }

    /// <summary>Writes a standalone (loose) recipe as a .rcp, atomically, mirroring
    /// <see cref="WriteIngredient"/>. Returns validation problems and writes nothing when there are any.
    ///
    /// A recipe created by the wizard is intentionally EMPTY — the user fills it with "Add ingredient"
    /// next — and <c>Validator.ValidateRecipe</c> rejects an empty layer order, so an empty one is
    /// allowed through here for the same reason the in-book path does not validate a fresh recipe.
    /// Anything with layers is held to the full standard.</summary>
    public static IReadOnlyList<string> WriteRecipe(string path, LoadedRecipe recipe)
    {
        var problems = recipe.Manifest.LayerOrder.Count == 0
            ? Array.Empty<string>()
            : Validator.ValidateRecipe(recipe);
        if (problems.Count > 0) return problems;

        var tmp = path + ".tmp";
        try
        {
            RecipeArchive.Write(tmp, recipe.Manifest, recipe.Ingredients);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
        }
        return Array.Empty<string>();
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
