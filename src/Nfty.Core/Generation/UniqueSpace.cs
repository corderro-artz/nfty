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
/// re-derived afterwards from <c>Total &lt; Cap</c>. Also false — with <see cref="Total"/> and
/// <see cref="Combos"/> both zero — when the recipe itself could not be resolved (a layerOrder
/// entry naming a missing ingredient): that recipe's real space is undefined until the book is
/// fixed, not honestly zero, so it must never be read as a rule conflict either.
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
        if (!TryResolveLayers(recipe, out var resolved))
            return (0, false);
        var layers = resolved.Select(Reachable).ToList();

        long product = 1;
        foreach (var layer in layers)
            product = Multiply(product, layer.Variants.Count, cap);

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
            foreach (var v in layer.Variants)
            {
                selection[layer.Id] = v.Id;
                Walk(depth + 1);
            }
            selection.Remove(layer.Id);
        }

        Walk(0);
        return (legal, true);
    }

    /// <summary>One layer reduced to the variants a roll can actually land on.</summary>
    private record ReachableLayer(string Id, IReadOnlyList<Variant> Variants);

    /// <summary>
    /// The variants of an ingredient that <see cref="WeightedRoller.Roll"/> can return. It walks
    /// the entries accumulating weight and returns the first whose running total passes the
    /// sample, so a zero-weight variant never advances that total past its predecessor and is
    /// unreachable — a deliberate way for an author to shelve a variant without deleting it.
    /// Counting it would promise DNA that can never be rolled. Also collapsed to one entry per
    /// id: two variants sharing an id resolve to the same DNA regardless of which one the roller
    /// lands on (<see cref="Dna"/> records the variant id, not which entry produced it), so
    /// counting both separately would promise more DNA than the id space actually holds.
    /// Validator rejects a duplicate variant id outright, so this only guards the same class of
    /// latent bug <see cref="TryResolveLayers"/> guards for ingredient ids.
    /// </summary>
    private static ReachableLayer Reachable(LoadedIngredient ing) =>
        new(ing.Manifest.Id, ing.Manifest.Variants
            .Where(v => v.Weight > 0)
            .DistinctBy(v => v.Id, StringComparer.Ordinal)
            .ToList());

    /// <summary>
    /// Resolves a recipe's layerOrder to its ingredients, in order. <see cref="Count"/> is a
    /// public API the planned GUI calls live while a CookBook is mid-edit — a transiently invalid
    /// book (a duplicate ingredient id, or a layerOrder entry naming a removed ingredient) is a
    /// normal state to see there, not a crash. So this never throws: ingredient ids are resolved
    /// duplicate-tolerantly (last one wins, same as <c>Validator.CheckRecipe</c>'s own ingById),
    /// and a layerOrder entry with no matching ingredient fails the whole recipe back to the
    /// caller as "unresolved" rather than indexing a missing key. Deciding what makes a book
    /// legal is Validator's job, not this one's — this only has to avoid throwing on an illegal
    /// one.
    /// </summary>
    private static bool TryResolveLayers(LoadedRecipe recipe, out List<LoadedIngredient> layers)
    {
        var ingById = new Dictionary<string, LoadedIngredient>();
        foreach (var i in recipe.Ingredients) ingById[i.Manifest.Id] = i;

        var resolved = new List<LoadedIngredient>(recipe.Manifest.LayerOrder.Count);
        foreach (var id in recipe.Manifest.LayerOrder)
        {
            if (!ingById.TryGetValue(id, out var ing))
            {
                layers = new List<LoadedIngredient>();
                return false;
            }
            resolved.Add(ing);
        }

        layers = resolved;
        return true;
    }

    /// <summary>The product of every dynamic layer's distinct quantized (H,S) buckets.</summary>
    private static (long Count, bool Exact) ColourBuckets(LoadedRecipe recipe, long cap)
    {
        if (!TryResolveLayers(recipe, out var layers))
            return (0, false);

        long product = 1;
        bool exact = true;

        foreach (var ing in layers)
        {
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
            // ColorRoller.PickEntry accumulates weight exactly as WeightedRoller does, so a
            // zero-weight entry is never picked and contributes no bucket.
            if (entry.Weight <= 0) continue;

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
