using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class BrushStrokeTests
{
    [Fact]
    public void Size_one_brush_paints_a_single_pixel_at_full_alpha()
    {
        var map = new ValueMap(3, 3);
        var stroke = new BrushStroke(new Brush(1, 180), new[] { (1, 1) });
        stroke.Apply(map);
        Assert.Equal(180, map.GetValue(1, 1));
        Assert.Equal(255, map.GetAlpha(1, 1));
        Assert.Equal(0, map.GetAlpha(0, 0)); // untouched
    }

    [Fact]
    public void Stroke_clips_to_bounds()
    {
        var map = new ValueMap(2, 2);
        var stroke = new BrushStroke(new Brush(3, 90), new[] { (0, 0) });
        stroke.Apply(map); // disc would spill past the edge; must not throw
        Assert.Equal(90, map.GetValue(0, 0));
    }
}
