using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class BrushStrokeTests
{
    private static BrushStroke<GrayPixel> Stroke(int size, byte value, params (int x, int y)[] path) =>
        new(new Brush<GrayPixel>(size, new GrayPixel(value, 255)), path);

    [Fact]
    public void Size_one_brush_paints_a_single_pixel_at_full_alpha()
    {
        var map = new ValueMap(3, 3);
        Stroke(1, 180, (1, 1)).Apply(map);
        Assert.Equal(180, map.GetValue(1, 1));
        Assert.Equal(255, map.GetAlpha(1, 1));
        Assert.Equal(0, map.GetAlpha(0, 0)); // untouched
    }

    [Fact]
    public void Stroke_clips_to_bounds()
    {
        var map = new ValueMap(2, 2);
        Stroke(3, 90, (0, 0)).Apply(map); // disc would spill past the edge; must not throw
        Assert.Equal(90, map.GetValue(0, 0));
    }
}
