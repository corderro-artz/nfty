using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Core.Generation;

/// <summary>
/// How many distinct DNA a cookbook can produce. <see cref="IsExact"/> is false when the
/// space was too large to count and <see cref="Total"/> saturated at the cap — the real
/// figure is "more than Total", never less.
/// </summary>
public record UniqueSpaceCount(long Total, bool IsExact, IReadOnlyDictionary<string, long> PerRecipe, long Cap)
{
    /// <summary>A per-recipe count that reached the cap is a floor, not the real figure.</summary>
    public bool IsRecipeExact(string recipeId) => PerRecipe.GetValueOrDefault(recipeId) < Cap;
}

/// <summary>
/// Counts the unique DNA space of a cookbook: per recipe, the legal variant combinations
/// (rules honoured) multiplied by each dynamic layer's quantized colour buckets. Static and
/// custom layers contribute a single bucket — a static layer's colour is constant, so it
/// adds no cross-asset uniqueness.
/// </summary>
public static class UniqueSpace
{
    public const long DefaultCap = 1_000_000;

    public static UniqueSpaceCount Count(LoadedCookBook book, long cap = DefaultCap)
    {
        long total = 0;
        bool exact = true;
        var perRecipe = new Dictionary<string, long>();

        foreach (var recipe in book.Recipes)
        {
            var (combos, combosExact) = LegalCombinations(recipe, cap);
            var (buckets, bucketsExact) = ColourBuckets(recipe, cap);

            long recipeTotal = Saturate(Multiply(combos, buckets, cap), cap);
            bool recipeExact = combosExact && bucketsExact && recipeTotal < cap;

            perRecipe[recipe.Manifest.Id] = recipeTotal;
            total = Saturate(total + recipeTotal, cap);
            exact &= recipeExact;
        }

        if (total >= cap) { total = cap; exact = false; }
        return new UniqueSpaceCount(total, exact, perRecipe, cap);
    }

    /// <summary>Variant combinations that satisfy the recipe's rules.</summary>
    private static (long Count, bool Exact) LegalCombinations(LoadedRecipe recipe, long cap)
    {
        var ingById = recipe.Ingredients.ToDictionary(i => i.Manifest.Id);
        var layers = recipe.Manifest.LayerOrder.Select(id => ingById[id]).ToList();

        long product = 1;
        foreach (var layer in layers)
            product = Multiply(product, layer.Manifest.Variants.Count, cap);

        // No rules: every combination is legal, so the product is the answer.
        if (recipe.Manifest.Rules.Count == 0)
            return product >= cap ? (cap, false) : (product, true);

        // Rules can only remove combinations, but knowing how many requires enumeration.
        // Only enumerate when the unconstrained product is small enough to walk.
        if (product >= cap)
            return (cap, false);

        long legal = 0;
        var selection = new Dictionary<string, string>();

        void Walk(int depth)
        {
            if (depth == layers.Count)
            {
                if (RulesEngine.IsLegal(selection, recipe.Manifest.Rules)) legal++;
                return;
            }
            var layer = layers[depth];
            foreach (var v in layer.Manifest.Variants)
            {
                selection[layer.Manifest.Id] = v.Id;
                Walk(depth + 1);
            }
            selection.Remove(layer.Manifest.Id);
        }

        Walk(0);
        return (legal, true);
    }

    /// <summary>The product of every dynamic layer's distinct quantized (H,S) buckets.</summary>
    private static (long Count, bool Exact) ColourBuckets(LoadedRecipe recipe, long cap)
    {
        var ingById = recipe.Ingredients.ToDictionary(i => i.Manifest.Id);
        long product = 1;
        bool exact = true;

        foreach (var layerId in recipe.Manifest.LayerOrder)
        {
            var ing = ingById[layerId];
            // Static and custom layers resolve to one constant bucket each.
            if (ing.Manifest.Kind != LayerKind.Dynamic) continue;

            var (buckets, bucketsExact) = DistinctBuckets(ing.Manifest.Colorization!, cap);
            exact &= bucketsExact;
            product = Multiply(product, buckets, cap);
            if (product >= cap) return (cap, false);
        }

        return (product, exact);
    }

    private static (long Count, bool Exact) DistinctBuckets(Colorization col, long cap)
    {
        int hueQ = Math.Max(1, col.HueQuantize);
        int satQ = Math.Max(1, col.SatQuantize);
        var seen = new HashSet<(long Hue, long Sat)>();

        foreach (var entry in col.Entries)
        {
            if (entry.Fixed is not null)
            {
                var c = ColorRoller.FromFixed(entry.Fixed, col.Model);
                seen.Add(((long)Math.Floor(c.H / hueQ), (long)Math.Floor(c.S * 100.0 / satQ)));
                continue;
            }

            // A range covers every bucket its endpoints span, inclusive — mirroring how
            // Dna.Compute floors a rolled colour into its bucket.
            var r = entry.Range!;
            long h0 = (long)Math.Floor(r.HueMin / hueQ), h1 = (long)Math.Floor(r.HueMax / hueQ);
            long s0 = (long)Math.Floor(r.SatMin / satQ), s1 = (long)Math.Floor(r.SatMax / satQ);

            for (long h = h0; h <= h1; h++)
                for (long s = s0; s <= s1; s++)
                {
                    seen.Add((h, s));
                    if (seen.Count >= cap) return (cap, false);
                }
        }

        return (seen.Count, true);
    }

    private static long Multiply(long a, long b, long cap)
    {
        if (a == 0 || b == 0) return 0;
        if (a > cap / b) return cap;
        return a * b;
    }

    private static long Saturate(long value, long cap) => value > cap ? cap : value;
}
