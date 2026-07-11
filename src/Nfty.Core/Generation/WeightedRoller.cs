namespace Nfty.Core.Generation;

public static class WeightedRoller
{
    public static string Roll(IReadOnlyDictionary<string, double> weights, IRng rng)
    {
        var ordered = weights.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        double total = ordered.Sum(kv => kv.Value);
        if (total <= 0) throw new InvalidOperationException("Total weight must be positive.");

        double r = rng.NextDouble() * total, acc = 0;
        foreach (var kv in ordered)
        {
            acc += kv.Value;
            if (r < acc) return kv.Key;
        }
        return ordered[^1].Key;
    }
}
