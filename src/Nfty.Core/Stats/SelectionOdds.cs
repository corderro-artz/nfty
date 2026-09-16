using System.Globalization;
using System.Numerics;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;

namespace Nfty.Core.Stats;

/// <summary>
/// The chance of one roll producing a particular selection, and what that figure is.
/// </summary>
/// <param name="Probability">The probability, 0..1. Zero whenever <paramref name="Certainty"/> is
/// <see cref="SpaceCertainty.Unknown"/> — a figure that means nothing is not carried as a number
/// that looks like one.</param>
/// <param name="Certainty">
/// <see cref="SpaceCertainty.Exact"/> when nothing gave up; <see cref="SpaceCertainty.AtLeast"/>
/// when the rules could not be walked, which makes the figure a floor on the probability and so a
/// CEILING on the odds ("at most 1 in N"); <see cref="SpaceCertainty.Unknown"/> when the selection
/// could not be mapped onto the book at all. <see cref="SpaceCertainty.AtMost"/> never occurs here
/// and is not special-cased: see <see cref="SelectionOdds"/> for why the error can only go one way.
/// </param>
public record SelectionChance(double Probability, SpaceCertainty Certainty)
{
    /// <summary>Nothing is known about this selection's chance.</summary>
    public static SelectionChance Nothing { get; } = new(0, SpaceCertainty.Unknown);

    /// <summary>Whether <see cref="Probability"/> is the real figure rather than a bound.</summary>
    public bool IsExact => Certainty == SpaceCertainty.Exact;

    /// <summary>Whether there is a figure to show at all.</summary>
    public bool IsKnown => Certainty != SpaceCertainty.Unknown && Probability > 0;

    /// <summary>The same figure as odds: one roll in this many. Infinite when nothing is known.</summary>
    public double OneIn => Probability > 0 ? 1.0 / Probability : double.PositiveInfinity;
}

/// <summary>
/// How likely one asset's exact selection was, computed from the CookBook that produced it.
/// </summary>
/// <remarks>
/// <para><b>The Set browser could already multiply its rarity table together, and that product is
/// an estimate.</b> It assumes the layers roll independently, which is true of a plain book and
/// false of every book that uses incompatibility rules or optional layers — a rule makes a
/// combination impossible and hands its share to the survivors, so the product understates every
/// combination that survives. This is the figure without the assumption.</para>
///
/// <para><b>It is rejection sampling, so the normaliser is the whole point.</b> The generator rolls
/// a recipe, rolls each of its layers, and throws the entire attempt away if the recipe's rules
/// reject it — recipe included, which is why the denominator sums over the book rather than over
/// one recipe. So the chance of a selection is its own unconditional mass divided by the mass of
/// everything legal:</para>
///
/// <code>
///            w_r/W  x  P(layers)
///   P  =  ------------------------------------------------
///          SUM over recipes of  w/W  x  P(it rolls legal)
/// </code>
///
/// <para>With no rules anywhere the denominator is exactly 1 and this reduces to the product the
/// browser was already showing — which is the right relationship between the two: the estimate is
/// not a different model, it is this one with its correction dropped.</para>
///
/// <para><b>The denominator is the only expensive part, and giving up on it costs a DIRECTION, not
/// the answer.</b> Each recipe's legal mass needs a walk of its combinations, budgeted exactly as
/// <see cref="UniqueSpace"/> budgets its own. Every legal mass is at most 1, so the denominator is
/// at most 1, so dropping it can only make the figure too SMALL. An un-walkable book therefore
/// reports the numerator as <see cref="SpaceCertainty.AtLeast"/> — a real bound — and never the
/// upside-down one <see cref="SpaceCertainty"/> exists to have caught.</para>
///
/// <para><b>What it is a chance OF: one roll of this CookBook.</b> Two things deliberately do not
/// enter it. Unique-DNA rejection is a property of a COLLECTION being filled, not of a roll, and
/// distorts nothing until a run approaches the book's whole space. And <c>generate --recipe</c>
/// pins a run to one recipe without recording that anywhere in the Set, so a pinned run's assets
/// are scored against the book's own weights; a recipe the book has SHELVED is the one case that is
/// detectable, and it reports nothing rather than a figure the book contradicts.</para>
/// </remarks>
public static class SelectionOdds
{
    /// <summary>
    /// Resolves everything about a book that does not depend on which asset is being priced — the
    /// per-layer outcome probabilities and, above all, the denominator.
    /// </summary>
    /// <param name="book">The book to price against. Never throws on a broken one; it simply cannot
    /// price anything.</param>
    /// <param name="enumerationBudget">How many combinations may be walked per recipe; see
    /// <see cref="UniqueSpace.DefaultEnumerationBudget"/>.</param>
    /// <returns>The prepared book.</returns>
    /// <remarks>
    /// The same split <see cref="WeightedRoller.Prepare"/> makes, for the same reason: the
    /// denominator is a fixed property of the book and the only part of this that can cost real
    /// time, so a caller pricing a grid of five hundred assets must not pay for it five hundred
    /// times.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The budget is zero or negative.</exception>
    public static BookOdds Prepare(PeekedCookBook book,
        long enumerationBudget = UniqueSpace.DefaultEnumerationBudget)
    {
        ArgumentNullException.ThrowIfNull(book);
        // A non-positive budget is the caller's own argument and is refused, exactly as
        // UniqueSpace.Count refuses one. Left unchecked it does not merely give a wrong answer: the
        // walk below is gated on `combos >= budget`, which a zero or negative budget makes true
        // immediately, so every recipe reports itself unwalkable and the whole book silently
        // downgrades to a floor.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(enumerationBudget);
        return new BookOdds(book, enumerationBudget);
    }

