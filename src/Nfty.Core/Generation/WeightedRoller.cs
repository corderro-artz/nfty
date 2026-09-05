using System.Globalization;

namespace Nfty.Core.Generation;

/// <summary>Weighted selection with a stable, locale-independent draw order.</summary>
public static class WeightedRoller
{
    /// <summary>
    /// A weight table with its ordering and running totals already resolved. Both are fixed
    /// properties of the cookbook, so a caller that rolls the same table repeatedly should
    /// <see cref="Prepare"/> it once and roll the result, instead of re-sorting and re-summing
    /// on every draw.
    /// </summary>
    public readonly struct WeightTable
    {
        internal WeightTable(string[] keys, double[] cumulative)
        {
            Keys = keys;
            Cumulative = cumulative;
        }

        /// <summary>Keys in ordinal order — the order the draw walks, and why it is locale-independent.</summary>
        internal string[] Keys { get; }

        /// <summary>Running total of the weights in <see cref="Keys"/> order; the last entry is the total.</summary>
        internal double[] Cumulative { get; }

        internal double Total => Cumulative.Length == 0 ? 0 : Cumulative[^1];
    }

    /// <summary>
    /// Resolves the draw order and running totals of a weight table. Pure: an unusable table
    /// (zero or negative total) is not rejected here but on the <see cref="Roll(WeightTable, IRng)"/>
    /// that tries to use it, so preparing a table early cannot move where the error surfaces.
    /// </summary>
    public static WeightTable Prepare(IReadOnlyDictionary<string, double> weights)
    {
        // Ordinal, not the default culture-sensitive comparison: the draw order decides which key
        // a given random number lands on, so a culture-dependent order would make the same seed
        // produce different output on different machines.
        var ordered = weights.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        var keys = new string[ordered.Count];
        var cumulative = new double[ordered.Count];
        double acc = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            keys[i] = ordered[i].Key;
            acc += ordered[i].Value;
            cumulative[i] = acc;
        }
        return new WeightTable(keys, cumulative);
    }

    /// <summary>Prepares and rolls in one step. Prefer <see cref="Prepare"/> plus
    /// <see cref="Roll(WeightTable, IRng)"/> when rolling the same table repeatedly.</summary>
    /// <param name="weights">Weight per key.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <returns>The chosen key.</returns>
    public static string Roll(IReadOnlyDictionary<string, double> weights, IRng rng) =>
        Roll(Prepare(weights), rng);

    /// <summary>
    /// Rolls a prepared table that also carries a chance of choosing NOTHING — a layer that may be
    /// left out of an asset entirely.
    /// </summary>
    /// <param name="table">The prepared weights.</param>
    /// <param name="absentWeight">Weight of the "nothing" outcome, competing with every key in the
    /// table. Zero for a table with no such outcome.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <returns>The chosen key, or <see langword="null"/> for the absent outcome.</returns>
    /// <exception cref="InvalidOperationException">The total is not a finite positive number.</exception>
    /// <remarks>
    /// <b>ONE DRAW, and at <paramref name="absentWeight"/> zero this is the old arithmetic
    /// bit-for-bit.</b> That is the whole design of the overload and it is not a nicety: the absent
    /// outcome is not a key in the table, so the key array and its ordinal order are untouched;
    /// <c>total</c> is <c>0 + table.Total</c>, so <c>r</c> is drawn from exactly the same range;
    /// <c>r &lt; 0</c> is false and <c>r -= 0</c> changes nothing, so the walk that follows sees the
    /// same number it always did. A cookbook that does not use optional layers therefore produces
    /// byte-identical output from the same seed, and every Set ever generated stays reproducible.
    /// <c>AbsentOutcomeTests</c> pins that against a recorded baseline rather than trusting this
    /// paragraph.
    ///
    /// <para>The absent outcome is drawn FIRST, before the keys. Which end it sits at is arbitrary,
    /// but it has to be fixed and stated somewhere, because it decides which outcome a given random
    /// number lands on.</para>
    /// </remarks>
    public static string? Roll(WeightTable table, double absentWeight, IRng rng)
    {
        if (!double.IsFinite(absentWeight) || absentWeight < 0)
            throw new InvalidOperationException(
                "Absent weight must be a finite number, zero or greater, but it is "
                + $"{absentWeight.ToString(CultureInfo.InvariantCulture)}.");

        double total = absentWeight + table.Total;

        // Checked before `total <= 0`, because that comparison is FALSE for NaN — see the note on
        // the single-argument overload, which this one shares its arithmetic with.
        if (!double.IsFinite(total))
            throw new InvalidOperationException(
                $"Total weight must be a finite number, but it is {total.ToString(CultureInfo.InvariantCulture)}.");
        if (total <= 0) throw new InvalidOperationException("Total weight must be positive.");

        double r = rng.NextDouble() * total;
        if (r < absentWeight) return null;
        r -= absentWeight;

        var cumulative = table.Cumulative;
        for (int i = 0; i < cumulative.Length; i++)
            if (r < cumulative[i]) return table.Keys[i];

        // Unreachable for a finite, positive total, as on the overload below.
        return table.Keys[^1];
    }

    /// <summary>
    /// The weight of an "absent" outcome that makes a layer miss <paramref name="percent"/> of the
    /// time against the variants it competes with.
    /// </summary>
    /// <param name="percent">How often the layer is left out, 0..100.</param>
    /// <param name="variantTotal">The total weight of the layer's variants.</param>
    /// <returns>A weight to hand <see cref="Roll(WeightTable, double, IRng)"/>.</returns>
    /// <remarks>
    /// Solving <c>a / (a + W) = p</c> for <c>a</c> gives <c>W · p / (1 − p)</c>. The conversion from
    /// a percent happens HERE and nowhere else, which is the reason the manifest stores a percent
    /// rather than a probability.
    ///
    /// <para>At 100 the formula divides by zero, and the caller must not reach it: a layer that
    /// never appears is not rolled at all. <see cref="AlwaysAbsent"/> is that test, so the check and
    /// the formula cannot drift apart.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The percent is outside 0..100, or is 100 —
    /// which has no finite weight and must be handled by not rolling.</exception>
    public static double AbsentWeight(double percent, double variantTotal)
    {
        if (!double.IsFinite(percent) || percent < 0 || percent >= 100)
            throw new ArgumentOutOfRangeException(nameof(percent),
                $"Absent percent must be at least 0 and less than 100, but it is "
                + $"{percent.ToString(CultureInfo.InvariantCulture)}. 100 means the layer never "
                + "appears, which is not rolled at all — test it with AlwaysAbsent first.");

        return variantTotal * percent / (100 - percent);
    }

    /// <summary>Whether a layer with this absent percent is never rolled at all.</summary>
    /// <param name="percent">How often the layer is left out, 0..100.</param>
    /// <returns>True at 100 or above — shelved, the same meaning a recipe weight of 0 carries.</returns>
    public static bool AlwaysAbsent(double percent) => percent >= 100;

    /// <summary>Rolls a prepared table.</summary>
    /// <param name="table">The prepared weights.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <returns>The chosen key.</returns>
    /// <exception cref="InvalidOperationException">The total is not a finite positive number.</exception>
    public static string Roll(WeightTable table, IRng rng)
    {
        double total = table.Total;

        // Checked before `total <= 0`, because that comparison is FALSE for NaN and would wave it
        // through. A NaN total makes every `r < cumulative[i]` below false too, so the draw would
        // fall out of the loop and return Keys[^1] on every single roll — turning the defensive
        // return into the production path and silently pinning a whole collection to one
        // ordinal-last key. Validator reports non-finite weights now; this is the backstop for a
        // table assembled some other way.
        if (!double.IsFinite(total))
            throw new InvalidOperationException(
                $"Total weight must be a finite number, but it is {total.ToString(CultureInfo.InvariantCulture)}.");
        if (total <= 0) throw new InvalidOperationException("Total weight must be positive.");

        double r = rng.NextDouble() * total;
        var cumulative = table.Cumulative;
        for (int i = 0; i < cumulative.Length; i++)
            if (r < cumulative[i]) return table.Keys[i];

        // Unreachable for a finite, positive total: r = NextDouble() * total is strictly less than
        // total, which is Cumulative[^1]. Kept as a total-function guarantee, not as a fallback.
        return table.Keys[^1];
    }
}
