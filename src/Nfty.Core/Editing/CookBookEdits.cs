using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Core.Editing;

/// <summary>
/// Splices an edited ingredient back into a loaded cookbook. Returns a new graph that reuses the
/// existing image objects plus the new ingredient's — it disposes nothing, so the caller manages the
/// lifetime of whatever it replaces.
/// </summary>
public static class CookBookEdits
{
    /// <summary>Returns a book with one ingredient replaced or added.</summary>
    /// <param name="book">The book to edit.</param>
    /// <param name="recipeId">Which recipe owns the layer.</param>
    /// <param name="ingredient">The replacement.</param>
    /// <returns>A NEW graph SHARING the previous book's images, which is why the session
    /// swaps it in with <c>Replace</c> rather than <c>Open</c> — disposing the old book would
    /// dispose images the new one still points at.</returns>
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

    /// <summary>
    /// Moves one of a recipe's layers to a depth, shifting the layers it passes. Depth is the 1-based
    /// position in <c>layerOrder</c>, bottom-first — see <see cref="LayerDepth"/>, which does the
    /// reordering and whose clamping rules apply here unchanged.
    /// </summary>
    /// <param name="book">The book to edit.</param>
    /// <param name="recipeId">Which recipe owns the layer.</param>
    /// <param name="ingredientId">The layer to move.</param>
    /// <param name="toDepth">The 1-based depth to move it to; clamped to the stack rather than rejected.</param>
    /// <returns>A NEW graph SHARING every image with the previous book — only one recipe's manifest
    /// differs, and only in the order of its layer ids. Nothing is disposed.</returns>
    /// <exception cref="KeyNotFoundException">No such recipe, or no such ingredient in it.</exception>
    public static LoadedCookBook MoveLayer(
        LoadedCookBook book, string recipeId, string ingredientId, int toDepth)
    {
        var recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == recipeId)
            ?? throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");
        if (recipe.Ingredients.All(i => i.Manifest.Id != ingredientId))
            throw new KeyNotFoundException($"No ingredient '{ingredientId}' in recipe '{recipeId}'.");

        var recipes = book.Recipes.Select(r => r.Manifest.Id != recipeId ? r : new LoadedRecipe
        {
            Manifest = LayerDepth.MoveTo(r.Manifest, ingredientId, toDepth),
            // Reused wholesale: the ingredient collection has no order of its own that matters —
            // layerOrder is what generation walks — so a move rewrites nothing but that one list.
            Ingredients = r.Ingredients,
        }).ToList();

        return new LoadedCookBook
        {
            Manifest = book.Manifest,
            Recipes = recipes,
            SourceSha256 = book.SourceSha256,
        };
    }

    /// <summary>
    /// Applies an edit to one recipe's MANIFEST, leaving its ingredients and every image alone.
    /// </summary>
    /// <param name="book">The book to edit.</param>
    /// <param name="recipeId">Which recipe.</param>
    /// <param name="edit">The manifest edit — one of the <see cref="RuleEdits"/> methods, or
    /// anything else pure. Taking the function rather than the finished manifest is what keeps the
    /// caller from having to hand-splice a recipe back into a book, which is the step every one of
    /// these methods exists to stop being written twice.</param>
    /// <returns>A NEW graph SHARING every image the old one held, like the rest of this class — so a
    /// caller swaps it in rather than disposing the book it replaces.</returns>
    /// <exception cref="KeyNotFoundException">No such recipe.</exception>
    public static LoadedCookBook EditRecipeManifest(
        LoadedCookBook book, string recipeId, Func<RecipeManifest, RecipeManifest> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!book.Recipes.Any(r => r.Manifest.Id == recipeId))
            throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");

        var recipes = book.Recipes.Select(r => r.Manifest.Id != recipeId ? r : new LoadedRecipe
        {
            Manifest = edit(r.Manifest),
            // Reused wholesale: a manifest edit touches no artwork, and rebuilding the collection
            // would hand the new graph images the old one still believes it owns.
            Ingredients = r.Ingredients,
        }).ToList();

        return new LoadedCookBook
        {
            Manifest = book.Manifest,
            Recipes = recipes,
            SourceSha256 = book.SourceSha256,
        };
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

    /// <summary>
    /// Removes one variant from an ingredient.
    /// </summary>
    /// <remarks>
    /// <para>Reuses every surviving image; the caller owns the removed one, exactly as
    /// <see cref="RemoveIngredient"/> hands back the subtree it dropped.</para>
    /// <para>It REFUSES to empty an ingredient. A layer with no variants has nothing to roll, and
    /// <c>Validator</c> reports it as a problem - so a book saved that way is broken from then on,
    /// reported on a screen nowhere near the delete. A front-end is expected to gray the control as
    /// well; this is the backstop that makes the rule true of the operation rather than true of
    /// whichever caller remembered it.</para>
    /// </remarks>
    /// <param name="book">The book to edit.</param>
    /// <param name="recipeId">The recipe holding the layer.</param>
    /// <param name="ingredientId">The layer holding the variant.</param>
    /// <param name="variantId">The variant to remove.</param>
    /// <returns>A book with that variant gone.</returns>
    /// <exception cref="KeyNotFoundException">No such recipe, ingredient or variant.</exception>
    /// <exception cref="InvalidOperationException">It is the ingredient's only variant.</exception>
    public static LoadedCookBook RemoveVariant(LoadedCookBook book, string recipeId,
        string ingredientId, string variantId)
    {
        var recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == recipeId)
            ?? throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");
        var ingredient = recipe.Ingredients.FirstOrDefault(i => i.Manifest.Id == ingredientId)
            ?? throw new KeyNotFoundException($"No ingredient '{ingredientId}' in recipe '{recipeId}'.");
        if (ingredient.Manifest.Variants.All(v => v.Id != variantId))
            throw new KeyNotFoundException($"No variant '{variantId}' in ingredient '{ingredientId}'.");
        if (ingredient.Manifest.Variants.Count <= 1)
            throw new InvalidOperationException(
                $"'{ingredient.Manifest.Name}' has one variant left. A layer with none has nothing "
                + "to roll; delete the layer instead.");

        var variants = ingredient.Manifest.Variants.Where(v => v.Id != variantId).ToList();
        var images = ingredient.VariantImages
            .Where(kv => kv.Key != variantId)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var trimmed = new LoadedIngredient
        {
            Manifest = ingredient.Manifest with { Variants = variants },
            VariantImages = images,
        };

        var recipes = book.Recipes.Select(r =>
        {
            if (r.Manifest.Id != recipeId) return r;
            var ings = r.Ingredients.Select(i => i.Manifest.Id == ingredientId ? trimmed : i).ToList();
            return new LoadedRecipe { Manifest = r.Manifest, Ingredients = ings };
        }).ToList();

        return new LoadedCookBook { Manifest = book.Manifest, Recipes = recipes, SourceSha256 = book.SourceSha256 };
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
