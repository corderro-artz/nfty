using Nfty.Core.Formats;

namespace Nfty.Core.Output;

/// <summary>Recovers which variant of which layer an asset in a Set was made from.</summary>
public static class SetItemSelection
{
    /// <summary>Maps a Set item back onto its recipe's layers.</summary>
    /// <param name="recipe">The recipe the Set's book holds for this item.</param>
    /// <param name="item">The asset, as its metadata records it.</param>
    /// <returns>The present layers' ingredient id and variant id, bottom first, or <c>null</c> when
    /// the item is from another recipe or a layer cannot be told apart by name.</returns>
    /// <remarks>
    /// Names, not ids, because names are all a Set records — the same name-based reading
    /// <c>SelectionOdds.Of(SetItem)</c> makes. A layer whose trait matches no variant name, or more than
    /// one, cannot be resolved honestly, so the whole answer is <c>null</c>. A layer named in
    /// <see cref="SetItem.AbsentLayers"/> is skipped.
    /// </remarks>
    public static IReadOnlyList<(string IngredientId, string VariantId)>? Resolve(LoadedRecipe recipe, SetItem item)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(item);
        if (!string.Equals(recipe.Manifest.Id, item.Recipe, StringComparison.Ordinal)) return null;

        var absent = new HashSet<string>(item.AbsentLayers ?? Array.Empty<string>(), StringComparer.Ordinal);
        var traits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in item.Rarity)
        {
            if (string.Equals(r.Trait_type, SetWriter.TypeTrait, StringComparison.Ordinal)) continue;
            if (absent.Contains(r.Trait_type)) continue;
            traits[r.Trait_type] = r.Value;
        }

        var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
        foreach (var ing in recipe.Ingredients) byId[ing.Manifest.Id] = ing;

        var result = new List<(string, string)>();
        foreach (var ingId in recipe.Manifest.LayerOrder)
        {
            if (!byId.TryGetValue(ingId, out var ing)) return null;
            if (absent.Contains(ing.Manifest.Name)) continue;
            if (!traits.TryGetValue(ing.Manifest.Name, out var variantName)) return null;
            var matches = ing.Manifest.Variants
                .Where(v => string.Equals(v.Name, variantName, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1) return null;
            result.Add((ingId, matches[0].Id));
        }
        return result;
    }
}
