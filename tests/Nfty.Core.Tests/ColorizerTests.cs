using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class ColorizerTests
{
    [Fact]
    public void Hsv_maps_grayscale_value_to_v_with_rolled_hue()
    {
        using var map = new Image<Rgba32>(1, 1);
        map[0, 0] = new Rgba32(128, 128, 128, 255); // g ~ 0.502

        using var outImg = Colorizer.Apply(map, h: 0, s: 1.0, ColorModel.Hsv);

        var px = outImg[0, 0];
        Assert.Equal(128, px.R);
        Assert.Equal(0, px.G);
        Assert.Equal(0, px.B);
        Assert.Equal(255, px.A);
    }

    [Fact]
    public void Preserves_alpha_and_does_not_mutate_input()
    {
        using var map = new Image<Rgba32>(1, 1);
        map[0, 0] = new Rgba32(200, 200, 200, 64);

        using var outImg = Colorizer.Apply(map, h: 180, s: 0.5, ColorModel.Hsl);

        Assert.Equal(64, outImg[0, 0].A);
        Assert.Equal(new Rgba32(200, 200, 200, 64), map[0, 0]);
    }
}
