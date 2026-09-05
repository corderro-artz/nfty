using Nfty.Core.Model;

namespace Nfty.Core.Editing;

/// <summary>
/// Authoring for a recipe's optional layers: how often each one is left out entirely. Pure, like
/// <see cref="RuleEdits"/> and <see cref="LayerDepth"/> beside it — every method returns a NEW
/// <see cref="RecipeManifest"/> and mutates nothing.
/// </summary>
public static class AbsentChance
{
    /// <summary>
    /// Sets how often a layer is left out.
    /// </summary>
    /// <param name="recipe">The recipe to edit.</param>
    /// <param name="ingredientId">The layer. Must be one this recipe stacks — a chance for a layer
    /// it does not is almost certainly one meant for a layer it does, and it would sit in the
    /// manifest doing nothing while the author believed their chase item was rare.</param>
    /// <param name="percent">0..100. Zero always appears; 100 never does, which shelves the layer
    /// without deleting it, the same meaning a recipe weight of zero already carries.</param>
    /// <returns>A new manifest.</returns>
    /// <exception cref="ArgumentException">The layer is not in this recipe.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The percent is not a finite 0..100.</exception>
    public static RecipeManifest Set(RecipeManifest recipe, string ingredientId, double percent)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        if (!recipe.LayerOrder.Contains(ingredientId, StringComparer.Ordinal))
            throw new ArgumentException(
                $"Recipe '{recipe.Id}' has no layer '{ingredientId}'. A chance can only be set for a "
                + "layer the recipe actually stacks.", nameof(ingredientId));

        if (!double.IsFinite(percent) || percent < 0 || percent > 100)
            throw new ArgumentOutOfRangeException(nameof(percent),
                $"An absent chance must be a finite number between 0 and 100, but it is {percent}. "
                + "0 always appears, 100 never does.");

        var next = recipe.AbsentPercent is null
            ? new Dictionary<string, double>(StringComparer.Ordinal)
            : new Dictionary<string, double>(recipe.AbsentPercent, StringComparer.Ordinal);

        // ZERO IS NOT STORED. "Always appears" is the absence of an entry, not an entry saying zero
        // — so clearing a chance leaves the manifest exactly as it was before the layer ever had
        // one, and a recipe that has been given a chance and had it taken away serializes
        // identically to one that never had it. That is also what keeps HasOptionalLayers, which
        // the GUI's derived toggle reads, from staying true after the last chance is cleared.
        if (percent == 0) next.Remove(ingredientId);
        else next[ingredientId] = percent;

        return recipe with { AbsentPercent = next.Count == 0 ? null : next };
    }

    /// <summary>Clears every optional-layer chance, so every layer always appears.</summary>
    /// <param name="recipe">The recipe to edit.</param>
    /// <returns>A new manifest with no chances at all.</returns>
    /// <remarks>
    /// This is what the GUI's "optional layers" toggle does when it is turned OFF, and it is
    /// destructive by design: the toggle is DERIVED from the data rather than stored beside it, so
    /// "off" cannot be a flag that contradicts a chance sitting underneath it. There is nothing for
    /// the two to disagree about because there is only one of them.
    /// </remarks>
    public static RecipeManifest ClearAll(RecipeManifest recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.AbsentPercent is null ? recipe : recipe with { AbsentPercent = null };
    }
}
