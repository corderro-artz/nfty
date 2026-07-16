using Nfty.Core.Formats;

namespace Nfty.Core.Stats;

public record RecipeOdds(string RecipeId, string RecipeName, double Percent);

public record TraitOdds(
    string RecipeId, string RecipeName,
    string IngredientId, string IngredientName,
    string VariantId, string VariantName,
    double WithinRecipePercent, double OverallPercent);

public record RarityReport(IReadOnlyList<RecipeOdds> Recipes, IReadOnlyList<TraitOdds> Traits);

public static class RarityCalculator
{
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
                foreach (var v in ing.Manifest.Variants)
                {
                    double within = layerTotal > 0 ? v.Weight / layerTotal * 100 : 0;
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
