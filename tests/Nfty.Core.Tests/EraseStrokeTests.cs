using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class EraseStrokeTests
{
    private static BrushStroke<GrayPixel> Paint(byte value, int x, int y) =>
        new(new Brush<GrayPixel>(1, new GrayPixel(value, 255)), new[] { (x, y) });

    [Fact]
    public void Erase_sets_alpha_to_zero_and_leaves_value()
    {
        var map = new ValueMap(3, 3);
        Paint(200, 1, 1).Apply(map);
        new EraseStroke<GrayPixel>(1, new[] { (1, 1) }).Apply(map);
        Assert.Equal(0, map.GetAlpha(1, 1));
        Assert.Equal(200, map.GetValue(1, 1)); // value untouched
    }

    [Fact]
    public void Erase_is_undoable()
    {
        var map = new ValueMap(3, 3);
        Paint(200, 1, 1).Apply(map);
        var erase = new EraseStroke<GrayPixel>(1, new[] { (1, 1) });
        erase.Apply(map);
        erase.Undo(map);
        Assert.Equal(255, map.GetAlpha(1, 1)); // restored to the painted alpha
    }
}
