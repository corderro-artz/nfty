using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Core.Generation;

/// <summary>One recipe's share of the space.</summary>
/// <param name="Total">
/// Legal combinations times reachable color buckets. This is the recipe's space in isolation and
/// is recorded even for a shelved (zero-weight) recipe; whether the cookbook can actually roll it
/// is a separate question the cookbook-level <see cref="UniqueSpaceCount.Total"/> answers.
/// </param>
/// <param name="Combos">
/// The legal variant combinations alone, without color buckets folded in. <see cref="Total"/>
/// can be zero for two unrelated reasons — the rules exclude every combination, or a layer has
/// no reachable color buckets — and only this figure tells them apart. A caller must never read
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
public record RecipeSpace(long Total, long Combos, bool IsExact)
{
    /// <inheritdoc cref="UniqueSpaceCount.IsCountable"/>
    public bool IsCountable => IsExact || Total > 0;
}

/// <summary>
/// How many distinct DNA a cookbook can produce. Counts only rollable recipes — a zero-weight
/// recipe is shelved and never rolled, so its space is excluded from <see cref="Total"/> even
/// though it still appears in <see cref="Recipes"/>. <see cref="IsExact"/> is false when the
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

    /// <summary>
    /// Whether this figure means anything to show a user. <see cref="IsExact"/> alone is false for
    /// two unrelated situations: the space saturated the enumeration cap ("more than
    /// <see cref="Total"/>", a real lower bound), and the space is <em>undefined</em> because the
    /// book is invalid in a way that makes the question meaningless. The second reports
    /// <c>Total == 0</c>, and rendering that as "more than 0" states a bound that is technically
    /// true and reads like an answer.
    ///
    /// <para>Every front-end needs this distinction — the CLI's <c>stats</c>, the GUI's identity
    /// card and its per-recipe rows — so it is decided once here instead of three times, differently.
    /// </para>
    /// </summary>
    public bool IsCountable => IsExact || Total > 0;
}

/// <summary>
/// Counts the unique DNA space of a cookbook: per recipe, the legal variant combinations
/// (rules honoured) multiplied by each dynamic layer's quantized color buckets. Static and
/// custom layers contribute a single bucket — a static layer's color is constant, so it
/// adds no cross-asset uniqueness.
/// </summary>
public static class UniqueSpace
{
    /// <summary>How many buckets <see cref="Count"/> enumerates before saturating. Past this the
    /// exact answer stops being worth its cost, and "more than N" is enough to size a run.</summary>
    public const long DefaultCap = 1_000_000;

    /// <summary>Counts the unique DNA a book admits.</summary>
    /// <param name="book">The book to count. May be mid-edit and invalid; this never throws.</param>
    /// <param name="cap">Enumeration limit; see <see cref="DefaultCap"/>.</param>
    /// <returns>The total, whether it is exact, and the per-recipe breakdown.</returns>
    public static UniqueSpaceCount Count(LoadedCookBook book, long cap = DefaultCap)
    {
        long total = 0;
        bool exact = true;
        var recipes = new Dictionary<string, RecipeSpace>();

        foreach (var recipe in book.Recipes)
        {
            // combos x buckets NO LONGER FACTORIZES once a layer can be absent, and the difference
            // is not a rounding error — it is the whole answer. The old split was only valid because
            // every legal selection had every layer present, so every one of them contributed the
            // same product of color buckets. An absent Dynamic layer rolls no color and contributes
            // ONE shape, so the bucket product now depends on which layers a given selection
            // actually has. RecipeSpace does the sum; see its own note.
            var (recipeTotal, combos, recipeExact) = RecipeShapes(recipe, cap);

            // Each recipe's own space is always recorded, so a caller inspecting a shelved recipe
            // still sees what it would contribute if enabled. But the cookbook total counts only
            // rollable recipes: the cookbook rolls a recipe by weight exactly as a layer rolls a
            // variant, so a zero-weight recipe is never rolled and produces no DNA — mirroring the
            // weight>0 filter WeightedRoller applies and Generator.DescribeFailure re-derives.
            recipes[recipe.Manifest.Id] = new RecipeSpace(recipeTotal, combos, recipeExact);
            if (book.Manifest.RecipeWeights.GetValueOrDefault(recipe.Manifest.Id) <= 0)
                continue;
            total = Saturate(total + recipeTotal, cap);
            exact &= recipeExact;
        }

        if (total >= cap) { total = cap; exact = false; }
        return new UniqueSpaceCount(total, exact, cap, recipes);
    }

