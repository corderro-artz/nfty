using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class CompositorTests
{
    [Fact]
    public void Top_opaque_layer_covers_bottom()
    {
        using var bottom = new Image<Rgba32>(2, 2, new Rgba32(255, 0, 0, 255));
        using var top = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 255, 255));

        using var result = Compositor.Composite(new Dimensions(2, 2), new[] { bottom, top });

        Assert.Equal(new Rgba32(0, 0, 255, 255), result[0, 0]);
    }

    [Fact]
    public void Transparent_top_reveals_bottom()
    {
        using var bottom = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 255));
        using var top = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 0));

        using var result = Compositor.Composite(new Dimensions(1, 1), new[] { bottom, top });

        Assert.Equal(new Rgba32(255, 0, 0, 255), result[0, 0]);
    }

    [Fact]
    public void Three_layers_composite_in_order_and_partial_alpha_blends()
    {
        using var red = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 255));
        using var green = new Image<Rgba32>(1, 1, new Rgba32(0, 255, 0, 255));
        using var blue = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 255));

        using var opaqueTop = Compositor.Composite(new Dimensions(1, 1), new[] { red, green, blue });
        Assert.Equal(new Rgba32(0, 0, 255, 255), opaqueTop[0, 0]);

        using var transparentBlue = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 0));
        using var transparentTop = Compositor.Composite(new Dimensions(1, 1), new[] { red, green, transparentBlue });
        Assert.Equal(new Rgba32(0, 255, 0, 255), transparentTop[0, 0]);

        using var halfAlphaBlue = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 255, 128));
        using var blended = Compositor.Composite(new Dimensions(1, 1), new[] { red, halfAlphaBlue });
        var px = blended[0, 0];
        Assert.Equal(0, px.G);
        Assert.Equal(255, px.A);
        Assert.InRange(px.R, (byte)110, (byte)145);
        Assert.InRange(px.B, (byte)110, (byte)145);
    }
}
