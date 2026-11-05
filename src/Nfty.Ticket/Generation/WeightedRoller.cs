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
    /// <param name="weights">Relative weights keyed by identifier. Callers must validate that each
    /// weight is finite and nonnegative; preparation preserves ordinal summation order.</param>
    /// <returns>An ordinally ordered table. A nonfinite or nonpositive total is refused when rolled.</returns>
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
    /// <param name="weights">Finite, nonnegative relative weight per key.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <returns>The chosen key.</returns>
    /// <exception cref="InvalidOperationException">The total is not a finite positive number.</exception>
    public static string Roll(IReadOnlyDictionary<string, double> weights, IRng rng) =>
        Roll(Prepare(weights), rng);

    /// <summary>
    /// Rolls a prepared table that also carries a chance of choosing NOTHING — a layer that may be
    /// left out of an asset entirely.
    /// </summary>
    /// <param name="table">Weights from <see cref="Prepare"/>, with finite, nonnegative individual weights.</param>
    /// <param name="absentWeight">Weight of the "nothing" outcome, competing with every key in the
    /// table. Zero for a table with no such outcome.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <returns>The chosen key, or <see langword="null"/> for the absent outcome.</returns>
    /// <exception cref="InvalidOperationException">The absent weight is negative or nonfinite,
    /// or the combined total is not a finite positive number.</exception>
    /// <remarks>
    /// Consumes exactly one draw. The absent outcome is not a key in the table, so its ordinal
    /// order is untouched. At zero absent weight, <c>0 + table.Total</c>, <c>r &lt; 0</c> and
    /// <c>r -= 0</c> preserve ordinary draw arithmetic bit-for-bit. Both overloads share the same
    /// scan and corrected floating-point endpoint handling: a rounded upper endpoint cannot
    /// select a zero-weight key. <c>AbsentLayerTests</c> pins zero-absence equivalence and
    /// <c>TicketFreezeTests</c> guards the recorded ordinary collection baseline.
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

        return RollCore(table, absentWeight, rng);
    }

    private static string? RollCore(WeightTable table, double absentWeight, IRng rng)
    {
        double total = absentWeight + table.Total;

        // Checked before `total <= 0`, because that comparison is FALSE for NaN and would wave it
        // through. A NaN total also makes every threshold comparison false, silently pinning all
        // rolls to the fallback outcome. This shared boundary runs before either overload draws.
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

        // Floating-point multiplication or the absent subtraction can round r to the endpoint.
        // Use the last interval with positive width, never a trailing zero-weight key. With no
        // positive key interval, a valid positive total belongs entirely to the absent outcome.
        for (int i = cumulative.Length - 1; i >= 0; i--)
            if (cumulative[i] > (i == 0 ? 0 : cumulative[i - 1])) return table.Keys[i];
        return null;
    }

    /// <summary>
    /// The weight of an "absent" outcome that makes a layer miss <paramref name="percent"/> of the
    /// time against the variants it competes with.
    /// </summary>
    /// <param name="percent">How often the layer is left out, 0..100.</param>
    /// <param name="variantTotal">The finite, nonnegative total weight of validated variants.</param>
    /// <returns>A weight to hand <see cref="Roll(WeightTable, double, IRng)"/>. An unrepresentable
    /// weight is positive infinity; rolling refuses it or an overflowing combined total.</returns>
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

        double product = variantTotal * percent;
        // Keep ordinary arithmetic bit-for-bit: reassociate only when this intermediate overflows.
        // Dividing the percent first avoids an unnecessary infinity when the final weight fits.
        return double.IsInfinity(product) && double.IsFinite(variantTotal)
            ? variantTotal * (percent / (100 - percent))
            : product / (100 - percent);
    }

    /// <summary>Whether a layer with this absent percent is never rolled at all.</summary>
    /// <param name="percent">How often the layer is left out, 0..100.</param>
    /// <returns>True at 100 or above — shelved, the same meaning a recipe weight of 0 carries.</returns>
    public static bool AlwaysAbsent(double percent) => percent >= 100;

    /// <summary>Rolls a prepared table.</summary>
    /// <param name="table">Weights from <see cref="Prepare"/>, with finite, nonnegative individual weights.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <returns>The chosen key.</returns>
    /// <exception cref="InvalidOperationException">The total is not a finite positive number.</exception>
    /// <remarks>Consumes exactly one RNG draw. Floating-point rounding at the upper endpoint
    /// selects the last positive interval rather than a trailing zero-weight key.</remarks>
    public static string Roll(WeightTable table, IRng rng) => RollCore(table, 0, rng)!;
}
