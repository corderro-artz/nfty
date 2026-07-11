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
