using System.Numerics;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Core.Generation;

/// <summary>
/// What a counted space actually tells you: an answer, a bound in a stated direction, or nothing.
/// </summary>
/// <remarks>
/// <para><b>This used to be a <c>bool IsExact</c>, and the bool was wrong in one case.</b> Three of
/// the four outcomes below reported <c>false</c> together, and every surface rendered that one way -
/// "more than N" - because two of them really are floors. The third is not: when a recipe has rules
/// and too many combinations to walk, the count gave up and reported the BUDGET as the total, so a
/// book whose rules exclude all but four hundred selections announced "more than 1,000,000". Rules
/// can only REMOVE, so that number was not a lower bound at all; it was an upper one wearing the
/// wrong label.</para>
///
/// <para>Naming the direction fixes it by construction. It is the rule this codebase already applies
/// to colour specs and passphrase sources: state which kind of thing you have rather than leaving a
/// reader to infer it from a flag that cannot carry the distinction.</para>
/// </remarks>
public enum SpaceCertainty
{
    /// <summary>The total IS the figure. Nothing gave up and nothing saturated.</summary>
    Exact,

    /// <summary>
    /// The real figure is LARGER than the total: a colorization filled more buckets than the
    /// enumeration budget allowed collecting, so every product built on it is short. Renders as
    /// "more than N".
    /// </summary>
    /// <remarks>
    /// <b>Saturated arithmetic used to be the other way in here, and is not any more.</b> The totals
    /// are <see cref="System.Numerics.BigInteger"/>, so a product has no ceiling to hit and nothing
    /// about the ARITHMETIC can make a figure inexact. What remains is the one thing that genuinely
    /// gives up — a walk that stopped — which is what this was always meant to name.
    /// </remarks>
    AtLeast,

    /// <summary>
    /// The real figure is NO LARGER than the total — the recipe has rules and more combinations
    /// than the enumeration budget allows walking, so the unconstrained product is reported.
    /// </summary>
    /// <remarks>
    /// It is a true bound because rules only ever remove selections, and removing a selection
    /// removes the colours it would have carried with it. Renders as "at most N", which is a real
    /// fact about the book rather than the floor this case used to claim.
    /// </remarks>
    AtMost,

    /// <summary>
    /// Nothing is known and the total means nothing. The book is invalid in a way that makes the
    /// question meaningless (a layerOrder entry naming no ingredient, a Dynamic layer with no
    /// colorization), or the two bounds above contradict each other.
    /// </summary>
    Unknown,
}

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
/// <param name="Certainty">
/// What <see cref="Total"/> is: the figure, a floor, a ceiling, or nothing. Decided while counting,
/// where it is still known WHICH half gave up — a under-counted bucket set multiplied by zero
/// combinations lands back at zero, so this cannot be re-derived afterwards from the total alone.
/// </param>
public record RecipeSpace(BigInteger Total, BigInteger Combos, SpaceCertainty Certainty)
{
    /// <summary>Whether <see cref="Total"/> is the real figure rather than a bound.</summary>
    public bool IsExact => Certainty == SpaceCertainty.Exact;

    /// <inheritdoc cref="UniqueSpaceCount.IsCountable"/>
    public bool IsCountable => Certainty != SpaceCertainty.Unknown;
}