    /// <summary>Prepares and prices in one step. Prefer <see cref="Prepare"/> plus
    /// <see cref="BookOdds.Of(SetItem)"/> when pricing more than one asset.</summary>
    /// <param name="book">The book the Set was cooked from.</param>
    /// <param name="item">The asset, as its metadata records it.</param>
    /// <param name="enumerationBudget">How many combinations may be walked per recipe.</param>
    /// <returns>Its chance.</returns>
    public static SelectionChance Of(PeekedCookBook book, SetItem item,
        long enumerationBudget = UniqueSpace.DefaultEnumerationBudget) =>
        Prepare(book, enumerationBudget).Of(item);

    /// <summary>Prepares and prices in one step. Prefer <see cref="Prepare"/> plus
    /// <see cref="BookOdds.Of(string, IReadOnlyDictionary{string, string}, IReadOnlyCollection{string})"/>
    /// when pricing more than one asset.</summary>
    /// <param name="book">The book to score against.</param>
    /// <param name="recipeId">The recipe's id.</param>
    /// <param name="traits">Variant NAME per layer NAME, for the layers that are present.</param>
    /// <param name="absentLayers">The layer names this selection leaves out entirely.</param>
    /// <param name="enumerationBudget">How many combinations may be walked per recipe.</param>
    /// <returns>Its chance.</returns>
    public static SelectionChance Of(PeekedCookBook book, string recipeId,
        IReadOnlyDictionary<string, string> traits, IReadOnlyCollection<string> absentLayers,
        long enumerationBudget = UniqueSpace.DefaultEnumerationBudget) =>
        Prepare(book, enumerationBudget).Of(recipeId, traits, absentLayers);

    /// <summary>
    /// Renders a chance the way every figure in this project is rendered: invariant, and saying
    /// which direction it is bounded in when it is bounded at all.
    /// </summary>
    /// <param name="chance">The figure.</param>
    /// <param name="asOdds">True for "1 in N", false for a percentage.</param>
    /// <returns>The figure, or an empty string when there is nothing to say.</returns>
    /// <remarks>
    /// Worded HERE rather than in a front-end, for the reason <see cref="SpaceText"/> already
    /// records: the CLI and the GUI printing one number two ways is a defect nothing fails on. Note
    /// the direction INVERTS between the two units — a floor on the probability is a ceiling on the
    /// odds — which is exactly the kind of flip two hand-written copies get wrong in opposite ways.
    /// </remarks>
    public static string Describe(SelectionChance chance, bool asOdds)
    {
        ArgumentNullException.ThrowIfNull(chance);
        if (!chance.IsKnown) return "";
        bool bounded = chance.Certainty == SpaceCertainty.AtLeast;

        if (!asOdds)
        {
            double pct = chance.Probability * 100.0;
            // Enough places to stay non-zero however deep the stack goes: a six-layer asset is
            // routinely a thousandth of a percent, and "0.00%" is not a figure.
            string text = pct >= 0.01
                ? pct.ToString("0.##", CultureInfo.InvariantCulture)
                : pct.ToString("0.######", CultureInfo.InvariantCulture);
            return bounded ? $"at least {text}%" : $"{text}%";
        }

        // PAST A TRILLION THE DIGITS ARE NOISE, and the BOUNDED wording here was wrong outright.
        // `bounded` means the probability is a FLOOR, so the odds are a CEILING: all that is known
        // is `odds <= one`, and `one` is itself at least a trillion. "at most 1 in a trillion"
        // substitutes a trillion for `one` and so states a TIGHTER bound than the arithmetic
        // supports — the true odds could be one in three trillion, which that sentence denies.
        // Saying "over a trillion" on both sides keeps the ceiling honest; only the "at most"
        // distinguishes them, which is exactly the direction this method exists to get right.
        double one = chance.OneIn;
        if (one >= 1_000_000_000_000d)
            return bounded ? "at most 1 in over a trillion" : "1 in over a trillion";
        string odds = Math.Round(one, MidpointRounding.AwayFromZero)
            .ToString("N0", CultureInfo.InvariantCulture);
        return bounded ? $"at most 1 in {odds}" : $"1 in {odds}";
    }
}

