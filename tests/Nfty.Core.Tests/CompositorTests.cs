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
}