/// <summary>
/// How many distinct DNA a cookbook can produce. Counts only rollable recipes — a zero-weight
/// recipe is shelved and never rolled, so its space is excluded from <see cref="Total"/> even
/// though it still appears in <see cref="Recipes"/>.
///
/// <para><b><see cref="Total"/> is a <see cref="BigInteger"/>, and that is not future-proofing.</b>
/// A layer's shapes are its variants times its reachable colors, and the recipe's space is those
/// multiplied together — so six layers of ten variants at a 10°/10% quantize is
/// <c>(10 × 360)^6 ≈ 2.2e21</c>, which is two hundred times what a <see cref="long"/> holds. On a
/// <c>long</c> that book reported <c>more than 9,223,372,036,854,775,807</c>: a constant, printed
/// where the headline figure goes, on a card whose entire job is to let an author size a
/// collection. The old ceiling was raised from a million to <c>long.MaxValue</c> for exactly this
/// reason and merely moved the wall; there is no wall now, and the arithmetic cannot make a count
/// inexact at all.</para>
/// </summary>
/// <param name="Total">The distinct DNA the rollable recipes admit between them.</param>
/// <param name="Certainty">What <see cref="Total"/> is: the figure, a floor, a ceiling, or
/// nothing.</param>
/// <param name="Budget">The enumeration budget the count ran under, for a message that has to name
/// the limit somebody would raise.</param>
/// <param name="Recipes">The per-recipe breakdown, shelved recipes included.</param>
public record UniqueSpaceCount(
    BigInteger Total,
    SpaceCertainty Certainty,
    long Budget,
    IReadOnlyDictionary<string, RecipeSpace> Recipes)
{
    /// <summary>Whether <see cref="Total"/> is the real figure rather than a bound.</summary>
    public bool IsExact => Certainty == SpaceCertainty.Exact;

    /// <summary>An unknown recipe id has no space at all, and no space is exactly known.</summary>
    public RecipeSpace this[string recipeId] =>
        Recipes.GetValueOrDefault(recipeId) ?? new RecipeSpace(0, 0, SpaceCertainty.Exact);

    /// <summary>
    /// The combined space of a SUBSET of the book's recipes — the ones a given run can actually
    /// roll.
    /// </summary>
    /// <param name="recipeIds">The recipes in play. An id this count does not know contributes
    /// nothing, exactly as the indexer says.</param>
    /// <returns>Their summed total and combinations, and what that sum is.</returns>
    /// <remarks>
    /// <b>The certainty fold belongs here, not in the caller.</b> <c>Generator</c> needs precisely
    /// this to say how big a space a failing run had, and built it itself — a hand-rolled overflow
    /// guard beside an <c>&amp;=</c> over <c>IsExact</c>. That <c>&amp;=</c> is the operator
    /// <see cref="UniqueSpace.Combine"/> replaces: it cannot express a floor summed with a ceiling,
    /// which is the case that has no answer. The overflow guard is gone with the type that needed
    /// one — a <see cref="BigInteger"/> sum has nothing to clamp, and the follow-up check for "did
    /// this land on the ceiling honestly or by saturating?" is a question that can no longer be
    /// asked.
    /// </remarks>
    public RecipeSpace Over(IEnumerable<string> recipeIds)
    {
        ArgumentNullException.ThrowIfNull(recipeIds);
        BigInteger total = 0;
        BigInteger combos = 0;
        var certainty = SpaceCertainty.Exact;
        foreach (string id in recipeIds)
        {
            var one = this[id];
            total += one.Total;
            combos += one.Combos;
            certainty = UniqueSpace.Combine(certainty, one.Certainty);
        }

        return new RecipeSpace(total, combos, certainty);
    }

    /// <summary>
    /// Whether this figure means anything to show a user. <see cref="IsExact"/> alone is false for
    /// two unrelated situations: a bucket set was under-counted ("more than <see cref="Total"/>", a
    /// real lower bound), and the space is <em>undefined</em> because the book is invalid in a way
    /// that makes the question meaningless. The second reports <c>Total == 0</c>, and rendering that
    /// as "more than 0" states a bound that is technically true and reads like an answer.
    ///
    /// <para>Every front-end needs this distinction — the CLI's <c>stats</c>, the GUI's identity
    /// card and its per-recipe rows — so it is decided once here instead of three times, differently.
    /// </para>
    /// </summary>
    public bool IsCountable => Certainty != SpaceCertainty.Unknown;
}

