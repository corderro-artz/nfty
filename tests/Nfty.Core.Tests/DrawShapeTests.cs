using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class DrawShapeTests
{
    private static DrawShape<GrayPixel> Shape(ShapeKind kind, PixelRect bounds, byte value) =>
        new(kind, bounds, new GrayPixel(value, 255));

    [Fact]
    public void Rectangle_fills_exactly_its_bounds()
    {
        var map = new ValueMap(4, 4);
        Shape(ShapeKind.Rectangle, new PixelRect(1, 1, 2, 2), 100).Apply(map);
        Assert.Equal(100, map.GetValue(1, 1));
        Assert.Equal(100, map.GetValue(2, 2));
        Assert.Equal(0, map.GetAlpha(0, 0)); // outside
        Assert.Equal(0, map.GetAlpha(3, 3)); // outside
    }

    [Fact]
    public void Ellipse_fills_center_but_not_corner()
    {
        var map = new ValueMap(5, 5);
        Shape(ShapeKind.Ellipse, new PixelRect(0, 0, 5, 5), 100).Apply(map);
        Assert.Equal(255, map.GetAlpha(2, 2)); // center inside
        Assert.Equal(0, map.GetAlpha(0, 0));   // corner outside the ellipse
    }

    [Fact]
    public void Triangle_fills_bottom_row_but_not_top_corners()
    {
        var map = new ValueMap(5, 5);
        Shape(ShapeKind.Triangle, new PixelRect(0, 0, 5, 5), 100).Apply(map);
        Assert.Equal(255, map.GetAlpha(2, 4)); // bottom-center inside
        Assert.Equal(0, map.GetAlpha(0, 0));   // top-left corner outside
    }
}
