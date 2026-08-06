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
