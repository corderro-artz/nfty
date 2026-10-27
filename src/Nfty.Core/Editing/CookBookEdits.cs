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
        var depths = ingredient.Depths
            .Where(kv => kv.Key != variantId)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var trimmed = new LoadedIngredient
        {
            Manifest = ingredient.Manifest with { Variants = variants },
            VariantImages = images,
            Depths = depths,
        };

        var recipes = book.Recipes.Select(r =>
        {
            if (r.Manifest.Id != recipeId) return r;
            var ings = r.Ingredients.Select(i => i.Manifest.Id == ingredientId ? trimmed : i).ToList();
            return new LoadedRecipe { Manifest = r.Manifest, Ingredients = ings };
        }).ToList();

        return new LoadedCookBook { Manifest = book.Manifest, Recipes = recipes, SourceSha256 = book.SourceSha256 };
    }

    /// <summary>
    /// Moves one recipe to a position in the book's listing, shifting the recipes it passes.
    /// </summary>
    /// <param name="book">The book to edit.</param>
    /// <param name="recipeId">The recipe to move.</param>
    /// <param name="toIndex">Where to put it, 0-based; clamped to the list rather than rejected, so
    /// nudging the first or last recipe off the end is a no-op exactly as
    /// <see cref="LayerDepth.MoveTo"/> makes it one for a layer.</param>
    /// <returns>A NEW graph SHARING every image with the previous book. Only the manifest's
    /// <see cref="CookBookManifest.RecipeOrder"/> and the order of <c>Recipes</c> differ. Nothing is
    /// disposed.</returns>
    /// <exception cref="KeyNotFoundException">No such recipe.</exception>
    /// <remarks>
    /// <para>THIS CHANGES NOTHING ABOUT WHAT A COOK PRODUCES, and that is the whole difference
    /// between it and <see cref="MoveLayer"/> one level down. <c>WeightedRoller.Prepare</c> sorts
    /// its keys ordinally before building the cumulative table, so which Recipe a random number
    /// lands on is decided by the ids and their weights and never by the listing; a layer move, by
    /// contrast, moves which RNG draw reaches which layer and produces a different collection from
    /// the same seed. A reader who knows the layer rule would reasonably assume this one reorders
    /// their assets too. It does not.</para>
    ///
    /// <para>The order is written WHOLE rather than as a patch — every id in the book, in the new
    /// order — so the field never describes a partial listing that the reader has to merge with a
    /// fallback to interpret.</para>
    /// </remarks>
    public static LoadedCookBook MoveRecipe(LoadedCookBook book, string recipeId, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(book);
        int from = -1;
        for (int i = 0; i < book.Recipes.Count; i++)
            if (string.Equals(book.Recipes[i].Manifest.Id, recipeId, StringComparison.Ordinal)) from = i;
        if (from < 0)
            throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");

        int to = Math.Clamp(toIndex, 0, book.Recipes.Count - 1);
        var recipes = book.Recipes.ToList();
        var moved = recipes[from];
        recipes.RemoveAt(from);
        recipes.Insert(to, moved);

        return new LoadedCookBook
        {
            Manifest = book.Manifest with
            {
                RecipeOrder = recipes.Select(r => r.Manifest.Id).ToArray(),
            },
            Recipes = recipes,
            SourceSha256 = book.SourceSha256,
        };
    }

    /// <summary>
    /// Edits the book's identity: its own name, the marketplace-facing collection record, and the
    /// intended supply. Nothing else in the manifest moves.
    /// </summary>
    /// <param name="book">The book to edit.</param>
    /// <param name="name">The book's display name (what the titlebar, tree and recents show).</param>
    /// <param name="collection">What <c>SetWriter</c> publishes: the collection name that prefixes
    /// every asset, plus description and symbol.</param>
    /// <param name="targetSupply">How many assets the author intends to mint, or null for no commitment.</param>
    /// <returns>A NEW graph SHARING every image and recipe with the previous book. Nothing is
    /// disposed. Nothing is validated either: <c>Validator</c> is the one place that decides what a
    /// legal book is, and the caller runs it before writing.</returns>
    /// <remarks>None of these fields is read by a roll, so this can never change what a seed
    /// produces.</remarks>
    public static LoadedCookBook EditIdentity(
        LoadedCookBook book, string name, Collection collection, int? targetSupply)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(collection);
        return new LoadedCookBook
        {
            Manifest = book.Manifest with { Name = name, Collection = collection, TargetSupply = targetSupply },
            Recipes = book.Recipes,
            SourceSha256 = book.SourceSha256,
        };
    }

    /// <summary>
    /// Sets one recipe's selection weight in the book. Zero shelves the recipe: it stays in the book
    /// and the roll skips it.
    /// </summary>
    /// <param name="book">The book to edit.</param>
    /// <param name="recipeId">Which recipe.</param>
    /// <param name="weight">The new weight. Not validated here; <c>Validator</c> reports a negative one.</param>
    /// <returns>A NEW graph SHARING every image. Nothing is disposed.</returns>
    /// <remarks>UNLIKE an identity edit, this DOES change which assets a seed produces: the weight is
    /// part of the roll's cumulative table.</remarks>
    /// <exception cref="KeyNotFoundException">No such recipe.</exception>
    public static LoadedCookBook SetRecipeWeight(LoadedCookBook book, string recipeId, double weight)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.Recipes.All(r => r.Manifest.Id != recipeId))
            throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{book.Manifest.Id}'.");
        var weights = book.Manifest.RecipeWeights.ToDictionary(kv => kv.Key, kv => kv.Value);
        weights[recipeId] = weight;
        return new LoadedCookBook
        {
            Manifest = book.Manifest with { RecipeWeights = weights },
            Recipes = book.Recipes,
            SourceSha256 = book.SourceSha256,
        };
    }

    /// <summary>Replaces the swatches the book carries.</summary>
    /// <param name="book">The book to edit.</param>
    /// <param name="swatches">Prefixed color specs (<c>hex:d6249f</c>), in order. An empty list stores
    /// null ("the book has never saved a palette") rather than an empty array, so a book with no
    /// swatches is written the way every book before the field was.</param>
    /// <returns>A NEW graph SHARING every image. Nothing is disposed.</returns>
    public static LoadedCookBook SetPalette(LoadedCookBook book, IReadOnlyList<string> swatches)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(swatches);
        return new LoadedCookBook
        {
            Manifest = book.Manifest with { Palette = swatches.Count == 0 ? null : swatches.ToArray() },
            Recipes = book.Recipes,
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
