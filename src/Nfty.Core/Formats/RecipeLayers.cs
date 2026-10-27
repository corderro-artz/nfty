using Nfty.Ticket;

namespace Nfty.Core.Formats;

/// <summary>Resolving a recipe's <c>layerOrder</c> to its ingredients, in one place.</summary>
public static class RecipeLayers
{
    /// <summary>The recipe's ingredients by id; on a duplicate id the last one wins.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>The ingredients by id.</returns>
    public static Dictionary<string, LoadedIngredient> ById(LoadedRecipe recipe) =>
        Lookup.ById(recipe.Ingredients, i => i.Manifest.Id);

    /// <summary>
    /// Resolves a recipe's layerOrder to its ingredients, in order. Never throws: a transiently
    /// invalid book (a duplicate ingredient id, or a layerOrder entry naming a removed ingredient)
    /// is a normal state for live callers, not a crash. Ids resolve duplicate-tolerantly (last one
    /// wins, as <see cref="ById"/>), and an entry with no ingredient fails the whole recipe back as
    /// "unresolved". Deciding what makes a book legal is the validator's job.
    /// </summary>
    /// <param name="recipe">The recipe.</param>
    /// <param name="layers">The ingredients in layer order when this returns true; otherwise empty.</param>
    /// <returns>Whether every layerOrder entry named an ingredient.</returns>
    public static bool TryResolve(LoadedRecipe recipe, out List<LoadedIngredient> layers)
    {
        var ingById = ById(recipe);
        var resolved = new List<LoadedIngredient>(recipe.Manifest.LayerOrder.Count);
        foreach (var id in recipe.Manifest.LayerOrder)
        {
            if (!ingById.TryGetValue(id, out var ing))
            {
                layers = new List<LoadedIngredient>();
                return false;
            }
            resolved.Add(ing);
        }

        layers = resolved;
        return true;
    }
}
