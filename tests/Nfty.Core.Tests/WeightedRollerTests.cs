using Nfty.Core.Generation;

namespace Nfty.Core.Tests;

public class WeightedRollerTests
{
    [Fact]
    public void Distribution_matches_weights_within_tolerance()
    {
        var weights = new Dictionary<string, double> { ["a"] = 90, ["b"] = 10 };
        var rng = new SplitMix64Rng(42);
        int a = 0;
        for (int i = 0; i < 10000; i++) if (WeightedRoller.Roll(weights, rng) == "a") a++;
        Assert.InRange(a / 10000.0, 0.87, 0.93);
    }

    [Fact]
    public void Single_option_always_selected() =>
        Assert.Equal("only", WeightedRoller.Roll(new Dictionary<string, double> { ["only"] = 5 }, new SplitMix64Rng(1)));

    // --- prepared tables ---
    //
    // The draw order and the running totals are fixed properties of the cookbook, so generation
    // resolves them once per run and rolls the result rather than re-sorting and re-summing on
    // every draw. That is only safe if a prepared table draws exactly what the dictionary drew.

    /// <summary>Returns a fixed sequence of doubles, so a draw's outcome is decided by the test.</summary>
    private sealed class ScriptedRng : IRng
    {
        private readonly double[] _values;
        private int _i;
        public ScriptedRng(params double[] values) => _values = values;
        public double NextDouble() => _values[_i++];
    }

    [Fact]
    public void Prepared_table_draws_the_same_sequence_as_the_dictionary()
    {
        var weights = new Dictionary<string, double>
            { ["a"] = 3, ["b"] = 1, ["c"] = 6, ["d"] = 0.5, ["e"] = 0, ["f"] = 12.25 };
        var table = WeightedRoller.Prepare(weights);
        var direct = new SplitMix64Rng(7);
        var prepared = new SplitMix64Rng(7);

        // Same seed on both sides, so any difference in ordering, totals or threshold comparison
        // shows up as a diverging pick rather than merely a different distribution.
        for (int i = 0; i < 10000; i++)
            Assert.Equal(WeightedRoller.Roll(weights, direct), WeightedRoller.Roll(table, prepared));
    }

    [Fact]
    public void Draw_order_is_ordinal_not_culture_sensitive()
    {
        // Ordinal orders these B, D, a, c (uppercase sorts first); a culture-sensitive comparison
        // orders them a, B, c, D. Each draw below lands squarely inside one quarter of the range,
        // so the key returned reveals which ordering the table used — this is the guarantee that
        // the same seed produces the same output on every machine.
        var weights = new Dictionary<string, double> { ["a"] = 1, ["B"] = 1, ["c"] = 1, ["D"] = 1 };
        var table = WeightedRoller.Prepare(weights);
        var rng = new ScriptedRng(0.1, 0.35, 0.6, 0.9);

        Assert.Equal("B", WeightedRoller.Roll(table, rng));
        Assert.Equal("D", WeightedRoller.Roll(table, rng));
        Assert.Equal("a", WeightedRoller.Roll(table, rng));
        Assert.Equal("c", WeightedRoller.Roll(table, rng));
    }

    [Fact]
    public void Preparing_an_unusable_table_is_not_itself_an_error()
    {
        // Preparing is pure: an all-zero table is rejected by the draw that tries to use it, not
        // by Prepare. Preparing early therefore cannot move where the error surfaces.
        var weights = new Dictionary<string, double> { ["a"] = 0, ["b"] = 0 };

        var table = WeightedRoller.Prepare(weights);

        Assert.Throws<InvalidOperationException>(() => WeightedRoller.Roll(table, new SplitMix64Rng(1)));
        Assert.Throws<InvalidOperationException>(() => WeightedRoller.Roll(weights, new SplitMix64Rng(1)));
    }

    [Fact]
    public void A_zero_weight_entry_is_never_drawn()
    {
        var weights = new Dictionary<string, double> { ["never"] = 0, ["always"] = 1 };
        var table = WeightedRoller.Prepare(weights);
        var rng = new SplitMix64Rng(3);

        for (int i = 0; i < 1000; i++) Assert.Equal("always", WeightedRoller.Roll(table, rng));
    }
}
