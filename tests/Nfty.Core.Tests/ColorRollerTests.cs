using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Core.Tests;

public class ColorRollerTests
{
    [Fact]
    public void Fixed_entry_yields_its_hue_saturation()
    {
        var c = new Colorization(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, null, "hsv:200,50,80") });
        var rolled = ColorRoller.Roll(c, new SplitMix64Rng(7));
        Assert.InRange(rolled.H, 199.0, 201.0);
        Assert.InRange(rolled.S, 0.49, 0.51);
    }

    [Fact]
    public void Range_entry_samples_within_bounds()
    {
        var c = new Colorization(ColorModel.Hsv, 5, 5, new[] { new ColorEntry(1, new ColorRange(175, 195, 60, 90), null) });
        for (int i = 0; i < 200; i++)
        {
            var rolled = ColorRoller.Roll(c, new SplitMix64Rng((ulong)i));
            Assert.InRange(rolled.H, 175.0, 195.0);
            Assert.InRange(rolled.S, 0.60, 0.90);
        }
    }
}