/// <summary>
/// Counts the unique DNA space of a cookbook: per recipe, the legal variant combinations
/// (rules honoured) multiplied by each dynamic layer's quantized color buckets. Static and
/// custom layers contribute a single bucket — a static layer's color is constant, so it
/// adds no cross-asset uniqueness.
/// </summary>
public static class UniqueSpace
{
    /// <summary>
    /// How many things <see cref="Count"/> will WALK before giving up.
    /// </summary>
    /// <remarks>
    /// <para>This bounds the two places that genuinely enumerate: the per-selection walk a recipe
    /// with rules needs, and the set of distinct colour buckets a colorization fills. Both cost time
    /// and memory proportional to the number, so both need a budget.</para>
    ///
    /// <para><b>It does NOT bound the answer, and there is no longer a second number that does.</b>
    /// The cap was 1,000,000 for everything once, so a book with five million distinct assets
    /// reported "more than 1000000" — a figure exactly computable in a single multiply, because once
    /// the walk has happened the rest is arithmetic. That was split into a budget and a reporting
    /// ceiling of <c>long.MaxValue</c>, which fixed the million and moved the wall to 9.2e18, where
    /// six ordinary layers walk straight through it. A <see cref="BigInteger"/> total has no wall at
    /// all, so the reporting ceiling is GONE rather than raised again: arithmetic is free and is now
    /// treated as free, and <see cref="UniqueSpaceCount.IsExact"/> means exactly one thing —
    /// <b>no enumeration gave up</b> — which is what every caller already read it as.</para>
    /// </remarks>
    public const long DefaultEnumerationBudget = 1_000_000;

    /// <summary>Counts the unique DNA a book admits.</summary>
    /// <param name="book">The book to count. May be mid-edit and invalid; this never throws.</param>
    /// <param name="enumerationBudget">How much walking is allowed; see
    /// <see cref="DefaultEnumerationBudget"/>. Must be positive.</param>
    /// <returns>The total, whether it is exact, and the per-recipe breakdown.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The budget is zero or negative.</exception>
    /// <remarks>
    /// <b>The budget is validated even though this method is otherwise documented never to throw.</b>
    /// Those are different promises: the no-throw contract is about the BOOK, which a GUI hands over
    /// mid-edit and invalid by design, and the budget is the CALLER's own argument. A negative one
    /// used to be laundered into an answer — <see cref="DistinctBuckets"/> tripped its budget check
    /// on the first entry and returned the budget itself, so a layer reported a NEGATIVE bucket
    /// count, which the old saturating multiply then turned into the ceiling because
    /// <c>a &gt; ceiling / b</c> is true for a negative <c>b</c>. The card printed the largest
    /// number it could hold. Refusing the argument is the fix; guarding the multiply against the
    /// consequences of accepting it is not.
    /// </remarks>
    public static UniqueSpaceCount Count(
        LoadedCookBook book,
        long enumerationBudget = DefaultEnumerationBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(enumerationBudget);
        BigInteger total = 0;
        var certainty = SpaceCertainty.Exact;
        var recipes = new Dictionary<string, RecipeSpace>();

        foreach (var recipe in book.Recipes)
        {
            // combos x buckets NO LONGER FACTORIZES once a layer can be absent, and the difference
            // is not a rounding error — it is the whole answer. The old split was only valid because
            // every legal selection had every layer present, so every one of them contributed the
            // same product of color buckets. An absent Dynamic layer rolls no color and contributes
            // ONE shape, so the bucket product now depends on which layers a given selection
            // actually has. RecipeSpace does the sum; see its own note.
            var (recipeTotal, combos, recipeCertainty) =
                RecipeShapes(recipe, enumerationBudget);

            // Each recipe's own space is always recorded, so a caller inspecting a shelved recipe
            // still sees what it would contribute if enabled. But the cookbook total counts only
            // rollable recipes: the cookbook rolls a recipe by weight exactly as a layer rolls a
            // variant, so a zero-weight recipe is never rolled and produces no DNA — mirroring the
            // weight>0 filter WeightedRoller applies and Generator.DescribeFailure re-derives.
            recipes[recipe.Manifest.Id] = new RecipeSpace(recipeTotal, combos, recipeCertainty);
            if (book.Manifest.RecipeWeights.GetValueOrDefault(recipe.Manifest.Id) <= 0)
                continue;
            total += recipeTotal;
            certainty = Combine(certainty, recipeCertainty);
        }

        // Nothing is clamped here and nothing is re-checked afterwards. The budget governs WALKING;
        // summing the recipes is addition, and a BigInteger sum cannot overflow, so the certainty
        // the recipes carry is the whole of what this total is.
        return new UniqueSpaceCount(total, certainty, enumerationBudget, recipes);
    }

