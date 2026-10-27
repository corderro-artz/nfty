using System.Globalization;
using Nfty.Core.Formats;

namespace Nfty.Core.Stats;

/// <summary>
/// How far one variant's edit reaches: the share of the collection that carries it, and, when the
/// book states a target supply, the expected number of assets.
/// </summary>
/// <param name="Percent">The book-wide share (recipe share and absence folded in), 0..100.</param>
/// <param name="TargetSupply">The book's intended supply, or null.</param>
/// <param name="RecipeHasRules">Whether the variant's recipe has incompatibility rules, which can
/// shift the real share away from the weights.</param>
/// <remarks>
/// The figure is <see cref="RarityCalculator"/>'s <c>OverallPercent</c>, the number the Ingredient
/// pane already prints, so the two cannot disagree. An expected count is never exact, hence the
/// <c>~</c>.
/// </remarks>
public sealed record VariantReach(double Percent, int? TargetSupply, bool RecipeHasRules)
{
    /// <summary>Computes the reach of one variant in one recipe. An unknown id gives 0%, never a throw.</summary>
    /// <param name="book">The book.</param>
    /// <param name="recipeId">The recipe.</param>
    /// <param name="ingredientId">The layer.</param>
    /// <param name="variantId">The variant.</param>
    /// <returns>The reach.</returns>
    public static VariantReach Of(LoadedCookBook book, string recipeId, string ingredientId, string variantId)
    {
        ArgumentNullException.ThrowIfNull(book);
        var odds = RarityCalculator.Compute(book).Traits.FirstOrDefault(t =>
            t.RecipeId == recipeId && t.IngredientId == ingredientId && t.VariantId == variantId);
        var recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == recipeId);
        return new VariantReach(odds?.OverallPercent ?? 0, book.Manifest.TargetSupply,
            recipe is not null && recipe.Manifest.Rules.Count > 0);
    }

    /// <summary>The reach as a sentence fragment, invariant: <c>in ~900 of 1,200 assets</c>,
    /// <c>in all 1,200 assets</c>, <c>in 75% of assets</c> or <c>in every asset</c>.</summary>
    public string Text
    {
        get
        {
            bool all = Percent >= 100;
            if (TargetSupply is { } supply)
            {
                string total = supply.ToString("N0", CultureInfo.InvariantCulture);
                if (all) return $"in all {total} assets";
                double count = Math.Round(Percent / 100 * supply, MidpointRounding.AwayFromZero);
                return $"in ~{count.ToString("N0", CultureInfo.InvariantCulture)} of {total} assets";
            }
            return all ? "in every asset" : $"in {RarityText.Percent(Percent)} of assets";
        }
    }

    /// <summary>What the figure is, for a tooltip.</summary>
    public string Tip =>
        "The expected share from the book's weights."
        + (RecipeHasRules ? " Rules can change it." : "")
        + (TargetSupply is null ? "" : " Counted against the target supply.");
}
