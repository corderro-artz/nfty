using Nfty.Core.Model;

namespace Nfty.Core.Formats;

public static class Validator
{
    public static IReadOnlyList<string> Validate(LoadedCookBook cb)
    {
        var problems = new List<string>();
        var canvas = cb.Manifest.Canvas;
        var recipeIds = cb.Recipes.Select(r => r.Manifest.Id).ToHashSet();

        if (cb.Manifest.RecipeWeights.Values.Sum() <= 0)
            problems.Add("CookBook has zero total recipe weight.");
        foreach (var id in cb.Manifest.RecipeWeights.Keys)
            if (!recipeIds.Contains(id))
                problems.Add($"recipeWeights references unknown recipe '{id}'.");
        foreach (var r in cb.Recipes)
            if (!cb.Manifest.RecipeWeights.ContainsKey(r.Manifest.Id))
                problems.Add($"Recipe '{r.Manifest.Id}' has no recipe weight.");

        foreach (var r in cb.Recipes)
        {
            var ingById = r.Ingredients.ToDictionary(i => i.Manifest.Id);

            foreach (var layerId in r.Manifest.LayerOrder)
                if (!ingById.ContainsKey(layerId))
                    problems.Add($"Recipe '{r.Manifest.Id}' layerOrder references unknown ingredient '{layerId}'.");

            foreach (var ing in r.Ingredients)
            {
                if (ing.Manifest.Variants.Count == 0)
                    problems.Add($"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}' has no variants.");
                if (ing.Manifest.Variants.Sum(v => v.Weight) <= 0)
                    problems.Add($"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}' has zero total variant weight.");
                string where = $"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}'";
                var col = ing.Manifest.Colorization;
                switch (ing.Manifest.Kind)
                {
                    case LayerKind.Custom:
                        // Composited as-is; must NEVER carry a colorization.
                        if (col is not null)
                            problems.Add($"{where} is custom but has a colorization; custom layers must have none.");
                        break;

                    case LayerKind.Static:
                        // Colorized with exactly one fixed color, deterministically.
                        if (col is null)
                            problems.Add($"{where} is static but has no colorization; static requires exactly one fixed color.");
                        else if (col.Entries.Count != 1)
                            problems.Add($"{where} is static but has {col.Entries.Count} colorization entries; static requires exactly one fixed color.");
                        else if (col.Entries[0].Fixed is null || col.Entries[0].Range is not null)
                            problems.Add($"{where} is static but its colorization entry is not a single fixed color (no ranges allowed).");
                        break;

                    case LayerKind.Dynamic:
                        // Value-map rolled from one or more weighted entries.
                        if (col is null)
                            problems.Add($"{where} is dynamic but has no colorization.");
                        else
                            foreach (var entry in col.Entries)
                                if ((entry.Fixed is null) == (entry.Range is null))
                                    problems.Add($"{where} has a colorization entry that must have exactly one of fixed or range.");
                        break;
                }

                foreach (var v in ing.Manifest.Variants)
                {
                    if (!ing.VariantImages.TryGetValue(v.Id, out var img))
                    {
                        problems.Add($"Variant '{v.Id}' in '{ing.Manifest.Id}' has no image.");
                        continue;
                    }
                    if (img.Width != canvas.Width || img.Height != canvas.Height)
                        problems.Add(
                            $"Variant '{v.Id}' in '{ing.Manifest.Id}'/'{r.Manifest.Id}' has dimensions "
                            + $"{img.Width}x{img.Height}, expected canvas {canvas.Width}x{canvas.Height}.");
                }
            }

            foreach (var rule in r.Manifest.Rules)
                foreach (var t in rule.Targets.Append(rule.When))
                    if (!ingById.ContainsKey(t.IngredientId))
                        problems.Add($"Recipe '{r.Manifest.Id}' rule references unknown ingredient '{t.IngredientId}'.");
        }

        return problems;
    }
}
