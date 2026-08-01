using Nfty.Core.Formats;

namespace Nfty.Core.Editing;

/// <summary>
/// Splices an edited ingredient back into a loaded cookbook. Returns a new graph that reuses the
/// existing image objects plus the new ingredient's — it disposes nothing, so the caller manages the
/// lifetime of whatever it replaces.
/// </summary>
public static class CookBookEdits
{
    public static LoadedCookBook UpsertIngredient(LoadedCookBook book, string recipeId, LoadedIngredient ingredient)
    {
        if (!book.Recipes.Any(r => r.Manifest.Id == recipeId))
            throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");

        var recipes = book.Recipes.Select(r =>
        {
            if (r.Manifest.Id != recipeId) return r;

            var ings = r.Ingredients
                .Where(i => i.Manifest.Id != ingredient.Manifest.Id)
                .Append(ingredient)
                .ToList();

            var order = r.Manifest.LayerOrder.Contains(ingredient.Manifest.Id)
                ? r.Manifest.LayerOrder
                : r.Manifest.LayerOrder.Append(ingredient.Manifest.Id).ToList();

            return new LoadedRecipe { Manifest = r.Manifest with { LayerOrder = order }, Ingredients = ings };
        }).ToList();

        return new LoadedCookBook
        {
            Manifest = book.Manifest,
            Recipes = recipes,
            SourceSha256 = book.SourceSha256
        };
    }

    /// <summary>Removes an ingredient from a recipe (dropping it from the layer order too). Reuses every
    /// surviving image; the caller owns the removed ingredient's images.</summary>
    public static LoadedCookBook RemoveIngredient(LoadedCookBook book, string recipeId, string ingredientId)
    {
        var recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == recipeId)
            ?? throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");
        if (recipe.Ingredients.All(i => i.Manifest.Id != ingredientId))
            throw new KeyNotFoundException($"No ingredient '{ingredientId}' in recipe '{recipeId}'.");

        var recipes = book.Recipes.Select(r =>
        {
            if (r.Manifest.Id != recipeId) return r;
            var ings = r.Ingredients.Where(i => i.Manifest.Id != ingredientId).ToList();
            var order = r.Manifest.LayerOrder.Where(id => id != ingredientId).ToList();
            return new LoadedRecipe { Manifest = r.Manifest with { LayerOrder = order }, Ingredients = ings };
        }).ToList();

        return new LoadedCookBook { Manifest = book.Manifest, Recipes = recipes, SourceSha256 = book.SourceSha256 };
    }

    /// <summary>Adds a recipe to a cookbook (or replaces one with the same id) and sets its selection
    /// weight. Reuses every other recipe/image by reference; disposes nothing.</summary>
    public static LoadedCookBook UpsertRecipe(LoadedCookBook book, LoadedRecipe recipe, double weight)
    {
        var recipes = book.Recipes.Where(r => r.Manifest.Id != recipe.Manifest.Id).Append(recipe).ToList();
        var weights = book.Manifest.RecipeWeights
            .Where(kv => kv.Key != recipe.Manifest.Id)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        weights[recipe.Manifest.Id] = weight;
        return new LoadedCookBook
        {
            Manifest = book.Manifest with { RecipeWeights = weights },
            Recipes = recipes,
            SourceSha256 = book.SourceSha256,
        };
    }

    /// <summary>Removes a recipe from a cookbook (and its selection-weight entry). Reuses every surviving
    /// image; the caller owns the removed recipe's ingredient images.</summary>
    public static LoadedCookBook RemoveRecipe(LoadedCookBook book, string recipeId)
    {
        if (book.Recipes.All(r => r.Manifest.Id != recipeId))
            throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");

        var recipes = book.Recipes.Where(r => r.Manifest.Id != recipeId).ToList();
        var weights = book.Manifest.RecipeWeights.Where(kv => kv.Key != recipeId)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return new LoadedCookBook
        {
            Manifest = book.Manifest with { RecipeWeights = weights },
            Recipes = recipes,
            SourceSha256 = book.SourceSha256,
        };
    }
}
