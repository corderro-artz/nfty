using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Ticket.Tests;

public class NumericBoundaryTests
{
    public static TheoryData<string, string, int> NonfiniteComponents()
    {
        var data = new TheoryData<string, string, int>();
        foreach (string prefix in new[] { "rgb", "hsv", "hsl" })
        foreach (string component in new[] { "NaN", "Infinity", "-Infinity", "1e999", "-1e999" })
        for (int position = 0; position < 3; position++)
            data.Add(prefix, component, position);
        return data;
    }

    [Theory]
    [MemberData(nameof(NonfiniteComponents))]
    public void Nonfinite_color_components_are_refused_before_conversion(string prefix, string component, int position)
    {
        string[] parts = ["0", "50", "50"];
        parts[position] = component;
        var error = Assert.Throws<FormatException>(() => ColorSpec.Parse(prefix + ":" + string.Join(',', parts)));
        Assert.Contains(component, error.Message, StringComparison.Ordinal);
        Assert.Contains("finite", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Finite_fractional_colors_keep_invariant_rounding_and_hue_wrapping()
    {
        Assert.Equal(new RgbColor(214, 36, 160), ColorSpec.Parse("rgb:213.5,36.25,159.75"));
        Assert.Equal(new RgbColor(0, 255, 0), ColorSpec.Parse("rgb:-10,300,0"));
        Assert.Equal(ColorConvert.HsvToRgb(361.25, 0.5025, 0.755), ColorSpec.Parse("hsv:361.25,50.25,75.5"));
        Assert.Equal(ColorConvert.HslToRgb(-1.25, 0.5025, 0.755), ColorSpec.Parse("hsl:-1.25,50.25,75.5"));
    }

    [Theory]
    [InlineData(50, 1e307)]
    [InlineData(10, 5e307)]
    public void Absent_weight_avoids_intermediate_overflow_when_final_weights_fit(double percent, double variants)
    {
        double absent = WeightedRoller.AbsentWeight(percent, variants);
        Assert.True(double.IsFinite(absent));
        Assert.Equal(variants * (percent / (100 - percent)), absent);
        var table = WeightedRoller.Prepare(new Dictionary<string, double> { ["a"] = variants });
        var rng = new CountingRng(0, Math.BitDecrement(1));
        Assert.Null(WeightedRoller.Roll(table, absent, rng));
        Assert.Equal("a", WeightedRoller.Roll(table, absent, rng));
        Assert.Equal(2, rng.Calls);
    }

    [Fact]
    public void Near_hundred_absence_stays_finite_when_representable()
    {
        double percent = Math.BitDecrement(100.0);
        double absent = WeightedRoller.AbsentWeight(percent, 1e290);
        Assert.True(double.IsFinite(absent));
        Assert.Equal(1e290 * percent / (100 - percent), absent);
    }

    [Theory]
    [InlineData(99, 1e307)]
    [InlineData(50, 1.2e308)]
    public void Genuinely_overflowing_optional_totals_fail_before_consuming_rng(double percent, double variants)
    {
        var table = WeightedRoller.Prepare(new Dictionary<string, double> { ["a"] = variants });
        var rng = new CountingRng();
        var error = Assert.Throws<InvalidOperationException>(() =>
            WeightedRoller.Roll(table, WeightedRoller.AbsentWeight(percent, variants), rng));
        Assert.Contains("finite", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, rng.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Unusable_totals_are_refused_on_both_roll_paths_before_drawing(double weight)
    {
        var table = WeightedRoller.Prepare(new Dictionary<string, double> { ["a"] = weight });
        var rng = new CountingRng();
        Assert.Throws<InvalidOperationException>(() => WeightedRoller.Roll(table, rng));
        Assert.Throws<InvalidOperationException>(() => WeightedRoller.Roll(table, 0, rng));
        Assert.Equal(0, rng.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rounded_weighted_endpoint_never_picks_a_shelved_tail(bool optional)
    {
        var table = WeightedRoller.Prepare(new Dictionary<string, double>
            { ["a"] = double.Epsilon, ["y"] = 0, ["z"] = 0 });
        var rng = new CountingRng(0.75);
        Assert.Equal("a", optional ? WeightedRoller.Roll(table, 0, rng) : WeightedRoller.Roll(table, rng));
        Assert.Equal(1, rng.Calls);
    }

    [Fact]
    public void Rounded_optional_endpoint_picks_the_last_positive_variant()
    {
        var table = WeightedRoller.Prepare(new Dictionary<string, double>
            { ["a"] = double.Epsilon, ["z"] = 0 });
        var rng = new CountingRng(0, 0.875);
        Assert.Null(WeightedRoller.Roll(table, double.Epsilon, rng));
        Assert.Equal("a", WeightedRoller.Roll(table, double.Epsilon, rng));
        Assert.Equal(2, rng.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rounded_absent_only_endpoint_remains_absent(bool empty)
    {
        var weights = empty ? new Dictionary<string, double>() : new Dictionary<string, double> { ["z"] = 0 };
        var rng = new CountingRng(0.75);
        Assert.Null(WeightedRoller.Roll(WeightedRoller.Prepare(weights), double.Epsilon, rng));
        Assert.Equal(1, rng.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rounded_color_endpoint_never_picks_a_shelved_tail_or_changes_draw_cost(bool ranged)
    {
        var range = new ColorRange(100, 101, 50, 51);
        var c = new Colorization(ColorModel.Hsv, 5, 5,
        [
            ranged ? new ColorEntry(double.Epsilon, range, null) : new ColorEntry(double.Epsilon, null, "hex:ff0000"),
            new ColorEntry(0, null, "hex:00ff00"),
        ]);
        var rng = ranged ? new CountingRng(0.75, 0.5, 0.5) : new CountingRng(0.75);
        var actual = ColorRoller.Roll(c, rng);
        Assert.Equal(ranged ? new RolledColor(100.5, 0.505) : new RolledColor(0, 1), actual);
        Assert.Equal(ranged ? 3 : 1, rng.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(73.2)]
    [InlineData(99.5)]
    public void Ordinary_seeded_rolls_match_the_original_arithmetic(double percent)
    {
        double[] weights = [3.125, 1.75, 6.3, 0];
        string[] keys = ["a", "b", "c", "z"];
        double total = weights.Sum();
        double originalAbsent = total * percent / (100 - percent);
        Assert.Equal(originalAbsent, WeightedRoller.AbsentWeight(percent, total));
        var table = WeightedRoller.Prepare(keys.Zip(weights).ToDictionary(p => p.First, p => p.Second));
        var original = new SplitMix64Rng(12345);
        var optional = new SplitMix64Rng(12345);
        var ordinary = new SplitMix64Rng(12345);
        for (int roll = 0; roll < 4096; roll++)
        {
            double r = original.NextDouble() * (originalAbsent + total);
            string? expected = null;
            if (r >= originalAbsent)
            {
                r -= originalAbsent;
                double acc = 0;
                for (int i = 0; i < weights.Length; i++)
                {
                    acc += weights[i];
                    if (r < acc) { expected = keys[i]; break; }
                }
            }
            Assert.Equal(expected, WeightedRoller.Roll(table, originalAbsent, optional));
            if (percent == 0) Assert.Equal(expected, WeightedRoller.Roll(table, ordinary));
        }
    }

    [Fact]
    public void Ordinary_seeded_color_rolls_match_original_entry_and_sampling_arithmetic()
    {
        ColorEntry[] entries =
        [
            new(1.25, new ColorRange(100.25, 135.75, 51.5, 89.25), null),
            new(3.5, null, "hex:ff0000"),
            new(0.75, new ColorRange(200, 201, 20, 21), null),
            new(0, null, "hex:00ff00"),
        ];
        var c = new Colorization(ColorModel.Hsv, 5, 5, entries);
        var original = new SplitMix64Rng(777);
        var actual = new SplitMix64Rng(777);
        for (int roll = 0; roll < 4096; roll++)
        {
            double r = original.NextDouble() * entries.Sum(e => e.Weight), acc = 0;
            var entry = entries[^1];
            foreach (var candidate in entries)
            {
                acc += candidate.Weight;
                if (r < acc) { entry = candidate; break; }
            }
            RolledColor expected;
            if (entry.Fixed is not null) expected = ColorRoller.FromFixed(entry.Fixed, c.Model);
            else
            {
                var range = entry.Range!;
                expected = new RolledColor(
                    range.HueMin + original.NextDouble() * (range.HueMax - range.HueMin),
                    (range.SatMin + original.NextDouble() * (range.SatMax - range.SatMin)) / 100.0);
            }
            Assert.Equal(expected, ColorRoller.Roll(c, actual));
        }
    }

    [Fact]
    public void Floating_point_sampling_can_reach_a_nondegenerate_upper_endpoint()
    {
        var range = new ColorRange(100, 101, 50, 51);
        Assert.Equal(101, ColorRoller.SampleHue(range, Math.BitDecrement(1.0)));
        Assert.Equal(0.51, ColorRoller.SampleSat(range, Math.BitDecrement(1.0)));
    }

    private sealed class CountingRng(params double[] values) : IRng
    {
        public int Calls { get; private set; }
        public double NextDouble() => values[Calls++];
    }
}
