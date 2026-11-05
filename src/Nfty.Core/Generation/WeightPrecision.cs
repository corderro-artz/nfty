namespace Nfty.Core.Generation;

/// <summary>Conservative reporting gate for one weighted draw, without changing how it rolls.</summary>
internal static class WeightPrecision
{
    // SplitMix64 emits k / 2^53. A nominal positive branch smaller than one draw quantum cannot
    // retain its stated weight share: it either disappears or receives at least one whole draw.
    private const double DrawQuantum = 1.0 / (1UL << 53);

    /// <summary>Whether every positive branch has a representable share and advances its cumulative
    /// threshold. Weights must be supplied in the roller's actual order.</summary>
    internal static bool CanReport(IEnumerable<double> orderedWeights, double absentPercent = 0)
    {
        if (!double.IsFinite(absentPercent) || absentPercent < 0 || absentPercent >= 100) return false;
        var weights = orderedWeights.ToArray();
        double total = 0;
        foreach (double weight in weights)
        {
            if (!double.IsFinite(weight) || weight < 0) return false;
            double previous = total;
            total += weight;
            if (weight > 0 && total <= previous) return false;
        }
        // A wholly subnormal draw scale can quantize even equal branches into unequal shares.
        // Leave rolling unchanged, but do not report the nominal distribution as exact.
        if (!double.IsNormal(total) || total <= 0) return false;

        double absentWeight = absentPercent > 0 ? WeightedRoller.AbsentWeight(absentPercent, total) : 0;
        double combined = absentWeight + total;
        if (!double.IsNormal(combined) || combined <= 0) return false;
        if (absentPercent > 0 && (!(absentWeight > 0) || absentWeight / combined < DrawQuantum)) return false;
        foreach (double weight in weights)
            if (weight > 0 && weight / combined < DrawQuantum) return false;

        return true;
    }
}