    /// <summary>
    /// How many distinct quantized colors one colorization admits.
    /// </summary>
    /// <param name="colorization">The block to count. May be mid-edit and illegal; this never throws.</param>
    /// <param name="enumerationBudget">How many buckets to enumerate before giving up; see
    /// <see cref="DefaultEnumerationBudget"/>. Filling this set is real work, so it is budgeted.</param>
    /// <returns>The bucket count, and whether it is exact rather than saturated.</returns>
    /// <remarks>
    /// This is the same counter <see cref="Count"/> multiplies per dynamic layer, exposed because an
    /// editor showing the user a colors figure must show <em>that</em> figure. The Ingredient editor
    /// used to print the product of the two quantize STEPS, which is not a count of anything: a hue
    /// step of 30 and a saturation step of 20 read as "600 colors" where the layer actually admits
    /// 36, and coarsening a step — which can only ever remove colors — made the number go up.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The budget is zero or negative.</exception>
    public static (long Count, bool Exact) CountColors(
        Colorization colorization, long enumerationBudget = DefaultEnumerationBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(enumerationBudget);
        return DistinctBuckets(colorization, enumerationBudget);
    }

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
        /// <remarks>
        /// <b>Widened before multiplying, not after.</b> This was the one product in the file that
        /// did not go through the saturating helper beside it: <c>int × long</c> in a <c>long</c>,
        /// safe only by an argument about how big each factor could get. The factors are unchanged
        /// — <see cref="Buckets"/> is still bounded by the enumeration budget — but the argument is
        /// no longer load-bearing, because the result has nowhere to wrap to.
        /// </remarks>
        public BigInteger Shapes => (BigInteger)Variants.Count * Buckets + (CanBeAbsent ? 1 : 0);
    }

    /// <summary>
    /// The distinct DNA one recipe admits, and how many legal variant selections underlie it.
    /// </summary>
    /// <param name="recipe">The recipe. May be mid-edit and illegal; this never throws.</param>
    /// <param name="budget">How much walking is allowed.</param>
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
    private static (BigInteger Total, BigInteger Combos, SpaceCertainty Certainty) RecipeShapes(
        LoadedRecipe recipe, long budget)
    {
        if (!TryResolveLayers(recipe, out var resolved))
            return (0, 0, SpaceCertainty.Unknown);

        var layers = new List<LayerShapes>(resolved.Count);
        bool bucketsExact = true;
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
                    return (0, 0, SpaceCertainty.Unknown);
                var (b, bExact) = DistinctBuckets(colorization, budget);
                buckets = b;
                bucketsExact &= bExact;
            }

            layers.Add(new LayerShapes(
                ing.Manifest.Id,
                // A layer that never appears offers no variants at all, however many it carries.
                never ? Array.Empty<Variant>() : Reachable(ing).Variants,
                buckets,
                percent > 0));
        }

        // THE UNCONSTRAINED PRODUCT, computed once and used by both paths. It is free - a product
        // is not enumeration - and it is two different things depending on the path: with no rules
        // it IS the answer, and with rules it is a true UPPER bound on the answer, because a rule
        // can only remove selections and removing a selection removes the colours it carried.
        BigInteger product = 1;
        BigInteger combos = 1;
        foreach (var l in layers)
        {
            product *= l.Shapes;
            combos *= l.Variants.Count + (l.CanBeAbsent ? 1 : 0);
        }

        if (recipe.Manifest.Rules.Count == 0)
        {
            // Nothing is walked here: with no rules the space factorizes. The only way the truth can
            // be LARGER than this product is an under-counted bucket set, which is a floor; the
            // product itself is exact however big it gets.
            return (product, combos, bucketsExact ? SpaceCertainty.Exact : SpaceCertainty.AtLeast);
        }

        // With rules the space does not factorize and has to be walked one selection at a time -
        // THE expensive path, so the BUDGET governs it. Measured in combinations rather than in
        // DNA, because the walk costs one rules check per selection whatever colours those
        // selections carry. That separation is what lets a book with billions of distinct assets
        // over a few thousand combinations count exactly.
        if (combos >= budget)
        {
            // Too many to walk. `product` is a genuine upper bound - but ONLY if the buckets it was
            // built from were themselves counted. If they were not, the product is built on an
            // under-count and bounds the truth in neither direction, which is nothing at all.
            //
            // This case used to return the BUDGET with IsExact false, which every surface rendered
            // as "more than 1,000,000" - a floor, for the one result that is the opposite of one.
            return bucketsExact
                ? (product, combos, SpaceCertainty.AtMost)
                : (0, 0, SpaceCertainty.Unknown);
        }

        BigInteger total = 0;
        long legal = 0;
        var selection = new Dictionary<string, string>();

        void Walk(int depth, BigInteger bucketsSoFar)
        {
            if (depth == layers.Count)
            {
                if (!RulesEngine.IsLegal(selection, recipe.Manifest.Rules)) return;
                legal++;
                total += bucketsSoFar;
                return;
            }

            var layer = layers[depth];
            foreach (var v in layer.Variants)
            {
                selection[layer.Id] = v.Id;
                Walk(depth + 1, bucketsSoFar * layer.Buckets);
            }
            selection.Remove(layer.Id);

            // ABSENT IS A CHOICE LIKE ANY OTHER, and it is expressed by the layer having no entry in
            // the selection — which is exactly what RulesEngine already reads as "not present", so
            // exclude rules pass and require rules fail with no new code. It multiplies no buckets:
            // a layer nobody can see wears no color.
            if (layer.CanBeAbsent) Walk(depth + 1, bucketsSoFar);
        }

        Walk(0, 1);
        // The walk finished, so the selection count is exact and the sum of what it found is too.
        // An under-counted bucket set is the only thing left that can make the total a floor.
        return (total, legal, bucketsExact ? SpaceCertainty.Exact : SpaceCertainty.AtLeast);
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

    /// <summary>
    /// The distinct quantized buckets a colorization can roll.
    /// </summary>
    /// <remarks>
    /// This FILLS A SET, one entry per reachable bucket, so it is the one place left that genuinely
    /// enumerates a colorization, and the budget is what bounds it. A range at a fine quantize can
    /// reach an enormous number of buckets, and the cost of counting them is the count itself — the
    /// reason a bucket count, alone among the figures here, is still a <c>long</c> and still capped.
    /// It is also the only remaining way a space can come back inexact.
    /// </remarks>
    private static (long Count, bool Exact) DistinctBuckets(Colorization col, long budget)
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
                    if (seen.Count >= budget) return (budget, false);
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

    /// <summary>
    /// What a SUM of two spaces is, given what each of them is.
    /// </summary>
    /// <remarks>
    /// A floor plus a floor is a floor; a ceiling plus a ceiling is a ceiling; either plus an exact
    /// figure keeps its direction. <b>A floor plus a ceiling is nothing</b> — the two bounds point
    /// opposite ways and their sum bounds the truth in neither direction, so claiming either would
    /// be inventing one. That combination is the reason this is a function rather than an <c>&amp;=</c>.
    /// </remarks>
    public static SpaceCertainty Combine(SpaceCertainty a, SpaceCertainty b)
    {
        if (a == SpaceCertainty.Unknown || b == SpaceCertainty.Unknown) return SpaceCertainty.Unknown;
        if (a == b) return a;
        if (a == SpaceCertainty.Exact) return b;
        if (b == SpaceCertainty.Exact) return a;
        return SpaceCertainty.Unknown;              // one floor and one ceiling
    }

    // THE SATURATING Multiply AND Add USED TO LIVE HERE, AND THEIR DELETION IS THE POINT OF THE
    // CHANGE RATHER THAN A TIDY-UP. Both were correct for positive operands — `a > ceiling / b` is
    // the standard overflow idiom and it is sound — and both were silently wrong for a negative
    // one, which an unvalidated budget could produce. More to the point, they were machinery for
    // keeping a number inside a type too small to hold the answer. BigInteger is the standard
    // library's answer to that, so the guards go with the ceiling they guarded.
}
