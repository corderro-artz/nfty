using Nfty.Core.Formats;

namespace Nfty.Core.Stats;

/// <summary>One recipe's share of the collection, from the cookbook's weights.</summary>
/// <param name="RecipeId">The recipe's id.</param>
/// <param name="RecipeName">Its display name.</param>
/// <param name="Percent">Its share of mints.</param>
public record RecipeOdds(string RecipeId, string RecipeName, double Percent);

/// <summary>One variant's odds, both within its recipe and across the whole collection.</summary>
/// <param name="RecipeId">The owning recipe's id.</param>
/// <param name="RecipeName">The owning recipe's name.</param>
/// <param name="IngredientId">The layer's id.</param>
/// <param name="IngredientName">The layer's name.</param>
/// <param name="VariantId">The variant's id.</param>
/// <param name="VariantName">The variant's name.</param>
/// <param name="WithinRecipePercent">Share among that recipe's mints.</param>
/// <param name="OverallPercent">Share of the whole collection — the recipe's own share folded in.</param>
public record TraitOdds(
    string RecipeId, string RecipeName,
    string IngredientId, string IngredientName,
    string VariantId, string VariantName,
    double WithinRecipePercent, double OverallPercent);

/// <summary>The odds a cookbook's weights imply, before anything is generated.</summary>
/// <param name="Recipes">Per-recipe shares.</param>
/// <param name="Traits">Per-variant odds.</param>
public record RarityReport(IReadOnlyList<RecipeOdds> Recipes, IReadOnlyList<TraitOdds> Traits);

/// <summary>Computes the odds a cookbook's weights imply. Distinct from the rarity written into a
/// Set, which counts what was actually minted rather than what was intended.</summary>
public static class RarityCalculator
{
    /// <summary>Computes the odds.</summary>
    /// <param name="book">The book to analyze.</param>
    /// <returns>Per-recipe and per-variant odds.</returns>
    public static RarityReport Compute(LoadedCookBook book)
    {
        double recipeTotal = book.Manifest.RecipeWeights.Values.Sum();
        var recipes = new List<RecipeOdds>();
        var traits = new List<TraitOdds>();

        foreach (var r in book.Recipes)
        {
            double recipeWeight = book.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id);
            double recipePct = recipeTotal > 0 ? recipeWeight / recipeTotal * 100 : 0;
            recipes.Add(new RecipeOdds(r.Manifest.Id, r.Manifest.Name, Math.Round(recipePct, 2)));

            // Driven by LayerOrder, exactly as Generator rolls: an ingredient present in the
            // archive but absent from layerOrder is never rolled, so reporting odds for it
            // would invent traits that cannot occur. (Validator reports such an orphan.)
            var ingById = new Dictionary<string, LoadedIngredient>();
            foreach (var i in r.Ingredients) ingById[i.Manifest.Id] = i;

            foreach (var layerId in r.Manifest.LayerOrder)
            {
                if (!ingById.TryGetValue(layerId, out var ing)) continue;
                double layerTotal = ing.Manifest.Variants.Sum(v => v.Weight);

                // A LAYER THAT CAN BE LEFT OUT SCALES EVERY VARIANT UNDER IT. Without this the
                // variants of a 90%-absent layer would each report the share they hold AMONG
                // THEMSELVES — a chase item printing "50% in recipe" when it lands on one asset in
                // twenty. The odds shown are the odds of getting it, so absence has to be folded in
                // here rather than mentioned somewhere else.
                double absentPct = r.Manifest.AbsentPercentOf(layerId);
                double presentShare = Math.Clamp(100 - absentPct, 0, 100) / 100;

                foreach (var v in ing.Manifest.Variants)
                {
                    double within = (layerTotal > 0 ? v.Weight / layerTotal * 100 : 0) * presentShare;
                    double overall = recipePct / 100 * within;
                    traits.Add(new TraitOdds(r.Manifest.Id, r.Manifest.Name,
                        ing.Manifest.Id, ing.Manifest.Name, v.Id, v.Name,
                        Math.Round(within, 2), Math.Round(overall, 2)));
                }
            }
        }

        return new RarityReport(recipes, traits);
    }
}
