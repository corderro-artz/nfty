using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class FloodFillTests
{
    [Fact]
    public void Fills_contiguous_matching_region_only()
    {
        var map = new ValueMap(3, 1);
        // left two pixels are (0,0); right pixel is a different value/alpha "wall"
        new BrushStroke(new Brush(1, 50), new[] { (2, 0) }).Apply(map); // (2,0) => value 50, alpha 255
        new FloodFill(0, 0, 220).Apply(map);
        Assert.Equal(220, map.GetValue(0, 0));
        Assert.Equal(255, map.GetAlpha(0, 0));
        Assert.Equal(220, map.GetValue(1, 0));
        Assert.Equal(50, map.GetValue(2, 0)); // wall untouched
    }
}
