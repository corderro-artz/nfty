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
}
