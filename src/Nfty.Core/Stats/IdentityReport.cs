using System.Text;
using Nfty.Core.Formats;

namespace Nfty.Core.Stats;

/// <summary>
/// The <c>inspect</c> report: the tree of a CookBook with each Recipe's, Ingredient's and Variant's
/// <b>id</b> shown alongside its name.
///
/// Ids are the thing this exists for. The CLI's own help says it: "Those ids — not the display names
/// — are what --recipe and --variant expect elsewhere on this command line, so inspect is how you
/// find them." The GUI shows names everywhere and never the ids, so an author working across both
/// had to drop to a terminal to find out what to type.
/// </summary>
public static class IdentityReport
{
    public static string Render(LoadedCookBook book)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CookBook [{book.Manifest.Id}] {book.Manifest.Name}");

        foreach (var recipe in book.Recipes)
        {
            var weight = book.Manifest.RecipeWeights.GetValueOrDefault(recipe.Manifest.Id);
            sb.AppendLine($"  Recipe [{recipe.Manifest.Id}] {recipe.Manifest.Name} (weight {weight:0.##})");

            // Layer ORDER, not the ingredient collection's order: that is the order the layers
            // actually composite in, and an author reading this is usually asking about stacking.
            foreach (var id in recipe.Manifest.LayerOrder)
            {
                var ing = recipe.Ingredients.FirstOrDefault(i => i.Manifest.Id == id);
                if (ing is null)
                {
                    // A layer named in the order but absent from the archive. Validator flags this;
                    // showing it here rather than skipping it is the point of an inspect report.
                    sb.AppendLine($"    Ingredient [{id}] — MISSING from this recipe");
                    continue;
                }

                sb.AppendLine($"    Ingredient [{ing.Manifest.Id}] {ing.Manifest.Name} "
                    + $"({ing.Manifest.Kind.ToString().ToLowerInvariant()})");
                foreach (var v in ing.Manifest.Variants)
                    sb.AppendLine($"      Variant [{v.Id}] {v.Name} (weight {v.Weight:0.##})");
            }
        }

        return sb.ToString();
    }
}