/// <summary>
/// One CookBook, resolved down to what pricing an asset against it needs. Build one with
/// <see cref="SelectionOdds.Prepare"/>; see <see cref="SelectionOdds"/> for the arithmetic.
/// </summary>
/// <remarks>
/// Immutable once built and safe to share: nothing here is written after the constructor, so a
/// front-end may prepare one off the UI thread and price from it on any other.
/// </remarks>
public sealed class BookOdds
{
    private readonly long _budget;
    private readonly double _weightTotal;
    private readonly IReadOnlyDictionary<string, double> _weights;
    private readonly Dictionary<string, Resolved> _recipes = new(StringComparer.Ordinal);

    /// <summary>The book's own legal mass, 0..1, or null when it could not be walked.</summary>
    private readonly double? _denominator;

    /// <summary>One recipe, resolved: its rules and its per-layer outcome probabilities.</summary>
    private sealed record Resolved(IReadOnlyList<IncompatibilityRule> Rules, IReadOnlyList<LayerOdds> Layers);

    internal BookOdds(PeekedCookBook book, long budget)
    {
        _budget = budget;
        _weights = book.Manifest.RecipeWeights;

        double total = 0;
        bool usable = true;
        foreach (double w in _weights.Values)
        {
            if (!double.IsFinite(w) || w < 0) { usable = false; break; }
            total += w;
        }
        _weightTotal = usable && total > 0 ? total : 0;
        if (_weightTotal <= 0) return;     // nothing can be priced; every Of() returns Nothing

        foreach (var recipe in book.Recipes)
            if (TryLayers(recipe, out var layers))
                _recipes[recipe.Manifest.Id] = new Resolved(recipe.Manifest.Rules, layers);

        // THE DENOMINATOR, computed once. Exactly 1 for a book with no rules anywhere, which is most
        // books and costs nothing to notice; otherwise one budgeted walk per ruled recipe, which is
        // the only part of this that can cost real time and the reason this type exists.
        double denominator = 0;
        foreach (var (id, w) in _weights)
        {
            // A SHELVED recipe is never rolled, so it is not part of the denominator and its own
            // resolvability does not matter. Every ROLLABLE one is, and a share this cannot account
            // for leaves the denominator unknown rather than merely smaller — which is exactly the
            // case the floor exists for.
            if (!(w > 0)) continue;
            if (!_recipes.TryGetValue(id, out var resolved)) return;
            double mass = LegalMass(resolved.Layers, resolved.Rules, _budget, out bool walked);
            if (!walked) return;                       // _denominator stays null: a floor is all we have
            denominator += mass * w / _weightTotal;
        }

        if (denominator > 0) _denominator = denominator;
    }

    /// <summary>
    /// The chance of rolling the selection one cooked asset carries.
    /// </summary>
    /// <param name="item">The asset, as its metadata records it.</param>
    /// <returns>Its chance, or <see cref="SelectionChance.Nothing"/> when the asset cannot be mapped
    /// onto this book.</returns>
    public SelectionChance Of(SetItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var absent = new HashSet<string>(item.AbsentLayers ?? Array.Empty<string>(), StringComparer.Ordinal);

        // Built from the rarity table's own rows, minus the two kinds that are not layer selections:
        // the recipe (carried separately, as an ID, so it needs no name lookup at all) and the
        // absent layers, whose rows exist to be COUNTED and carry a placeholder in place of a
        // variant name. Absence is read off item.AbsentLayers rather than off that placeholder
        // string deliberately — a variant may legally be named anything, the sentinel included.
        var traits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in item.Rarity)
        {
            if (string.Equals(r.Trait_type, SetWriter.TypeTrait, StringComparison.Ordinal)) continue;
            if (absent.Contains(r.Trait_type)) continue;
            traits[r.Trait_type] = r.Value;
        }

