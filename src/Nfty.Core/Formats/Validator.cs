using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

public static class Validator
{
    public static IReadOnlyList<string> Validate(LoadedCookBook cb)
    {
        var problems = new List<string>();
        var canvas = cb.Manifest.Canvas;
        var recipeIds = cb.Recipes.Select(r => r.Manifest.Id).ToHashSet();

        // Id uniqueness comes first: every ToDictionary below would throw on a duplicate, and
        // a validator that crashes cannot report anything.
        foreach (var dup in Duplicates(cb.Recipes.Select(r => r.Manifest.Id)))
            problems.Add($"CookBook has duplicate recipe id '{dup}'.");

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
            foreach (var dup in Duplicates(r.Ingredients.Select(i => i.Manifest.Id)))
                problems.Add($"Recipe '{r.Manifest.Id}' has duplicate ingredient id '{dup}'.");

            // Built duplicate-tolerantly (last wins) so a duplicate is reported above rather
            // than thrown here; the rest of the checks still run and report what they find.
            var ingById = new Dictionary<string, LoadedIngredient>();
            foreach (var i in r.Ingredients) ingById[i.Manifest.Id] = i;

            // No layers means nothing to composite: every asset would be fully transparent.
            if (r.Manifest.LayerOrder.Count == 0)
                problems.Add($"Recipe '{r.Manifest.Id}' has an empty layerOrder, so it would "
                    + "generate a fully-transparent asset.");

            foreach (var layerId in r.Manifest.LayerOrder)
                if (!ingById.ContainsKey(layerId))
                    problems.Add($"Recipe '{r.Manifest.Id}' layerOrder references unknown ingredient '{layerId}'.");

            // The mirror of the check above: generation only ever rolls layerOrder, so an
            // ingredient the archive carries but layerOrder omits is never composited.
            var ordered = r.Manifest.LayerOrder.ToHashSet(StringComparer.Ordinal);
            foreach (var ing in r.Ingredients)
                if (!ordered.Contains(ing.Manifest.Id))
                    problems.Add($"Recipe '{r.Manifest.Id}' has ingredient '{ing.Manifest.Id}' "
                        + "that is missing from its layerOrder, so it is never rolled.");

            foreach (var ing in r.Ingredients)
            {
                if (ing.Manifest.Variants.Count == 0)
                    problems.Add($"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}' has no variants.");
                if (ing.Manifest.Variants.Sum(v => v.Weight) <= 0)
                    problems.Add($"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}' has zero total variant weight.");
                foreach (var dup in Duplicates(ing.Manifest.Variants.Select(v => v.Id)))
                    problems.Add($"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}' has duplicate variant id '{dup}'.");
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
                        {
                            problems.Add($"{where} is dynamic but has no colorization.");
                        }
                        else
                        {
                            // Spec 4.1: dynamic needs >=1 entries. Spec 5.1: non-zero total weight.
                            // Without both, the roll has nothing to pick from.
                            if (col.Entries.Count == 0)
                                problems.Add($"{where} is dynamic but has no colorization entries; dynamic requires at least one.");
                            else if (col.Entries.Sum(e => e.Weight) <= 0)
                                problems.Add($"{where} is dynamic but its colorization entries have zero total weight.");

                            foreach (var entry in col.Entries)
                                if ((entry.Fixed is null) == (entry.Range is null))
                                    problems.Add($"{where} has a colorization entry that must have exactly one of fixed or range.");
                        }
                        break;
                }

                if (col is not null)
                    foreach (var entry in col.Entries)
                    {
                        // Ranges are sampled ascending and never wrap, so an inverted or
                        // out-of-axis range is author error — the roller would silently
                        // sample nonsense.
                        if (entry.Range is { } range)
                            CheckRange(problems, where, range);

                        // Spec 9: a colour spec must carry an explicit prefix and is never
                        // guessed. Parsing here is what makes `validate` catch it instead of
                        // Generate throwing FormatException later.
                        if (entry.Fixed is { } spec)
                        {
                            try { ColorSpec.Parse(spec); }
                            catch (FormatException ex)
                            {
                                problems.Add($"{where} has an invalid fixed color '{spec}': {ex.Message}");
                            }
                        }
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

    /// <summary>Each id that appears more than once, in first-seen order.</summary>
    private static IEnumerable<string> Duplicates(IEnumerable<string> ids) =>
        ids.GroupBy(id => id, StringComparer.Ordinal)
           .Where(g => g.Count() > 1)
           .Select(g => g.Key);

    /// <summary>
    /// A colorization range must run ascending and stay on its axis. Hue is 0..360 degrees and
    /// saturation 0..100 percent, both inclusive; wrap-around is not a feature (the roller
    /// samples <c>Min + r*(Max-Min)</c>, so an inverted range walks backwards off the range).
    /// </summary>
    private static void CheckRange(List<string> problems, string where, ColorRange range)
    {
        if (range.HueMin > range.HueMax)
            problems.Add($"{where} has a colorization range whose hueMin ({range.HueMin}) is "
                + $"greater than its hueMax ({range.HueMax}); ranges do not wrap around.");
        if (range.SatMin > range.SatMax)
            problems.Add($"{where} has a colorization range whose satMin ({range.SatMin}) is "
                + $"greater than its satMax ({range.SatMax}); ranges do not wrap around.");

        if (range.HueMin < 0 || range.HueMin > 360 || range.HueMax < 0 || range.HueMax > 360)
            problems.Add($"{where} has a colorization hue range {range.HueMin}..{range.HueMax} "
                + "outside the hue axis 0..360.");
        if (range.SatMin < 0 || range.SatMin > 100 || range.SatMax < 0 || range.SatMax > 100)
            problems.Add($"{where} has a colorization sat range {range.SatMin}..{range.SatMax} "
                + "outside the saturation axis 0..100.");
    }
}