    /// <summary>
    /// How many distinct quantized colors one colorization admits.
    /// </summary>
    /// <param name="colorization">The block to count. May be mid-edit and illegal; this never throws.</param>
    /// <param name="cap">Enumeration limit; see <see cref="DefaultCap"/>.</param>
    /// <returns>The bucket count, and whether it is exact rather than saturated at the cap.</returns>
    /// <remarks>
    /// This is the same counter <see cref="Count"/> multiplies per dynamic layer, exposed because an
    /// editor showing the user a colors figure must show <em>that</em> figure. The Ingredient editor
    /// used to print the product of the two quantize STEPS, which is not a count of anything: a hue
    /// step of 30 and a saturation step of 20 read as "600 colors" where the layer actually admits
    /// 36, and coarsening a step — which can only ever remove colors — made the number go up.
    /// </remarks>
    public static (long Count, bool Exact) CountColors(Colorization colorization, long cap = DefaultCap) =>
        DistinctBuckets(colorization, cap);

    /// <summary>One layer's choices, as the DNA space sees them.</summary>
    /// <param name="Id">The layer id.</param>
    /// <param name="Variants">The variants a roll can land on. Empty when the layer never appears.</param>
    /// <param name="Buckets">Distinct quantized colors ONE present variant of this layer admits;
    /// 1 for static and custom, which contribute no cross-asset color uniqueness.</param>
    /// <param name="CanBeAbsent">Whether "not there at all" is one of this layer's outcomes.</param>
    private record LayerShapes(string Id, IReadOnlyList<Variant> Variants, long Buckets, bool CanBeAbsent)
    {
        /// <summary>Distinct DNA contributions this layer can make on its own: every present
        /// variant times the colors it can wear, plus one for being absent, which is a single shape
        /// however many colors the layer could have worn had it shown up.</summary>
        public long Shapes => Variants.Count * Buckets + (CanBeAbsent ? 1 : 0);
    }

