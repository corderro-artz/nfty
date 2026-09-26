using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class DepthFillTests
{
    [Fact]
    public void Every_solid_pixel_gets_the_fill_and_nothing_else_does()
    {
        using var art = new Image<Rgba32>(3, 1);
        art[0, 0] = new Rgba32(1, 2, 3, 255);
        art[1, 0] = new Rgba32(0, 0, 0, 0);
        art[2, 0] = new Rgba32(1, 2, 3, 128);

        var map = DepthFill.All(art, front: 3, back: 1);

        Assert.Equal((3, 1), (map.Front(0, 0), map.Back(0, 0)));
        Assert.Equal((0, 0), (map.Front(1, 0), map.Back(1, 0)));
        Assert.Equal((0, 0), (map.Front(2, 0), map.Back(2, 0)));
    }

    [Fact]
    public void A_fill_of_zero_is_paper()
    {
        using var art = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255));
        Assert.True(DepthFill.All(art, 0, 0).IsFlat);
    }

    [Fact]
    public void The_fill_is_sized_to_the_art()
    {
        using var art = new Image<Rgba32>(4, 3, new Rgba32(1, 2, 3, 255));
        var map = DepthFill.All(art, 1, 1);
        Assert.Equal((4, 3), (map.Width, map.Height));
    }
}
