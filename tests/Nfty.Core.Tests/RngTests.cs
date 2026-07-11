using Nfty.Core.Generation;

namespace Nfty.Core.Tests;

public class RngTests
{
    [Fact]
    public void Same_seed_same_sequence()
    {
        var a = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        var b = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        for (int i = 0; i < 10; i++) Assert.Equal(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void Different_seed_different_sequence()
    {
        var a = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        var b = new SplitMix64Rng(SeedHash.ToUlong("soft"));
        Assert.NotEqual(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void Output_in_unit_interval()
    {
        var r = new SplitMix64Rng(1);
        for (int i = 0; i < 1000; i++) Assert.InRange(r.NextDouble(), 0.0, 1.0);
    }
}