    /// <summary>
    /// The distinct DNA one recipe admits, and how many legal variant selections underlie it.
    /// </summary>
    /// <param name="recipe">The recipe. May be mid-edit and illegal; this never throws.</param>
    /// <param name="cap">Enumeration limit.</param>
    /// <returns>The DNA total, the legal selection count, and whether both are exact.</returns>
    /// <remarks>
    /// Two paths, and the split is the same one the rules check already made. With no rules the
    /// space FACTORIZES — every layer's choices are independent, so the answer is the product of
    /// each layer's own shape count and nothing has to be walked. With rules it does not, because a
    /// rule can forbid a combination, and now a second thing varies per combination too: which
    /// Dynamic layers are present, and therefore how many color buckets that combination carries.
    /// So the enumeration sums a product per legal selection rather than multiplying one product by
    /// a count.
    /// </remarks>
    private static (long Total, long Combos, bool Exact) RecipeShapes(LoadedRecipe recipe, long cap)
    {
        if (!TryResolveLayers(recipe, out var resolved))
            return (0, 0, false);

        var layers = new List<LayerShapes>(resolved.Count);
        bool exact = true;
        foreach (var ing in resolved)
        {
            double percent = recipe.Manifest.AbsentPercentOf(ing.Manifest.Id);
            bool never = WeightedRoller.AlwaysAbsent(percent);

            long buckets = 1;
            if (ing.Manifest.Kind == LayerKind.Dynamic)
            {
                // A Dynamic layer with no colorization block is illegal, and Validator says so — but
                // this method is documented never to throw, precisely so a GUI can call it on a book
                // that is mid-edit. Report it the way an unresolvable layer is reported: "undefined
                // until the book is fixed", not an honest zero.
                if (ing.Manifest.Colorization is not { } colorization)
                    return (0, 0, false);
                var (b, bExact) = DistinctBuckets(colorization, cap);
                buckets = b;
                exact &= bExact;
            }

            layers.Add(new LayerShapes(
                ing.Manifest.Id,
                // A layer that never appears offers no variants at all, however many it carries.
                never ? Array.Empty<Variant>() : Reachable(ing).Variants,
                buckets,
                percent > 0));
        }

        if (recipe.Manifest.Rules.Count == 0)
        {
            long product = 1;
            long combos = 1;
            foreach (var l in layers)
            {
                product = Multiply(product, l.Shapes, cap);
                combos = Multiply(combos, l.Variants.Count + (l.CanBeAbsent ? 1 : 0), cap);
            }
            // BOTH have to be under the cap, not just the total. A Dynamic layer with no color
            // entries has zero buckets, so a product that saturated on combinations can collapse
            // back to 0 — under the cap — and re-deriving exactness from the total alone would then
            // call a count exact that had already given up. The old split carried that signal in
            // combosExact; folding the two products together is what nearly lost it.
            bool ok = exact && product < cap && combos < cap;
            return (Saturate(product, cap), Saturate(combos, cap), ok);
        }

        // Rules can only remove selections, so the unconstrained product bounds the walk. Only
        // enumerate when that bound is small enough to be worth walking.
        long bound = 1;
        foreach (var l in layers)
            bound = Multiply(bound, l.Variants.Count + (l.CanBeAbsent ? 1 : 0), cap);
        if (bound >= cap) return (cap, cap, false);

        long total = 0;
        long legal = 0;
        var selection = new Dictionary<string, string>();

        void Walk(int depth, long bucketsSoFar)
        {
            if (depth == layers.Count)
            {
                if (!RulesEngine.IsLegal(selection, recipe.Manifest.Rules)) return;
                legal++;
                total = Saturate(total + bucketsSoFar, cap);
                return;
            }

            var layer = layers[depth];
            foreach (var v in layer.Variants)
            {
                selection[layer.Id] = v.Id;
                Walk(depth + 1, Multiply(bucketsSoFar, layer.Buckets, cap));
            }
            selection.Remove(layer.Id);

            // ABSENT IS A CHOICE LIKE ANY OTHER, and it is expressed by the layer having no entry in
            // the selection — which is exactly what RulesEngine already reads as "not present", so
            // exclude rules pass and require rules fail with no new code. It multiplies no buckets:
            // a layer nobody can see wears no color.
            if (layer.CanBeAbsent) Walk(depth + 1, bucketsSoFar);
        }

        Walk(0, 1);
        return (total, legal, exact && total < cap);
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
    /// The variants of an ingredient that
    /// <see cref="WeightedRoller.Roll(WeightedRoller.WeightTable, IRng)"/> can return. It walks
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
    private static (long Count, bool Exact) CountColorBuckets(LoadedRecipe recipe, long cap)
    {
        if (!TryResolveLayers(recipe, out var layers))
            return (0, false);

        long product = 1;
        bool exact = true;

        foreach (var ing in layers)
        {
            // Static and custom layers resolve to one constant bucket each.
            if (ing.Manifest.Kind != LayerKind.Dynamic) continue;

            // A Dynamic layer with no colorization block is illegal, and Validator says so — but
            // this method is documented never to throw, precisely so a GUI can call it on a book
            // that is mid-edit. It used to dereference this null anyway, which is why
            // CollectionReport wrapped the call in a try/catch: the contract was stated in one file
            // and worked around in another. Report it the way an unresolvable layer is reported —
            // "undefined until the book is fixed", not an honest zero.
            if (ing.Manifest.Colorization is not { } colorization)
                return (0, false);

            var (buckets, bucketsExact) = DistinctBuckets(colorization, cap);
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
            // zero-weight entry is never picked and contributes no bucket. A non-finite weight
            // makes the pick undefined rather than zero, so the count is undefined too.
            if (!double.IsFinite(entry.Weight)) return (0, false);
            if (entry.Weight <= 0) continue;

            if (entry.Fixed is not null)
            {
                // An unparseable spec is Validator's problem to report, not this method's to throw
                // on — see the contract on TryResolveLayers.
                RolledColor c;
                try { c = ColorRoller.FromFixed(entry.Fixed, col.Model); }
                catch (FormatException) { return (0, false); }
                seen.Add((ColorBuckets.Hue(c.H, hueQ), ColorBuckets.Sat(c.S, satQ)));
                continue;
            }

            // Neither a fixed spec nor a range: illegal, reported by Validator, and uncountable
            // here rather than a NullReferenceException on the dereference below.
            if (entry.Range is null) return (0, false);

            // A range covers every bucket reachable by ColorRoller.Roll, which samples
            // Min + r*(Max-Min) with r in [0,1) — so Max itself is never rolled.
            var r = entry.Range;
            var (h0, h1) = BucketSpan(r.HueMin, r.HueMax,
                u => ColorRoller.SampleHue(r, u), h => ColorBuckets.Hue(h, hueQ));
            var (s0, s1) = BucketSpan(r.SatMin, r.SatMax,
                u => ColorRoller.SampleSat(r, u), s => ColorBuckets.Sat(s, satQ));

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
    /// <c>Min + r*(Max-Min)</c> with <c>r ∈ [0,1)</c>, so the reachable interval is <c>[Min, Max)</c>.
    /// A degenerate range (Min == Max) reaches exactly its endpoint, so it keeps that one bucket.
    ///
    /// <para><paramref name="sample"/> is the axis's own sampler from <see cref="ColorRoller"/> and
    /// <paramref name="bucket"/> its bucketing function from <see cref="ColorBuckets"/> — the same
    /// two <see cref="Dna"/> is built from. Composing the real functions is the point: this used to
    /// re-derive the reachable interval algebraically from the stored percentages, and that
    /// re-derivation is what let the count and the DNA disagree.</para>
    ///
    /// <para>The step back happens in the <em>sampled</em> space, not the stored percentage —
    /// saturation's <c>/100</c> would swallow it (<c>BitDecrement(30)/100.0</c> rounds straight back
    /// onto <c>0.3</c>). Nor is the top read at <c>BitDecrement(1.0)</c>: the unit sample nearest 1
    /// makes <c>(0 + r*30)/100.0</c> round to exactly <c>0.3</c>, so Max's own bucket becomes
    /// reachable — at probability 2⁻⁵³. Counting it would be true and useless, because
    /// <c>Generate</c> would exhaust its reroll budget trying to deliver it. <c>Count</c> is a
    /// promise about what can actually be produced, so the half-open <c>[Min, Max)</c> reading
    /// stands and the measure-zero edge is deliberately excluded.</para>
    /// </summary>
    private static (long Lo, long Hi) BucketSpan(
        double min, double max, Func<double, double> sample, Func<double, long> bucket)
    {
        long lo = bucket(sample(0.0));
        if (max <= min) return (lo, lo);
        return (lo, Math.Max(lo, bucket(Math.BitDecrement(sample(1.0)))));
    }

    private static long Multiply(long a, long b, long cap)
    {
        if (a == 0 || b == 0) return 0;
        if (a > cap / b) return cap;
        return a * b;
    }

    private static long Saturate(long value, long cap) => value > cap ? cap : value;
}