        return Of(item.Recipe, traits, absent);
    }

    /// <summary>
    /// The chance of rolling a selection, named the way a Set's metadata names one.
    /// </summary>
    /// <param name="recipeId">The recipe's <b>id</b> — what <c>nfty/NNNN.json</c> records.</param>
    /// <param name="traits">Variant NAME per layer NAME, for the layers that are present. Names
    /// rather than ids because names are all a Set records; a layer carrying two variants of one
    /// name is scored as either of them, which is the event the metadata actually describes.</param>
    /// <param name="absentLayers">The layer names this selection leaves out entirely.</param>
    /// <returns>Its chance, or <see cref="SelectionChance.Nothing"/> when the selection cannot be
    /// mapped onto this book.</returns>
    public SelectionChance Of(string recipeId, IReadOnlyDictionary<string, string> traits,
        IReadOnlyCollection<string> absentLayers)
    {
        ArgumentNullException.ThrowIfNull(recipeId);
        ArgumentNullException.ThrowIfNull(traits);
        ArgumentNullException.ThrowIfNull(absentLayers);

        if (_weightTotal <= 0) return SelectionChance.Nothing;

        double recipeWeight = _weights.GetValueOrDefault(recipeId);
        // A shelved recipe is never rolled, so an asset claiming one did not come from a plain cook
        // of this book. Nothing is the honest answer; a figure would be one the book contradicts.
        if (!(recipeWeight > 0)) return SelectionChance.Nothing;
        if (!_recipes.TryGetValue(recipeId, out var resolved)) return SelectionChance.Nothing;

        // THE NUMERATOR, restricted to the outcomes the metadata names — normally one per layer, so
        // the walk below is a single legality check unless a layer carries two variants of one name.
        var wanted = new List<LayerOdds>(resolved.Layers.Count);
        var absent = new HashSet<string>(absentLayers, StringComparer.Ordinal);
        foreach (var layer in resolved.Layers)
        {
            if (absent.Contains(layer.Name))
            {
                if (!(layer.AbsentP > 0)) return SelectionChance.Nothing;   // it cannot be left out
                wanted.Add(layer with { Present = Array.Empty<VariantOdds>() });
                continue;
            }

            if (!traits.TryGetValue(layer.Name, out string? variantName)) return SelectionChance.Nothing;
            var matching = layer.Present
                .Where(v => string.Equals(v.Name, variantName, StringComparison.Ordinal))
                .ToList();
            if (matching.Count == 0) return SelectionChance.Nothing;        // not this book's layer
            wanted.Add(layer with { Present = matching, AbsentP = 0 });
        }

        double numerator = LegalMass(wanted, resolved.Rules, _budget, out bool walked);
        if (!walked || !(numerator > 0)) return SelectionChance.Nothing;
        numerator *= recipeWeight / _weightTotal;

        // No denominator means one could not be walked. The missing factor is at most 1, so the
        // numerator alone is a true FLOOR on the probability — and a ceiling on the odds.
        if (_denominator is not { } denominator)
            return new SelectionChance(numerator, SpaceCertainty.AtLeast);

        double p = numerator / denominator;
        return p is > 0 and <= 1
            ? new SelectionChance(p, SpaceCertainty.Exact)
            : SelectionChance.Nothing;
    }

    /// <summary>One outcome of one layer, with the chance a single roll lands on it.</summary>
    private sealed record VariantOdds(string Id, string Name, double P);

    /// <summary>One layer's outcomes: the variants it can roll, and the chance of no layer at all.</summary>
    private sealed record LayerOdds(string Id, string Name, IReadOnlyList<VariantOdds> Present, double AbsentP)
    {
        /// <summary>How many branches a walk takes here.</summary>
        public int Branches => Present.Count + (AbsentP > 0 ? 1 : 0);
    }

    /// <summary>
    /// Resolves a recipe's layers into per-outcome probabilities, in <c>layerOrder</c>.
    /// </summary>
    /// <param name="recipe">The recipe. May be mid-edit and invalid; this reports rather than throws.</param>
    /// <param name="layers">Its layers, empty when it could not be resolved.</param>
    /// <returns>Whether every layer resolved.</returns>
    /// <remarks>
    /// The arithmetic mirrors <c>Generator.PlanLayers</c> and
    /// <see cref="WeightedRoller.AbsentWeight"/> exactly: the absent weight solves <c>a/(a+W) = p</c>,
    /// so the absent outcome's probability IS the stored percent over a hundred, and each variant's
    /// share of what is left is its weight over the layer's whole variant total — the total
    /// including shelved zero-weight variants, which contribute nothing to it either way.
    /// </remarks>
    private static bool TryLayers(PeekedRecipe recipe, out List<LayerOdds> layers)
    {
        layers = new List<LayerOdds>(recipe.Manifest.LayerOrder.Count);
        var byId = new Dictionary<string, IngredientManifest>(StringComparer.Ordinal);
        foreach (var i in recipe.Ingredients) byId[i.Id] = i;

        foreach (string id in recipe.Manifest.LayerOrder)
        {
            if (!byId.TryGetValue(id, out var ing)) { layers.Clear(); return false; }

            double percent = recipe.Manifest.AbsentPercentOf(id);
            if (!double.IsFinite(percent) || percent < 0) { layers.Clear(); return false; }

            if (WeightedRoller.AlwaysAbsent(percent))
            {
                layers.Add(new LayerOdds(id, ing.Name, Array.Empty<VariantOdds>(), 1));
                continue;
            }

            double variantTotal = 0;
            foreach (var v in ing.Variants)
            {
                if (!double.IsFinite(v.Weight) || v.Weight < 0) { layers.Clear(); return false; }
                variantTotal += v.Weight;
            }
            if (!(variantTotal > 0)) { layers.Clear(); return false; }

            double present = 1 - percent / 100.0;
            var outcomes = ing.Variants
                .Where(v => v.Weight > 0)
                .DistinctBy(v => v.Id, StringComparer.Ordinal)
                .Select(v => new VariantOdds(v.Id, v.Name, v.Weight / variantTotal * present))
                .ToList();

            layers.Add(new LayerOdds(id, ing.Name, outcomes, percent / 100.0));
        }

        return true;
    }

    /// <summary>
    /// How much of a roll over these layers satisfies the rules.
    /// </summary>
    /// <param name="layers">The layers, each restricted to the outcomes in play.</param>
    /// <param name="rules">The recipe's rules.</param>
    /// <param name="budget">How many combinations may be walked.</param>
    /// <param name="walked">False when there were more combinations than the budget allows.</param>
    /// <returns>The probability mass of the legal combinations.</returns>
    /// <remarks>
    /// The walk is <see cref="UniqueSpace"/>'s, summing probability where that one sums shapes —
    /// absence included the same way, by leaving the layer OUT of the selection dictionary, which is
    /// already what <see cref="RulesEngine"/> reads as "not present". With no rules there is nothing
    /// to reject, so the mass is the plain product and nothing is walked at all.
    /// </remarks>
    private static double LegalMass(
        IReadOnlyList<LayerOdds> layers, IReadOnlyList<IncompatibilityRule> rules,
        long budget, out bool walked)
    {
        walked = true;
        if (rules.Count == 0)
        {
            double product = 1;
            foreach (var l in layers)
            {
                double mass = l.AbsentP;
                foreach (var v in l.Present) mass += v.P;
                product *= mass;
            }
            return product;
        }

        // A BigInteger, because this was the one product in the project that guarded nothing. It
        // was `long combos *= branches`, safe only by an argument about the factors: the check is
        // inside the loop, so `combos` stays under the budget until the last multiply, and a
        // million times an int cannot overflow. That argument holds for the DEFAULT budget and for
        // no other — `enumerationBudget` is a public parameter, and at a large one the check never
        // trips, `combos` wraps to a negative, the gate opens and Walk recurses over a space that
        // was too big to walk. Widening costs nothing here (a handful of multiplies, once per
        // recipe) and removes the argument rather than restating it.
        BigInteger combos = 1;
        foreach (var l in layers)
        {
            combos *= Math.Max(1, l.Branches);
            if (combos >= budget) { walked = false; return 0; }
        }

        double total = 0;
        var selection = new Dictionary<string, string>(StringComparer.Ordinal);

        void Walk(int depth, double mass)
        {
            if (depth == layers.Count)
            {
                if (RulesEngine.IsLegal(selection, rules)) total += mass;
                return;
            }

            var layer = layers[depth];
            foreach (var v in layer.Present)
            {
                selection[layer.Id] = v.Id;
                Walk(depth + 1, mass * v.P);
            }
            selection.Remove(layer.Id);

            if (layer.AbsentP > 0) Walk(depth + 1, mass * layer.AbsentP);
        }

        Walk(0, 1);
        return total;
    }
}
