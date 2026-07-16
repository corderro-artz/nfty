using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Core.Generation;

/// <summary>One recipe's share of the space.</summary>
/// <param name="Total">Legal combinations times reachable colour buckets.</param>
/// <param name="Combos">
/// The legal variant combinations alone, without colour buckets folded in. <see cref="Total"/>
/// can be zero for two unrelated reasons — the rules exclude every combination, or a layer has
/// no reachable colour buckets — and only this figure tells them apart. A caller must never read
/// a zero <see cref="Total"/> as a rule conflict.
/// </param>
/// <param name="IsExact">
/// Whether <see cref="Total"/> is the real figure rather than a floor. Decided while counting,
/// where it was still known whether the combinations or the buckets gave up: a saturated
/// combination count multiplied by zero buckets lands back under the cap, so this cannot be
/// re-derived afterwards from <c>Total &lt; Cap</c>.
/// </param>
public record RecipeSpace(long Total, long Combos, bool IsExact);

/// <summary>
/// How many distinct DNA a cookbook can produce. <see cref="IsExact"/> is false when the
/// space was too large to count and <see cref="Total"/> saturated at the cap — the real
/// figure is "more than Total", never less.
/// </summary>
public record UniqueSpaceCount(
    long Total,
    bool IsExact,
    long Cap,
    IReadOnlyDictionary<string, RecipeSpace> Recipes)
{
    /// <summary>An unknown recipe id has no space at all, and no space is exactly known.</summary>
    public RecipeSpace this[string recipeId] =>
        Recipes.GetValueOrDefault(recipeId) ?? new RecipeSpace(0, 0, true);
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
        var recipes = new Dictionary<string, RecipeSpace>();

        foreach (var recipe in book.Recipes)
        {
            var (combos, combosExact) = LegalCombinations(recipe, cap);
            var (buckets, bucketsExact) = ColourBuckets(recipe, cap);

            long recipeTotal = Saturate(Multiply(combos, buckets, cap), cap);
            bool recipeExact = combosExact && bucketsExact && recipeTotal < cap;

            recipes[recipe.Manifest.Id] = new RecipeSpace(recipeTotal, combos, recipeExact);
            total = Saturate(total + recipeTotal, cap);
            exact &= recipeExact;
        }

        if (total >= cap) { total = cap; exact = false; }
        return new UniqueSpaceCount(total, exact, cap, recipes);
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

            // A range covers every bucket reachable by ColorRoller.Roll, which samples
            // Min + r*(Max-Min) with r in [0,1) — so Max itself is never rolled.
            var r = entry.Range!;
            var (h0, h1) = BucketSpan(r.HueMin, r.HueMax, hueQ);
            var (s0, s1) = BucketSpan(r.SatMin, r.SatMax, satQ);

            for (long h = h0; h <= h1; h++)
                for (long s = s0; s <= s1; s++)
                {
                    seen.Add((h, s));
                    if (seen.Count >= cap) return (cap, false);
                }
        }

        return (seen.Count, true);
    }

    /// <summary>
    /// The inclusive bucket span reachable on one axis. <see cref="ColorRoller.Roll"/> samples
    /// <c>Min + r*(Max-Min)</c> with <c>r ∈ [0,1)</c>, so the reachable interval is <c>[Min, Max)</c>
    /// and the bucket containing Max is only reachable when Max lands strictly inside it.
    /// A degenerate range (Min == Max) reaches exactly its endpoint, so it keeps that one bucket.
    /// </summary>
    private static (long Lo, long Hi) BucketSpan(double min, double max, int q)
    {
        long lo = (long)Math.Floor(min / q);
        if (max <= min) return (lo, lo);
        // Ceiling(max/q) - 1 drops the bucket Max opens but never enters; when Max falls
        // strictly inside a bucket, Ceiling rounds up into it and the -1 lands back on it.
        return (lo, (long)Math.Ceiling(max / q) - 1);
    }

    private static long Multiply(long a, long b, long cap)
    {
        if (a == 0 || b == 0) return 0;
        if (a > cap / b) return cap;
        return a * b;
    }

    private static long Saturate(long value, long cap) => value > cap ? cap : value;
}
