using SixLabors.ImageSharp.PixelFormats;
using Nfty.Model.Render;

namespace Nfty.Model.Tests;

public class RenderTargetTests
{
    [Fact]
    public void A_new_target_allocates_one_color_and_depth_slot_per_pixel()
    {
        var target = new RenderTarget(4, 3);
        Assert.Equal(4, target.Width);
        Assert.Equal(3, target.Height);
        Assert.Equal(12, target.Color.Length);
        Assert.Equal(12, target.Depth.Length);
    }

    [Fact]
    public void Clear_fills_every_pixel_with_the_background_and_resets_depth_to_infinity()
    {
        var target = new RenderTarget(2, 2);
        var background = new Rgba32(10, 20, 30, 255);

        target.Color[0] = new Rgba32(255, 255, 255, 255);
        target.Depth[0] = 0.1f;

        target.Clear(background);

        Assert.All(target.Color, c => Assert.Equal(background, c));
        Assert.All(target.Depth, d => Assert.Equal(float.PositiveInfinity, d));
    }

    [Fact]
    public void Zero_sized_dimensions_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTarget(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTarget(4, 0));
    }
}
