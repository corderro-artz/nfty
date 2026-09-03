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
        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(1, new GrayPixel(50, 255)), new[] { (2, 0) })
            .Apply(map); // (2,0) => value 50, alpha 255
        new FloodFill<GrayPixel>(0, 0, new GrayPixel(220, 255)).Apply(map);
        Assert.Equal(220, map.GetValue(0, 0));
        Assert.Equal(255, map.GetAlpha(0, 0));
        Assert.Equal(220, map.GetValue(1, 0));
        Assert.Equal(50, map.GetValue(2, 0)); // wall untouched
    }
}
